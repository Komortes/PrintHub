using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrintHub.Api.Tests.Infrastructure;

internal static class TestJson
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };
}
