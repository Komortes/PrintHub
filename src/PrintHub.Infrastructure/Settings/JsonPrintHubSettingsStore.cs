using System.Text.Json;
using PrintHub.Core.Settings;

namespace PrintHub.Infrastructure.Settings;

public sealed class JsonPrintHubSettingsStore : IPrintHubSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _contentRootPath;
    private readonly string _settingsFilePath;

    public JsonPrintHubSettingsStore(string contentRootPath, string settingsFilePath)
    {
        _contentRootPath = contentRootPath;
        _settingsFilePath = ResolvePath(contentRootPath, settingsFilePath);
    }

    public async ValueTask<PrintHubSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_settingsFilePath);
        return await JsonSerializer.DeserializeAsync<PrintHubSettings>(stream, SerializerOptions, cancellationToken);
    }

    public async ValueTask SaveAsync(PrintHubSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_settingsFilePath)
            ?? throw new InvalidOperationException("Settings file path does not have a directory component.");

        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(ResolvePath(_contentRootPath, settings.StorageDirectory));

        await using var stream = File.Create(_settingsFilePath);
        await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
    }

    private static string ResolvePath(string contentRootPath, string path) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(contentRootPath, path));
}
