using System.Text.Json;
using System.Text.Json.Serialization;

namespace Megruli.Shared;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
