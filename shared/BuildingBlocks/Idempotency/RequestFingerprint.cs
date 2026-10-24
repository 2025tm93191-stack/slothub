using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BuildingBlocks.Idempotency;

/// <summary>
/// SHA-256 fingerprint of an unsafe request: method, path and a canonical form of the JSON body
/// (properties sorted, whitespace removed), so formatting differences do not count as a different payload.
/// </summary>
public static class RequestFingerprint
{
    public static string Compute(string method, string path, string body)
    {
        var canonical = $"{method.ToUpperInvariant()}\n{path.ToLowerInvariant()}\n{Canonicalize(body)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal static string Canonicalize(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            return Sort(JsonNode.Parse(body))?.ToJsonString() ?? "null";
        }
        catch (JsonException)
        {
            return body.Trim();
        }
    }

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
        JsonArray array => new JsonArray(array.Select(Sort).ToArray()),
        _ => node?.DeepClone(),
    };
}
