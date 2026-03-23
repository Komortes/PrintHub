namespace PrintHub.Contracts.Settings;

public sealed record CompleteOnboardingRequest(
    string ApiKey,
    string? DefaultPrinterId);
