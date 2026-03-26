using System.Net;
using System.Net.Http.Json;
using System.Text;
using PrintHub.Api.Tests.Infrastructure;
using PrintHub.Contracts.PrintJobs;

namespace PrintHub.Api.Tests;

public sealed class PrintJobsTests
{
    private const string ApiKey = "test-api-key";
    private const string ApiKeyHeaderName = "X-PrintHub-Api-Key";
    private const string PdfBase64 = "JVBERi0xLjQKMSAwIG9iajw8Pj5lbmRvYmoKdHJhaWxlcjw8Pj4KJSVFT0YK";

    [Fact]
    public async Task CreatePrintJob_Base64_CompletesAndAppearsInHistory()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/print-jobs")
        {
            Content = JsonContent.Create(
                new CreatePrintJobRequest(
                    PrinterName: "Office Printer",
                    Copies: 1,
                    Document: new PrintDocumentRequest(
                        DocumentSourceType.Base64,
                        PrintDocumentFormat.Pdf,
                        Url: null,
                        Data: PdfBase64,
                        FileName: "integration-smoke.pdf")),
                options: TestJson.SerializerOptions)
        };
        createRequest.Headers.Add(ApiKeyHeaderName, ApiKey);

        var createResponse = await client.SendAsync(createRequest);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdJob = await createResponse.Content.ReadFromJsonAsync<CreatePrintJobResponse>(TestJson.SerializerOptions);

        Assert.NotNull(createdJob);

        var completedJob = await WaitForCompletionAsync(client, createdJob!.JobId);

        Assert.Equal(PrintJobStatus.Completed, completedJob.Status);
        Assert.Equal("Office Printer", completedJob.PrinterName);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/print-jobs?limit=10");
        listRequest.Headers.Add(ApiKeyHeaderName, ApiKey);

        var listResponse = await client.SendAsync(listRequest);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var jobs = await listResponse.Content.ReadFromJsonAsync<PrintJobDto[]>(TestJson.SerializerOptions);

        Assert.NotNull(jobs);
        Assert.Contains(jobs!, job => job.JobId == createdJob.JobId);

