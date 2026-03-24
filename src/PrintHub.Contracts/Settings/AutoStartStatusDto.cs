namespace PrintHub.Contracts.Settings;

public sealed record AutoStartStatusDto(
    bool IsSupported,
    bool IsEnabled,
    string Provider,
    string? EntryPath);
