using System.Text;

namespace PrintHub.Api.Logging;

public sealed class PrintHubFileLoggerProvider : ILoggerProvider
{
    private readonly PrintHubFileLogWriter _writer;

    public PrintHubFileLoggerProvider(string contentRootPath, PrintHubFileLoggerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        ArgumentNullException.ThrowIfNull(options);

        _writer = new PrintHubFileLogWriter(contentRootPath, options);
    }

    public ILogger CreateLogger(string categoryName) =>
        new PrintHubFileLogger(categoryName, _writer);

    public void Dispose() => _writer.Dispose();

    private sealed class PrintHubFileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly PrintHubFileLogWriter _writer;

        public PrintHubFileLogger(string categoryName, PrintHubFileLogWriter writer)
        {
            _categoryName = string.IsNullOrWhiteSpace(categoryName) ? "Application" : categoryName.Trim();
            _writer = writer;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);

            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            _writer.Write(logLevel, _categoryName, eventId, message, exception);
        }
    }

    private sealed class PrintHubFileLogWriter : IDisposable
    {
        private readonly object _sync = new();
        private readonly string _directoryPath;
        private readonly string _currentLogFilePath;
        private readonly string _fileNameWithoutExtension;
        private readonly string _fileExtension;
        private readonly long _maxFileSizeBytes;
        private readonly int _retainedFileCountLimit;

        private StreamWriter? _writer;
        private long _currentFileSizeBytes;

        public PrintHubFileLogWriter(string contentRootPath, PrintHubFileLoggerOptions options)
        {
            _directoryPath = ResolveDirectoryPath(contentRootPath, options.DirectoryPath);
            _currentLogFilePath = ResolveCurrentLogFilePath(_directoryPath, options.FileName);
            _fileNameWithoutExtension = Path.GetFileNameWithoutExtension(_currentLogFilePath);
            _fileExtension = Path.GetExtension(_currentLogFilePath);
            _maxFileSizeBytes = options.MaxFileSizeBytes > 0
                ? options.MaxFileSizeBytes
                : throw new ArgumentOutOfRangeException(nameof(options.MaxFileSizeBytes));
            _retainedFileCountLimit = options.RetainedFileCountLimit >= 0
                ? options.RetainedFileCountLimit
                : throw new ArgumentOutOfRangeException(nameof(options.RetainedFileCountLimit));
        }

        public void Write(
            LogLevel logLevel,
            string categoryName,
            EventId eventId,
            string? message,
            Exception? exception)
        {
            var payload = FormatEntry(logLevel, categoryName, eventId, message, exception);

            lock (_sync)
            {
                EnsureWriter();
                RotateIfNeeded(payload);
                _writer!.WriteLine(payload);
                _currentFileSizeBytes += Encoding.UTF8.GetByteCount(payload + Environment.NewLine);
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        private void EnsureWriter()
        {
            if (_writer is not null)
            {
                return;
            }

            Directory.CreateDirectory(_directoryPath);

            var stream = new FileStream(
                _currentLogFilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read);

            _writer = new StreamWriter(stream, Encoding.UTF8)
            {
                AutoFlush = true
            };
            _currentFileSizeBytes = stream.Length;
        }

        private void RotateIfNeeded(string payload)
        {
            var nextEntrySizeBytes = Encoding.UTF8.GetByteCount(payload + Environment.NewLine);

            if (_currentFileSizeBytes + nextEntrySizeBytes <= _maxFileSizeBytes)
            {
                return;
            }

            _writer?.Dispose();
            _writer = null;

            if (File.Exists(_currentLogFilePath))
            {
                File.Move(_currentLogFilePath, BuildArchiveFilePath());
            }

            DeleteExpiredArchives();
            EnsureWriter();
        }

        private string BuildArchiveFilePath()
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            var candidatePath = Path.Combine(_directoryPath, $"{_fileNameWithoutExtension}-{timestamp}{_fileExtension}");
            var suffix = 0;

            while (File.Exists(candidatePath))
            {
                suffix++;
                candidatePath = Path.Combine(
                    _directoryPath,
                    $"{_fileNameWithoutExtension}-{timestamp}-{suffix}{_fileExtension}");
            }

            return candidatePath;
        }

        private void DeleteExpiredArchives()
        {
            if (_retainedFileCountLimit == int.MaxValue)
            {
                return;
            }

            var archivedFiles = Directory.EnumerateFiles(_directoryPath, $"{_fileNameWithoutExtension}-*{_fileExtension}")
                .OrderByDescending(Path.GetFileName)
                .Skip(_retainedFileCountLimit);

            foreach (var archivedFile in archivedFiles)
            {
                File.Delete(archivedFile);
            }
        }

        private static string FormatEntry(
            LogLevel logLevel,
            string categoryName,
            EventId eventId,
            string? message,
            Exception? exception)
        {
            var builder = new StringBuilder()
                .Append(DateTimeOffset.UtcNow.ToString("O"))
                .Append(' ')
                .Append('[')
                .Append(ToShortLevel(logLevel))
                .Append("] ")
                .Append(categoryName);

            if (eventId.Id != 0 || !string.IsNullOrWhiteSpace(eventId.Name))
            {
                builder.Append(" (");

                if (!string.IsNullOrWhiteSpace(eventId.Name))
                {
                    builder.Append(eventId.Name);

                    if (eventId.Id != 0)
                    {
                        builder.Append(':');
                    }
                }

                if (eventId.Id != 0)
                {
                    builder.Append(eventId.Id);
                }

                builder.Append(')');
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                builder.Append(": ").Append(message.Trim());
            }

            if (exception is not null)
            {
                builder.AppendLine().Append(exception);
            }

            return builder.ToString();
        }

        private static string ToShortLevel(LogLevel logLevel) =>
            logLevel switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "NON"
            };

        private static string ResolveDirectoryPath(string contentRootPath, string directoryPath)
        {
            var normalizedPath = string.IsNullOrWhiteSpace(directoryPath)
                ? PrintHubFileLoggerOptions.DefaultDirectoryPath
                : directoryPath.Trim();

            return Path.IsPathRooted(normalizedPath)
                ? normalizedPath
                : Path.GetFullPath(Path.Combine(contentRootPath, normalizedPath));
        }

        private static string ResolveCurrentLogFilePath(string directoryPath, string fileName)
        {
            var normalizedFileName = string.IsNullOrWhiteSpace(fileName)
                ? PrintHubFileLoggerOptions.DefaultFileName
                : Path.GetFileName(fileName.Trim());

            if (string.IsNullOrWhiteSpace(normalizedFileName))
            {
                throw new ArgumentException("Log file name is required.", nameof(fileName));
            }

            return Path.Combine(directoryPath, normalizedFileName);
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }
}
