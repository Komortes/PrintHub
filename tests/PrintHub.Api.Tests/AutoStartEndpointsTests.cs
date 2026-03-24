using System.Net;
using System.Net.Http.Json;
using PrintHub.Api.Tests.Infrastructure;
using PrintHub.Contracts.Settings;

namespace PrintHub.Api.Tests;

public sealed class AutoStartEndpointsTests
{
    [Fact]
    public async Task AutoStart_CanBeEnabledAndDisabled()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        using var getRequest = CreateAuthorizedRequest(HttpMethod.Get, "/settings/auto-start");
        var initialResponse = await client.SendAsync(getRequest);

        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);

        var initialStatus = await initialResponse.Content.ReadFromJsonAsync<AutoStartStatusDto>();

        Assert.NotNull(initialStatus);
        Assert.True(initialStatus!.IsSupported);
        Assert.False(initialStatus.IsEnabled);
        Assert.Equal(GetExpectedProvider(), initialStatus.Provider);

        using var enableRequest = CreateAuthorizedRequest(HttpMethod.Put, "/settings/auto-start");
        enableRequest.Content = JsonContent.Create(new UpdateAutoStartRequest(true));

        var enableResponse = await client.SendAsync(enableRequest);

        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        var enabledStatus = await enableResponse.Content.ReadFromJsonAsync<AutoStartStatusDto>();

        Assert.NotNull(enabledStatus);
        Assert.True(enabledStatus!.IsEnabled);
        Assert.False(string.IsNullOrWhiteSpace(enabledStatus.EntryPath));
        Assert.True(File.Exists(enabledStatus.EntryPath));

        using var disableRequest = CreateAuthorizedRequest(HttpMethod.Put, "/settings/auto-start");
        disableRequest.Content = JsonContent.Create(new UpdateAutoStartRequest(false));

        var disableResponse = await client.SendAsync(disableRequest);

        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        var disabledStatus = await disableResponse.Content.ReadFromJsonAsync<AutoStartStatusDto>();

        Assert.NotNull(disabledStatus);
        Assert.False(disabledStatus!.IsEnabled);
        Assert.False(string.IsNullOrWhiteSpace(disabledStatus.EntryPath));
        Assert.False(File.Exists(disabledStatus.EntryPath));
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-PrintHub-Api-Key", "test-api-key");
        return request;
    }

    private static string GetExpectedProvider()
    {
        if (OperatingSystem.IsMacOS())
        {
            return "launch-agent";
        }

        if (OperatingSystem.IsWindows())
        {
            return "startup-folder";
        }

        return "desktop-autostart";
    }
}
