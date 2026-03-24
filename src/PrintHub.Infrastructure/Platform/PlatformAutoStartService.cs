using System.Text;
using PrintHub.Core.Platform;

namespace PrintHub.Infrastructure.Platform;

public sealed class PlatformAutoStartService : IAutoStartService
{
    private const string MacOsProvider = "launch-agent";
    private const string LinuxProvider = "desktop-autostart";
    private const string WindowsProvider = "startup-folder";

    private readonly string _appBaseDirectory;
    private readonly string _unixLauncherPath;
    private readonly string _windowsLauncherPath;
    private readonly string? _macOsLaunchAgentsDirectoryPath;
    private readonly string? _linuxAutostartDirectoryPath;
    private readonly string? _windowsStartupDirectoryPath;

    public PlatformAutoStartService(
        string appBaseDirectory,
        string? unixLauncherPath = null,
        string? windowsLauncherPath = null,
        string? macOsLaunchAgentsDirectoryPath = null,
        string? linuxAutostartDirectoryPath = null,
        string? windowsStartupDirectoryPath = null)
    {
        if (string.IsNullOrWhiteSpace(appBaseDirectory))
        {
            throw new ArgumentException("Application base directory is required.", nameof(appBaseDirectory));
        }

        _appBaseDirectory = Path.GetFullPath(appBaseDirectory.Trim());
        _unixLauncherPath = ResolveLauncherPath(unixLauncherPath, "run-printhub.sh");
        _windowsLauncherPath = ResolveLauncherPath(windowsLauncherPath, "run-printhub.ps1");
        _macOsLaunchAgentsDirectoryPath = NormalizeOptionalPath(macOsLaunchAgentsDirectoryPath);
        _linuxAutostartDirectoryPath = NormalizeOptionalPath(linuxAutostartDirectoryPath);
        _windowsStartupDirectoryPath = NormalizeOptionalPath(windowsStartupDirectoryPath);
    }

    public ValueTask<AutoStartRegistration> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = ResolveTarget();

        return ValueTask.FromResult(new AutoStartRegistration(
            target.IsSupported,
            target.IsSupported && File.Exists(target.EntryPath),
            target.Provider,
            target.IsSupported ? target.EntryPath : null));
    }

    public ValueTask<AutoStartRegistration> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = ResolveTarget();

        if (!target.IsSupported)
        {
            throw new InvalidOperationException("Auto-start is not available in the current runtime layout.");
        }

        if (enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target.EntryPath)!);
            File.WriteAllText(target.EntryPath, target.FileContents, Encoding.UTF8);
        }
        else if (File.Exists(target.EntryPath))
        {
            File.Delete(target.EntryPath);
        }

        return ValueTask.FromResult(new AutoStartRegistration(
            true,
            enabled && File.Exists(target.EntryPath),
            target.Provider,
            target.EntryPath));
    }

    private PlatformAutoStartTarget ResolveTarget()
    {
        if (OperatingSystem.IsMacOS())
        {
            return CreateMacOsTarget();
        }

        if (OperatingSystem.IsWindows())
        {
            return CreateWindowsTarget();
        }

        if (OperatingSystem.IsLinux())
        {
            return CreateLinuxTarget();
        }

        return PlatformAutoStartTarget.Unsupported("unsupported-platform");
    }

    private PlatformAutoStartTarget CreateMacOsTarget()
    {
        if (!File.Exists(_unixLauncherPath))
        {
            return PlatformAutoStartTarget.Unsupported(MacOsProvider);
        }

        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var directoryPath = _macOsLaunchAgentsDirectoryPath
            ?? Path.Combine(homeDirectory, "Library", "LaunchAgents");
        var entryPath = Path.Combine(directoryPath, "local.printhub.agent.plist");
        var fileContents = $$"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>local.printhub.agent</string>
  <key>ProgramArguments</key>
  <array>
    <string>{{EscapeXml(_unixLauncherPath)}}</string>
  </array>
  <key>EnvironmentVariables</key>
  <dict>
    <key>PRINTHUB_OPEN_BROWSER</key>
    <string>false</string>
  </dict>
  <key>RunAtLoad</key>
  <true/>
  <key>KeepAlive</key>
  <false/>
  <key>WorkingDirectory</key>
  <string>{{EscapeXml(Path.GetDirectoryName(_unixLauncherPath)! )}}</string>
</dict>
</plist>
""";

        return new PlatformAutoStartTarget(true, MacOsProvider, entryPath, fileContents);
    }

    private PlatformAutoStartTarget CreateLinuxTarget()
    {
        if (!File.Exists(_unixLauncherPath))
        {
            return PlatformAutoStartTarget.Unsupported(LinuxProvider);
        }

        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var directoryPath = _linuxAutostartDirectoryPath
            ?? (!string.IsNullOrWhiteSpace(xdgConfigHome)
                ? Path.Combine(xdgConfigHome.Trim(), "autostart")
                : Path.Combine(homeDirectory, ".config", "autostart"));
        var entryPath = Path.Combine(directoryPath, "printhub.desktop");
        var fileContents = $$"""
[Desktop Entry]
Type=Application
Version=1.0
Name=PrintHub
Comment=Start PrintHub on login
Exec=/usr/bin/env PRINTHUB_OPEN_BROWSER=false "{{_unixLauncherPath}}"
Path={{Path.GetDirectoryName(_unixLauncherPath)}}
Terminal=false
X-GNOME-Autostart-enabled=true
""";

        return new PlatformAutoStartTarget(true, LinuxProvider, entryPath, fileContents);
    }

    private PlatformAutoStartTarget CreateWindowsTarget()
    {
        if (!File.Exists(_windowsLauncherPath))
        {
            return PlatformAutoStartTarget.Unsupported(WindowsProvider);
        }

        var directoryPath = _windowsStartupDirectoryPath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var entryPath = Path.Combine(directoryPath, "PrintHub Startup.cmd");
        var windowsLauncherPath = _windowsLauncherPath.Replace("/", "\\");
        var fileContents = $$"""
@echo off
set PRINTHUB_OPEN_BROWSER=false
powershell -ExecutionPolicy Bypass -WindowStyle Hidden -File "{{windowsLauncherPath}}"
""";

        return new PlatformAutoStartTarget(true, WindowsProvider, entryPath, fileContents);
    }

    private string ResolveLauncherPath(string? overridePath, string defaultFileName)
    {
        var value = string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(_appBaseDirectory, defaultFileName)
            : overridePath.Trim();

        return Path.GetFullPath(value);
    }

    private static string? NormalizeOptionalPath(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(path.Trim());

    private static string EscapeXml(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private sealed record PlatformAutoStartTarget(
        bool IsSupported,
        string Provider,
        string EntryPath,
        string FileContents)
    {
        public static PlatformAutoStartTarget Unsupported(string provider) =>
            new(false, provider, string.Empty, string.Empty);
    }
}
