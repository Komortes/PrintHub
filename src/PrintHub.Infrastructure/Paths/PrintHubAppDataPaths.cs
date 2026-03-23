namespace PrintHub.Infrastructure.Paths;

public sealed class PrintHubAppDataPaths
{
    public const string HomeEnvironmentVariableName = "PRINTHUB_HOME";

    private const string AppFolderName = "PrintHub";

    public PrintHubAppDataPaths(string appDataRootPath)
    {
        if (string.IsNullOrWhiteSpace(appDataRootPath))
        {
            throw new ArgumentException("App data root path is required.", nameof(appDataRootPath));
        }

        AppDataRootPath = Path.GetFullPath(appDataRootPath.Trim());
        Directory.CreateDirectory(AppDataRootPath);
    }

    public string AppDataRootPath { get; }

    public static PrintHubAppDataPaths CreateDefault() =>
        new(ResolveAppDataRootPath());

    public static string ResolveAppDataRootPath()
    {
        var overridePath = Environment.GetEnvironmentVariable(HomeEnvironmentVariableName);

        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath.Trim());
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName);
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                AppFolderName);
        }

        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            return Path.Combine(xdgDataHome.Trim(), AppFolderName);
        }

        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDirectory, ".local", "share", AppFolderName);
    }

    public string ResolveDataPath(string? relativeOrAbsolutePath, string defaultRelativePath)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(relativeOrAbsolutePath)
            ? defaultRelativePath
            : relativeOrAbsolutePath.Trim();

        return Path.IsPathRooted(normalizedPath)
            ? normalizedPath
            : Path.GetFullPath(Path.Combine(AppDataRootPath, normalizedPath));
    }
}
