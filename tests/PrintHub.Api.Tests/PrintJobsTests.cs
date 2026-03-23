using System.Net;
using System.Net.Http.Json;
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

            Assert.True(File.Exists(Path.Combine(tempRootPath, "jobs.json")));

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

    private static async Task<PrintJobDto> WaitForCompletionAsync(HttpClient client, string jobId)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/print-jobs/{jobId}");
            request.Headers.Add(ApiKeyHeaderName, ApiKey);

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<PrintJobDto>(TestJson.SerializerOptions);

            if (payload is not null && payload.Status is PrintJobStatus.Completed or PrintJobStatus.Failed)
            {
                return payload;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        throw new TimeoutException($"Print job '{jobId}' did not finish within the expected time.");
    }
}
