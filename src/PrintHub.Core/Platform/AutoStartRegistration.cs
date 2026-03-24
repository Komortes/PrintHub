namespace PrintHub.Core.Platform;

public sealed record AutoStartRegistration(
    bool IsSupported,
    bool IsEnabled,
    string Provider,
    string? EntryPath);
