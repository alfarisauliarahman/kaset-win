using System.Text.Json.Nodes;

namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// Pure, dependency-free recursive search helpers for InnerTube (YouTube Music)
/// response trees. YouTube frequently reshuffles the container renderers that wrap a
/// payload (tabs, section lists, shelves, grids, …) without changing the leaf renderer
/// you actually care about. Searching by renderer key recursively makes the per-surface
/// parsers resilient to that reshuffling (macOS <c>ResponseTreeSearch</c> counterpart).
/// </summary>
/// <remarks>
/// All methods are <c>static</c> and deterministic: <see cref="JsonObject"/> preserves
/// document property order and <see cref="JsonArray"/> preserves element order, so a given
/// input always yields the same traversal and the same results. Nothing is mutated or
/// re-parented — returned nodes are references into the input tree.
/// </remarks>
public static class ResponseTreeSearch
{
    /// <summary>
    /// Returns the value of the first property named <paramref name="rendererKey"/> found
    /// anywhere in the tree (depth-first, document order), or <c>null</c> if absent.
    /// </summary>
    /// <param name="root">The response subtree to search. <c>null</c> yields <c>null</c>.</param>
    /// <param name="rendererKey">The renderer/property key to look for (e.g. <c>"musicShelfRenderer"</c>).</param>
    public static JsonNode? FindFirst(JsonNode? root, string rendererKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(rendererKey);
        return FindFirstCore(root, rendererKey);
    }

    /// <summary>
    /// Returns the values of every property named <paramref name="rendererKey"/> found in the
    /// tree, in depth-first document order. Nested occurrences under a match are included.
    /// </summary>
    /// <param name="root">The response subtree to search. <c>null</c> yields an empty list.</param>
    /// <param name="rendererKey">The renderer/property key to collect (e.g. <c>"musicResponsiveListItemRenderer"</c>).</param>
    public static IReadOnlyList<JsonNode> FindAll(JsonNode? root, string rendererKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(rendererKey);
        var results = new List<JsonNode>();
        FindAllCore(root, rendererKey, results);
        return results;
    }

    /// <summary>
    /// Whether any property named <paramref name="key"/> exists anywhere in the tree.
    /// </summary>
    public static bool ContainsKey(JsonNode? root, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        switch (root)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj)
                {
                    if (name == key || ContainsKey(value, key))
                    {
                        return true;
                    }
                }

                return false;

            case JsonArray array:
                foreach (var item in array)
                {
                    if (ContainsKey(item, key))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Whether any string value anywhere in the tree contains <paramref name="text"/>
    /// (case-insensitive).
    /// </summary>
    public static bool ContainsText(JsonNode? root, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        switch (root)
        {
            case JsonValue value when value.TryGetValue<string>(out var s):
                return s.Contains(text, StringComparison.OrdinalIgnoreCase);

            case JsonObject obj:
                foreach (var (_, child) in obj)
                {
                    if (ContainsText(child, text))
                    {
                        return true;
                    }
                }

                return false;

            case JsonArray array:
                foreach (var item in array)
                {
                    if (ContainsText(item, text))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    private static JsonNode? FindFirstCore(JsonNode? node, string key)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue(key, out var match) && match is not null)
                {
                    return match;
                }

                foreach (var (_, child) in obj)
                {
                    var found = FindFirstCore(child, key);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;

            case JsonArray array:
                foreach (var item in array)
                {
                    var found = FindFirstCore(item, key);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static void FindAllCore(JsonNode? node, string key, List<JsonNode> results)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    if (name == key && child is not null)
                    {
                        results.Add(child);
                    }

                    FindAllCore(child, key, results);
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    FindAllCore(item, key, results);
                }

                break;
        }
    }
}
