using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using PrintHub.Contracts.PrintJobs;
using PrintHub.Core.Models;
using PrintHub.Core.Repositories;

namespace PrintHub.Infrastructure.Repositories;

public sealed class SqlitePrintJobStore : IPrintJobStore
{
    private static readonly JsonSerializerOptions LegacySerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _databasePath;
    private bool _initialized;

    public SqlitePrintJobStore(string contentRootPath, string databasePath)
    {
        if (string.IsNullOrWhiteSpace(contentRootPath))
        {
            throw new ArgumentException("Content root path is required.", nameof(contentRootPath));
        }

        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        _databasePath = ResolvePath(contentRootPath, databasePath);
        SQLitePCL.Batteries_V2.Init();
    }

    public async ValueTask AddAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await InsertAsync(connection, job, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> DeleteAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID is required.", nameof(jobId));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM PrintJobs
                WHERE Id = $Id;
                """;
            command.Parameters.AddWithValue("$Id", jobId.Trim());

            var rowsDeleted = await command.ExecuteNonQueryAsync(cancellationToken);
            return rowsDeleted > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<PrintJob?> GetAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID is required.", nameof(jobId));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    Id,
                    PrinterName,
                    Copies,
                    DocumentSourceType,
                    DocumentFormat,
                    DocumentFileName,
                    DocumentStoredPath,
                    DocumentSizeBytes,
                    DocumentSourceUrl,
                    ParentJobId,
                    Status,
                    CreatedAtUnixMs,
                    StartedAtUnixMs,
                    CompletedAtUnixMs,
                    ErrorMessage
                FROM PrintJobs
                WHERE Id = $Id;
                """;
            command.Parameters.AddWithValue("$Id", jobId.Trim());

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? Map(reader)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyCollection<PrintJob>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    Id,
                    PrinterName,
                    Copies,
                    DocumentSourceType,
                    DocumentFormat,
                    DocumentFileName,
                    DocumentStoredPath,
                    DocumentSizeBytes,
                    DocumentSourceUrl,
                    ParentJobId,
                    Status,
                    CreatedAtUnixMs,
                    StartedAtUnixMs,
                    CompletedAtUnixMs,
                    ErrorMessage
                FROM PrintJobs
                ORDER BY CreatedAtUnixMs DESC;
                """;

            var jobs = new List<PrintJob>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                jobs.Add(Map(reader));
            }

            return jobs;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask UpdateAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE PrintJobs
                SET
                    PrinterName = $PrinterName,
                    Copies = $Copies,
                    DocumentSourceType = $DocumentSourceType,
                    DocumentFormat = $DocumentFormat,
                    DocumentFileName = $DocumentFileName,
                    DocumentStoredPath = $DocumentStoredPath,
                    DocumentSizeBytes = $DocumentSizeBytes,
                    DocumentSourceUrl = $DocumentSourceUrl,
                    ParentJobId = $ParentJobId,
                    Status = $Status,
                    CreatedAtUnixMs = $CreatedAtUnixMs,
                    StartedAtUnixMs = $StartedAtUnixMs,
                    CompletedAtUnixMs = $CompletedAtUnixMs,
                    ErrorMessage = $ErrorMessage
                WHERE Id = $Id;
                """;
            AddParameters(command, job);

            var rowsUpdated = await command.ExecuteNonQueryAsync(cancellationToken);

            if (rowsUpdated == 0)
            {
                throw new InvalidOperationException($"A print job with ID '{job.Id}' does not exist.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_databasePath)
            ?? throw new InvalidOperationException("Jobs database path does not have a directory component.");
        Directory.CreateDirectory(directory);

        var migratedJobs = await TryLoadLegacyJsonAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS PrintJobs (
                    Id TEXT NOT NULL PRIMARY KEY,
                    PrinterName TEXT NULL,
                    Copies INTEGER NOT NULL,
                    DocumentSourceType TEXT NOT NULL,
                    DocumentFormat TEXT NOT NULL,
                    DocumentFileName TEXT NOT NULL,
                    DocumentStoredPath TEXT NOT NULL,
                    DocumentSizeBytes INTEGER NOT NULL,
                    DocumentSourceUrl TEXT NULL,
                    ParentJobId TEXT NULL,
                    Status TEXT NOT NULL,
                    CreatedAtUnixMs INTEGER NOT NULL,
                    StartedAtUnixMs INTEGER NULL,
                    CompletedAtUnixMs INTEGER NULL,
                    ErrorMessage TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_PrintJobs_CreatedAtUnixMs
                    ON PrintJobs (CreatedAtUnixMs DESC);
                """;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await EnsureColumnAsync(connection, "PrintJobs", "ParentJobId", "TEXT NULL", cancellationToken);

        if (migratedJobs.Count > 0)
        {
            foreach (var job in migratedJobs)
            {
                await InsertOrReplaceAsync(connection, job, cancellationToken);
            }
        }

        _initialized = true;
    }

    private async Task<IReadOnlyCollection<PrintJob>> TryLoadLegacyJsonAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_databasePath) || !await LooksLikeJsonAsync(_databasePath, cancellationToken))
        {
            return [];
        }

        await using var stream = File.OpenRead(_databasePath);

        try
        {
            var payload = await JsonSerializer.DeserializeAsync<StoredPrintJob[]>(
                stream,
                LegacySerializerOptions,
                cancellationToken) ?? [];

            var backupPath = ReserveLegacyBackupPath(_databasePath);
            stream.Close();
            File.Move(_databasePath, backupPath, overwrite: true);

            return payload
                .Select(job => job.ToModel())
                .ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Could not migrate legacy print jobs from '{_databasePath}'.",
                exception);
        }
    }

    private static async Task<bool> LooksLikeJsonAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, leaveOpen: true);
        var buffer = new char[1];

        while (await reader.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) > 0)
        {
            var character = buffer[0];
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            return character is '[' or '{';
        }

        return false;
    }

    private static string ReserveLegacyBackupPath(string databasePath)
    {
        var candidate = $"{databasePath}.legacy.json";

        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var directory = Path.GetDirectoryName(databasePath) ?? AppContext.BaseDirectory;
        var fileName = Path.GetFileName(databasePath);
        return Path.Combine(directory, $"{fileName}.legacy-{Guid.NewGuid():N}.json");
    }

    private SqliteConnection CreateConnection() =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

    private static async Task InsertAsync(
        SqliteConnection connection,
        PrintJob job,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PrintJobs (
                Id,
                PrinterName,
                Copies,
                DocumentSourceType,
                DocumentFormat,
                DocumentFileName,
                DocumentStoredPath,
                DocumentSizeBytes,
                DocumentSourceUrl,
                ParentJobId,
                Status,
                CreatedAtUnixMs,
                StartedAtUnixMs,
                CompletedAtUnixMs,
                ErrorMessage
            )
            VALUES (
                $Id,
                $PrinterName,
                $Copies,
                $DocumentSourceType,
                $DocumentFormat,
                $DocumentFileName,
                $DocumentStoredPath,
                $DocumentSizeBytes,
                $DocumentSourceUrl,
                $ParentJobId,
                $Status,
                $CreatedAtUnixMs,
                $StartedAtUnixMs,
                $CompletedAtUnixMs,
                $ErrorMessage
            );
            """;
        AddParameters(command, job);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException($"A print job with ID '{job.Id}' already exists.", exception);
        }
    }

    private static async Task InsertOrReplaceAsync(
        SqliteConnection connection,
        PrintJob job,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO PrintJobs (
                Id,
                PrinterName,
                Copies,
                DocumentSourceType,
                DocumentFormat,
                DocumentFileName,
                DocumentStoredPath,
                DocumentSizeBytes,
                DocumentSourceUrl,
                ParentJobId,
                Status,
                CreatedAtUnixMs,
                StartedAtUnixMs,
                CompletedAtUnixMs,
                ErrorMessage
            )
            VALUES (
                $Id,
                $PrinterName,
                $Copies,
                $DocumentSourceType,
                $DocumentFormat,
                $DocumentFileName,
                $DocumentStoredPath,
                $DocumentSizeBytes,
                $DocumentSourceUrl,
                $ParentJobId,
                $Status,
                $CreatedAtUnixMs,
                $StartedAtUnixMs,
                $CompletedAtUnixMs,
                $ErrorMessage
            );
            """;
        AddParameters(command, job);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameters(SqliteCommand command, PrintJob job)
    {
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$Id", job.Id);
        command.Parameters.AddWithValue("$PrinterName", (object?)job.PrinterName ?? DBNull.Value);
        command.Parameters.AddWithValue("$Copies", job.Copies);
        command.Parameters.AddWithValue("$DocumentSourceType", job.Document.SourceType.ToString());
        command.Parameters.AddWithValue("$DocumentFormat", job.Document.Format.ToString());
        command.Parameters.AddWithValue("$DocumentFileName", job.Document.FileName);
        command.Parameters.AddWithValue("$DocumentStoredPath", job.Document.StoredPath);
        command.Parameters.AddWithValue("$DocumentSizeBytes", job.Document.SizeBytes);
        command.Parameters.AddWithValue("$DocumentSourceUrl", (object?)job.Document.SourceUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$ParentJobId", (object?)job.ParentJobId ?? DBNull.Value);
        command.Parameters.AddWithValue("$Status", job.Status.ToString());
        command.Parameters.AddWithValue("$CreatedAtUnixMs", job.CreatedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$StartedAtUnixMs", job.StartedAt?.ToUnixTimeMilliseconds() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$CompletedAtUnixMs", job.CompletedAt?.ToUnixTimeMilliseconds() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$ErrorMessage", (object?)job.ErrorMessage ?? DBNull.Value);
    }

    private static PrintJob Map(SqliteDataReader reader)
    {
        var sourceType = Enum.Parse<DocumentSourceType>(reader.GetString(reader.GetOrdinal("DocumentSourceType")));
        var format = Enum.Parse<PrintDocumentFormat>(reader.GetString(reader.GetOrdinal("DocumentFormat")));
        var status = Enum.Parse<PrintJobStatus>(reader.GetString(reader.GetOrdinal("Status")));
        var document = PrintDocument.CreateStored(
            sourceType,
            format,
            reader.GetString(reader.GetOrdinal("DocumentFileName")),
            reader.GetString(reader.GetOrdinal("DocumentStoredPath")),
            reader.GetInt64(reader.GetOrdinal("DocumentSizeBytes")),
            reader.IsDBNull(reader.GetOrdinal("DocumentSourceUrl"))
                ? null
                : reader.GetString(reader.GetOrdinal("DocumentSourceUrl")));

        return PrintJob.Restore(
            reader.GetString(reader.GetOrdinal("Id")),
            reader.IsDBNull(reader.GetOrdinal("PrinterName"))
                ? null
                : reader.GetString(reader.GetOrdinal("PrinterName")),
            reader.GetInt32(reader.GetOrdinal("Copies")),
            document,
            reader.IsDBNull(reader.GetOrdinal("ParentJobId"))
                ? null
                : reader.GetString(reader.GetOrdinal("ParentJobId")),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("CreatedAtUnixMs"))),
            status,
            ReadNullableUnixMilliseconds(reader, "StartedAtUnixMs"),
            ReadNullableUnixMilliseconds(reader, "CompletedAtUnixMs"),
            reader.IsDBNull(reader.GetOrdinal("ErrorMessage"))
                ? null
                : reader.GetString(reader.GetOrdinal("ErrorMessage")));
    }

    private static DateTimeOffset? ReadNullableUnixMilliseconds(SqliteDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(ordinal));
    }

    private static string ResolvePath(string contentRootPath, string path) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(contentRootPath, path));

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(reader.GetOrdinal("name")), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await reader.CloseAsync();

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record StoredPrintJob(
        string Id,
        string? PrinterName,
        int Copies,
        StoredPrintDocument Document,
        PrintJobStatus Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        string? ErrorMessage,
        string? ParentJobId = null)
    {
        public PrintJob ToModel() =>
            PrintJob.Restore(
                Id,
                PrinterName,
                Copies,
                Document.ToModel(),
                ParentJobId,
                CreatedAt,
                Status,
                StartedAt,
                CompletedAt,
                ErrorMessage);
    }

    private sealed record StoredPrintDocument(
        DocumentSourceType SourceType,
        PrintDocumentFormat Format,
        string FileName,
        string StoredPath,
        long SizeBytes,
        string? SourceUrl)
    {
        public PrintDocument ToModel() =>
            PrintDocument.CreateStored(
                SourceType,
                Format,
                FileName,
                StoredPath,
                SizeBytes,
                SourceUrl);
    }
}
