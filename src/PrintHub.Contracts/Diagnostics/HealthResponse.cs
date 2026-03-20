namespace PrintHub.Contracts.Diagnostics;

public sealed record HealthResponse(
    string Status,
    string Service,
    DateTimeOffset Timestamp);
