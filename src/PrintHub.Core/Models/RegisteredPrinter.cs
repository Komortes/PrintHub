namespace PrintHub.Core.Models;

public sealed record RegisteredPrinter(
    string Id,
    string Name,
    DateTimeOffset AddedAt);
