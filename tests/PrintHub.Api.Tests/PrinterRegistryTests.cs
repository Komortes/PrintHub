// tests/PrintHub.Api.Tests/PrinterRegistryTests.cs
using System.Net;
using System.Net.Http.Json;
using PrintHub.Api.Tests.Infrastructure;
using PrintHub.Contracts.Printers;

namespace PrintHub.Api.Tests;

public sealed class PrinterRegistryTests
{
    private static HttpRequestMessage Authed(HttpMethod method, string url) =>
        new(method, url) { Headers = { { "X-PrintHub-Api-Key", "test-api-key" } } };

    [Fact]
    public async Task GetPrinters_ReturnsEmpty_WhenNoneRegistered()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Authed(HttpMethod.Get, "/printers"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var printers = await response.Content.ReadFromJsonAsync<PrinterDto[]>(TestJson.SerializerOptions);
        Assert.NotNull(printers);
        Assert.Empty(printers!);
    }

    [Fact]
    public async Task DiscoverPrinters_ReturnsMockPrinters_WhenNoneRegistered()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Authed(HttpMethod.Get, "/printers/discover"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var printers = await response.Content.ReadFromJsonAsync<PrinterDto[]>(TestJson.SerializerOptions);
        Assert.NotNull(printers);
        Assert.Equal(2, printers!.Length); // MockBackend returns 2 printers
    }

    [Fact]
    public async Task AddPrinter_RegistersPrinter_AndRemovesFromDiscover()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        // Add printer_1 to registry
        var addReq = Authed(HttpMethod.Post, "/printers");
        addReq.Content = JsonContent.Create(new AddPrinterRequest("printer_1"));
        var addResp = await client.SendAsync(addReq);

        Assert.Equal(HttpStatusCode.OK, addResp.StatusCode);
        var added = await addResp.Content.ReadFromJsonAsync<PrinterDto>(TestJson.SerializerOptions);
        Assert.NotNull(added);
        Assert.Equal("printer_1", added!.Id);

        // GET /printers returns it
        var getResp = await client.SendAsync(Authed(HttpMethod.Get, "/printers"));
        var registered = await getResp.Content.ReadFromJsonAsync<PrinterDto[]>(TestJson.SerializerOptions);
        Assert.Single(registered!);
        Assert.Equal("printer_1", registered![0].Id);

        // GET /printers/discover no longer returns it
        var discoverResp = await client.SendAsync(Authed(HttpMethod.Get, "/printers/discover"));
        var discovered = await discoverResp.Content.ReadFromJsonAsync<PrinterDto[]>(TestJson.SerializerOptions);
        Assert.Single(discovered!);
        Assert.Equal("printer_2", discovered![0].Id);
    }

    [Fact]
    public async Task AddPrinter_Returns404_WhenNotInOS()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        var addReq = Authed(HttpMethod.Post, "/printers");
        addReq.Content = JsonContent.Create(new AddPrinterRequest("non_existent_printer"));
        var addResp = await client.SendAsync(addReq);

        Assert.Equal(HttpStatusCode.NotFound, addResp.StatusCode);
    }

    [Fact]
    public async Task RemovePrinter_RemovesFromRegistry()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        // Add then remove
        var addReq = Authed(HttpMethod.Post, "/printers");
        addReq.Content = JsonContent.Create(new AddPrinterRequest("printer_1"));
        await client.SendAsync(addReq);

        var delResp = await client.SendAsync(Authed(HttpMethod.Delete, "/printers/printer_1"));
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        var getResp = await client.SendAsync(Authed(HttpMethod.Get, "/printers"));
        var registered = await getResp.Content.ReadFromJsonAsync<PrinterDto[]>(TestJson.SerializerOptions);
        Assert.Empty(registered!);
    }

    [Fact]
    public async Task RemovePrinter_Returns404_WhenNotRegistered()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        var delResp = await client.SendAsync(Authed(HttpMethod.Delete, "/printers/printer_1"));
        Assert.Equal(HttpStatusCode.NotFound, delResp.StatusCode);
    }

    [Fact]
    public async Task SetDefaultPrinter_Works_OnRegisteredPrinter()
    {
        using var factory = new PrintHubApiFactory();
        using var client = factory.CreateClient();

        // Register printer_1 first
        var addReq = Authed(HttpMethod.Post, "/printers");
        addReq.Content = JsonContent.Create(new AddPrinterRequest("printer_1"));
        await client.SendAsync(addReq);

        // Set as default
        var defResp = await client.SendAsync(Authed(HttpMethod.Put, "/printers/printer_1/default"));
        Assert.Equal(HttpStatusCode.OK, defResp.StatusCode);

        var printer = await defResp.Content.ReadFromJsonAsync<PrinterDto>(TestJson.SerializerOptions);
        Assert.NotNull(printer);
        Assert.True(printer!.IsDefault);
    }
}
