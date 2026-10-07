using System.Text.Json.Serialization;

namespace Lumen.Eval;

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static JsonSerializerOptions Indented { get; } = new(Options) { WriteIndented = true };
}