        var storedFiles = Directory.GetFiles(Path.Combine(factory.TempRootPath, "documents"));
        Assert.Single(storedFiles);
    }

    [Fact]
    public async Task CompletedPrintJob_IsRestoredAfterHostRestart()
    {
        var tempRootPath = Path.Combine(
            Path.GetTempPath(),
            $"printhub-api-restart-{Guid.NewGuid():N}");

        try
        {
            string jobId;

            using (var firstFactory = new PrintHubApiFactory(tempRootPath: tempRootPath))
            using (var firstClient = firstFactory.CreateClient())
            using (var createRequest = new HttpRequestMessage(HttpMethod.Post, "/print-jobs"))
            {
                createRequest.Headers.Add(ApiKeyHeaderName, ApiKey);
                createRequest.Content = JsonContent.Create(
                    new CreatePrintJobRequest(
                        PrinterName: "Office Printer",
                        Copies: 1,
                        Document: new PrintDocumentRequest(
                            DocumentSourceType.Base64,
                            PrintDocumentFormat.Pdf,
                            Url: null,
                            Data: PdfBase64,
                            FileName: "restart-smoke.pdf")),
                    options: TestJson.SerializerOptions);

                var createResponse = await firstClient.SendAsync(createRequest);
                createResponse.EnsureSuccessStatusCode();

                var createdJob = await createResponse.Content.ReadFromJsonAsync<CreatePrintJobResponse>(TestJson.SerializerOptions);
                Assert.NotNull(createdJob);

                jobId = createdJob!.JobId;

                var completedJob = await WaitForCompletionAsync(firstClient, jobId);
                Assert.Equal(PrintJobStatus.Completed, completedJob.Status);
            }

            Assert.True(File.Exists(Path.Combine(tempRootPath, "jobs.db")));

            using var secondFactory = new PrintHubApiFactory(tempRootPath: tempRootPath);
            using var secondClient = secondFactory.CreateClient();
            using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/print-jobs?limit=10");
            listRequest.Headers.Add(ApiKeyHeaderName, ApiKey);

            var listResponse = await secondClient.SendAsync(listRequest);

            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

            var jobs = await listResponse.Content.ReadFromJsonAsync<PrintJobDto[]>(TestJson.SerializerOptions);

            Assert.NotNull(jobs);
            Assert.Contains(jobs!, job => job.JobId == jobId && job.Status == PrintJobStatus.Completed);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRootPath))
                {
                    Directory.Delete(tempRootPath, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task PendingPrintJob_CanBeCanceled_WhileAnotherJobIsProcessing()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        var longRunningJob = await CreateBase64JobAsync(client, copies: 15, fileName: "long-running.pdf");
        var pendingJob = await CreateBase64JobAsync(client, copies: 1, fileName: "pending-cancel.pdf");

        using var cancelRequest = new HttpRequestMessage(HttpMethod.Post, $"/print-jobs/{pendingJob.JobId}/cancel");
        cancelRequest.Headers.Add(ApiKeyHeaderName, ApiKey);

        var cancelResponse = await client.SendAsync(cancelRequest);

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var canceledJob = await cancelResponse.Content.ReadFromJsonAsync<PrintJobDto>(TestJson.SerializerOptions);

        Assert.NotNull(canceledJob);
        Assert.Equal(PrintJobStatus.Canceled, canceledJob!.Status);

        var confirmedCanceledJob = await WaitForStatusAsync(client, pendingJob.JobId, PrintJobStatus.Canceled);

        Assert.Equal(PrintJobStatus.Canceled, confirmedCanceledJob.Status);

        var completedLongRunningJob = await WaitForCompletionAsync(client, longRunningJob.JobId);
        Assert.Equal(PrintJobStatus.Completed, completedLongRunningJob.Status);
    }

    [Fact]
    public async Task CompletedPrintJob_CanBeRetried_AsANewJob()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        var originalJob = await CreateBase64JobAsync(client, copies: 1, fileName: "retry-source.pdf");
        var completedJob = await WaitForCompletionAsync(client, originalJob.JobId);

        Assert.Equal(PrintJobStatus.Completed, completedJob.Status);

        using var retryRequest = new HttpRequestMessage(HttpMethod.Post, $"/print-jobs/{originalJob.JobId}/retry");
        retryRequest.Headers.Add(ApiKeyHeaderName, ApiKey);

        var retryResponse = await client.SendAsync(retryRequest);

        Assert.Equal(HttpStatusCode.Created, retryResponse.StatusCode);

        var retriedJob = await retryResponse.Content.ReadFromJsonAsync<CreatePrintJobResponse>(TestJson.SerializerOptions);

        Assert.NotNull(retriedJob);
        Assert.NotEqual(originalJob.JobId, retriedJob!.JobId);
        Assert.Equal(PrintJobStatus.Pending, retriedJob.Status);

        var completedRetriedJob = await WaitForCompletionAsync(client, retriedJob.JobId);

        Assert.Equal(PrintJobStatus.Completed, completedRetriedJob.Status);
        Assert.Equal(completedJob.PrinterName, completedRetriedJob.PrinterName);
    }

    [Fact]
    public async Task CompletedPrintJob_CanBeDeleted_FromHistory()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        var createdJob = await CreateBase64JobAsync(client, copies: 1, fileName: "delete-history.pdf");
        var completedJob = await WaitForCompletionAsync(client, createdJob.JobId);

        Assert.Equal(PrintJobStatus.Completed, completedJob.Status);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/print-jobs/{createdJob.JobId}");
        deleteRequest.Headers.Add(ApiKeyHeaderName, ApiKey);

        var deleteResponse = await client.SendAsync(deleteRequest);

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/print-jobs/{createdJob.JobId}");
        getRequest.Headers.Add(ApiKeyHeaderName, ApiKey);

        var getResponse = await client.SendAsync(getRequest);

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Cleanup_RemovesOnlyFinishedJobs_AndKeepsActiveOnes()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        var processingJob = await CreateBase64JobAsync(client, copies: 15, fileName: "cleanup-processing.pdf");
        var cancelableJob = await CreateBase64JobAsync(client, copies: 1, fileName: "cleanup-canceled.pdf");

        using (var cancelRequest = new HttpRequestMessage(HttpMethod.Post, $"/print-jobs/{cancelableJob.JobId}/cancel"))
        {
            cancelRequest.Headers.Add(ApiKeyHeaderName, ApiKey);
            var cancelResponse = await client.SendAsync(cancelRequest);
            Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        }

        await WaitForStatusAsync(client, cancelableJob.JobId, PrintJobStatus.Canceled);

        using var cleanupRequest = new HttpRequestMessage(HttpMethod.Post, "/print-jobs/cleanup");
        cleanupRequest.Headers.Add(ApiKeyHeaderName, ApiKey);

        var cleanupResponse = await client.SendAsync(cleanupRequest);

        Assert.Equal(HttpStatusCode.OK, cleanupResponse.StatusCode);

        var cleanupPayload = await cleanupResponse.Content.ReadFromJsonAsync<CleanupPrintJobsResponse>(TestJson.SerializerOptions);

        Assert.NotNull(cleanupPayload);
        Assert.Equal(1, cleanupPayload!.DeletedCount);

        using (var deletedJobRequest = new HttpRequestMessage(HttpMethod.Get, $"/print-jobs/{cancelableJob.JobId}"))
        {
            deletedJobRequest.Headers.Add(ApiKeyHeaderName, ApiKey);
            var deletedJobResponse = await client.SendAsync(deletedJobRequest);
            Assert.Equal(HttpStatusCode.NotFound, deletedJobResponse.StatusCode);
        }

        var currentProcessingJob = await WaitForStatusAsync(client, processingJob.JobId, PrintJobStatus.Processing, PrintJobStatus.Completed);
        Assert.NotEqual(PrintJobStatus.Canceled, currentProcessingJob.Status);

        var completedProcessingJob = await WaitForCompletionAsync(client, processingJob.JobId);
        Assert.Equal(PrintJobStatus.Completed, completedProcessingJob.Status);
    }

    [Fact]
    public async Task Queue_CanBePaused_AndResumed_ForPendingJobs()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        using (var pauseRequest = new HttpRequestMessage(HttpMethod.Post, "/print-jobs/queue/pause"))
        {
            pauseRequest.Headers.Add(ApiKeyHeaderName, ApiKey);
            var pauseResponse = await client.SendAsync(pauseRequest);
            Assert.Equal(HttpStatusCode.OK, pauseResponse.StatusCode);
        }

        var queuedJob = await CreateBase64JobAsync(client, copies: 1, fileName: "paused-queue.pdf");

        await Task.Delay(TimeSpan.FromMilliseconds(700));

        using (var pendingJobRequest = new HttpRequestMessage(HttpMethod.Get, $"/print-jobs/{queuedJob.JobId}"))
        {
            pendingJobRequest.Headers.Add(ApiKeyHeaderName, ApiKey);
            var pendingJobResponse = await client.SendAsync(pendingJobRequest);
            pendingJobResponse.EnsureSuccessStatusCode();

            var pendingJob = await pendingJobResponse.Content.ReadFromJsonAsync<PrintJobDto>(TestJson.SerializerOptions);
            Assert.NotNull(pendingJob);
            Assert.Equal(PrintJobStatus.Pending, pendingJob!.Status);
        }

        using (var statusRequest = new HttpRequestMessage(HttpMethod.Get, "/print-jobs/queue"))
        {
            statusRequest.Headers.Add(ApiKeyHeaderName, ApiKey);
            var statusResponse = await client.SendAsync(statusRequest);
            statusResponse.EnsureSuccessStatusCode();

            var status = await statusResponse.Content.ReadFromJsonAsync<PrintQueueStatusDto>(TestJson.SerializerOptions);
            Assert.NotNull(status);
            Assert.True(status!.IsPaused);
            Assert.Equal(1, status.QueuedCount);
        }

        using (var resumeRequest = new HttpRequestMessage(HttpMethod.Post, "/print-jobs/queue/resume"))
        {
            resumeRequest.Headers.Add(ApiKeyHeaderName, ApiKey);
            var resumeResponse = await client.SendAsync(resumeRequest);
            Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);
        }

        var completedJob = await WaitForCompletionAsync(client, queuedJob.JobId);
        Assert.Equal(PrintJobStatus.Completed, completedJob.Status);
    }

    [Fact]
    public async Task ClearQueue_CancelsOnlyPendingQueuedJobs()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        var processingJob = await CreateBase64JobAsync(client, copies: 15, fileName: "queue-clear-processing.pdf");
        var queuedJob = await CreateBase64JobAsync(client, copies: 1, fileName: "queue-clear-pending.pdf");

        using var clearRequest = new HttpRequestMessage(HttpMethod.Post, "/print-jobs/queue/clear");
        clearRequest.Headers.Add(ApiKeyHeaderName, ApiKey);

        var clearResponse = await client.SendAsync(clearRequest);

        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);

        var clearPayload = await clearResponse.Content.ReadFromJsonAsync<ClearPrintQueueResponse>(TestJson.SerializerOptions);

        Assert.NotNull(clearPayload);
        Assert.Equal(1, clearPayload!.CanceledCount);

        var canceledQueuedJob = await WaitForStatusAsync(client, queuedJob.JobId, PrintJobStatus.Canceled);
        Assert.Equal(PrintJobStatus.Canceled, canceledQueuedJob.Status);

        var currentProcessingJob = await WaitForStatusAsync(client, processingJob.JobId, PrintJobStatus.Processing, PrintJobStatus.Completed);
        Assert.NotEqual(PrintJobStatus.Canceled, currentProcessingJob.Status);

        using (var statusRequest = new HttpRequestMessage(HttpMethod.Get, "/print-jobs/queue"))
        {
            statusRequest.Headers.Add(ApiKeyHeaderName, ApiKey);
            var statusResponse = await client.SendAsync(statusRequest);
            statusResponse.EnsureSuccessStatusCode();

            var status = await statusResponse.Content.ReadFromJsonAsync<PrintQueueStatusDto>(TestJson.SerializerOptions);
            Assert.NotNull(status);
            Assert.False(status!.IsPaused);
            Assert.Equal(0, status.QueuedCount);
        }

        var completedProcessingJob = await WaitForCompletionAsync(client, processingJob.JobId);
        Assert.Equal(PrintJobStatus.Completed, completedProcessingJob.Status);
    }

    [Fact]
    public async Task LegacyJsonHistory_IsMigrated_ToSqliteStore()
    {
        var tempRootPath = Path.Combine(
            Path.GetTempPath(),
            $"printhub-api-legacy-jobs-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempRootPath);

            var legacyJobsPath = Path.Combine(tempRootPath, "jobs.db");
            var legacyDocumentPath = Path.Combine(tempRootPath, "legacy-document.pdf");
            await File.WriteAllBytesAsync(legacyDocumentPath, Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF"));

            var legacyPayload = $$"""
            [
              {
                "id": "legacy-job-1",
                "printerName": "Office Printer",
                "copies": 1,
                "document": {
                  "sourceType": "base64",
                  "format": "pdf",
                  "fileName": "legacy-document.pdf",
                  "storedPath": "{{legacyDocumentPath.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                  "sizeBytes": 13,
                  "sourceUrl": null
                },
                "status": "completed",
                "createdAt": "2026-03-26T12:00:00+00:00",
                "startedAt": "2026-03-26T12:00:01+00:00",
                "completedAt": "2026-03-26T12:00:02+00:00",
                "errorMessage": null
              }
            ]
            """;

            await File.WriteAllTextAsync(legacyJobsPath, legacyPayload);

            using var factory = new PrintHubApiFactory(tempRootPath: tempRootPath);
            using var client = factory.CreateClient();
            using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/print-jobs?limit=10");
            listRequest.Headers.Add(ApiKeyHeaderName, ApiKey);

            var listResponse = await client.SendAsync(listRequest);

            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

            var jobs = await listResponse.Content.ReadFromJsonAsync<PrintJobDto[]>(TestJson.SerializerOptions);

            Assert.NotNull(jobs);
            Assert.Contains(jobs!, job => job.JobId == "legacy-job-1" && job.Status == PrintJobStatus.Completed);
            Assert.True(File.Exists(Path.Combine(tempRootPath, "jobs.db")));
            Assert.True(File.Exists(Path.Combine(tempRootPath, "jobs.db.legacy.json")));
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRootPath))
                {
                    Directory.Delete(tempRootPath, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<PrintJobDto> WaitForCompletionAsync(HttpClient client, string jobId)
    {
        return await WaitForStatusAsync(client, jobId, PrintJobStatus.Completed, PrintJobStatus.Failed);
    }

    private static async Task<PrintJobDto> WaitForStatusAsync(
        HttpClient client,
        string jobId,
        params PrintJobStatus[] statuses)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/print-jobs/{jobId}");
            request.Headers.Add(ApiKeyHeaderName, ApiKey);

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<PrintJobDto>(TestJson.SerializerOptions);

            if (payload is not null && statuses.Contains(payload.Status))
            {
                return payload;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        throw new TimeoutException($"Print job '{jobId}' did not reach any of the expected statuses: {string.Join(", ", statuses)}.");
    }

    private static async Task<CreatePrintJobResponse> CreateBase64JobAsync(
        HttpClient client,
        int copies,
        string fileName,
        string printerName = "Office Printer")
    {
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/print-jobs")
        {
            Content = JsonContent.Create(
                new CreatePrintJobRequest(
                    PrinterName: printerName,
                    Copies: copies,
                    Document: new PrintDocumentRequest(
                        DocumentSourceType.Base64,
                        PrintDocumentFormat.Pdf,
                        Url: null,
                        Data: PdfBase64,
                        FileName: fileName)),
                options: TestJson.SerializerOptions)
        };
        createRequest.Headers.Add(ApiKeyHeaderName, ApiKey);

        var createResponse = await client.SendAsync(createRequest);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdJob = await createResponse.Content.ReadFromJsonAsync<CreatePrintJobResponse>(TestJson.SerializerOptions);

        Assert.NotNull(createdJob);
        return createdJob!;
    }
}
