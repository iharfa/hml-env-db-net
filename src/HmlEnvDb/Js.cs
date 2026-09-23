using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HmlEnvDb;

// JsonNode access helpers — the API payloads stay untyped, mirroring how the
// original JS walked them, so the port stays a line-for-line translation.
public static class Js
{
    public static readonly JsonSerializerOptions Opts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Ser(object o) => JsonSerializer.Serialize(o, Opts);

    public static double? Num(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return double.IsNaN(d) ? null : d;
        if (v.TryGetValue<string>(out var s) && double.TryParse(s, out var p)) return p;
        return null;
    }

    public static int? Int(JsonNode? n) => Num(n) is double d ? (int)d : null;
    public static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    public static bool Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    public static JsonNode? At(JsonNode? arr, int i)
    {
        if (arr is not JsonArray a || i < 0 || i >= a.Count) return null;
        return a[i];
    }

    public static double? NumAt(JsonNode? arr, int i) => Num(At(arr, i));
    public static string? StrAt(JsonNode? arr, int i) => Str(At(arr, i));

    public static List<string> Strs(JsonNode? n)
    {
        var list = new List<string>();
        if (n is JsonArray a) foreach (var x in a) if (Str(x) is string s) list.Add(s);
        return list;
    }

    public static List<double?> Nums(JsonNode? n)
    {
        var list = new List<double?>();
        if (n is JsonArray a) foreach (var x in a) list.Add(Num(x));
        return list;
    }

    public static int Count(JsonNode? n) => n is JsonArray a ? a.Count : 0;
}
