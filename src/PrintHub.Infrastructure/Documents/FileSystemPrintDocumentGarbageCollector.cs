using PrintHub.Core.Documents;
using PrintHub.Core.Repositories;
using PrintHub.Core.Settings;
using PrintHub.Infrastructure.Paths;

namespace PrintHub.Infrastructure.Documents;

public sealed class FileSystemPrintDocumentGarbageCollector : IPrintDocumentGarbageCollector
{
    private readonly IPrintJobStore _store;
    private readonly IPrintHubSettingsService _settingsService;
    private readonly PrintHubAppDataPaths _appDataPaths;

    public FileSystemPrintDocumentGarbageCollector(
        IPrintJobStore store,
        IPrintHubSettingsService settingsService,
        PrintHubAppDataPaths appDataPaths)
    {
        _store = store;
        _settingsService = settingsService;
        _appDataPaths = appDataPaths;
    }

    public async ValueTask<PrintDocumentGarbageCollectionResult> CollectAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetAsync(cancellationToken);
        var storageDirectory = _appDataPaths.ResolveDataPath(settings.StorageDirectory, "data/documents");

        if (!Directory.Exists(storageDirectory))
        {
            return new PrintDocumentGarbageCollectionResult(0, 0);
        }

        var comparer = OperatingSystem.IsLinux()
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        var storageRoot = Path.GetFullPath(storageDirectory);
        var jobs = await _store.ListAsync(cancellationToken);
        var referencedPaths = jobs
            .Select(job => job.Document.StoredPath)
            .Where(path => IsInsideDirectory(storageRoot, path))
            .Select(Path.GetFullPath)
            .ToHashSet(comparer);

        var deletedFilesCount = 0;
        long reclaimedBytes = 0;

        foreach (var filePath in Directory.EnumerateFiles(storageRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullPath = Path.GetFullPath(filePath);
            if (referencedPaths.Contains(fullPath))
            {
                continue;
            }

            try
            {
                var fileInfo = new FileInfo(fullPath);
                if (!fileInfo.Exists)
                {
                    continue;
                }

                var sizeBytes = fileInfo.Length;
                fileInfo.Delete();
                deletedFilesCount++;
                reclaimedBytes += sizeBytes;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        foreach (var directory in Directory
                     .EnumerateDirectories(storageRoot, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return new PrintDocumentGarbageCollectionResult(deletedFilesCount, reclaimedBytes);
    }

    private static bool IsInsideDirectory(string rootDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var rootWithSeparator = rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);

        return fullPath.StartsWith(
            rootWithSeparator,
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }
}
