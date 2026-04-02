namespace PrintHub.Api.Diagnostics;

public interface IPrintHubSupportBundleService
{
    ValueTask<PrintHubSupportBundle> CreateAsync(CancellationToken cancellationToken = default);
}

public sealed record PrintHubSupportBundle(
    byte[] Content,
    string FileName,
    string ContentType);
