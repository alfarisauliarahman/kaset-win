using System.Text.Json.Nodes;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// Search / suggestions endpoints (task 7.3). Posts to the InnerTube <c>search</c> and
/// <c>music/get_search_suggestions</c> endpoints via the shared <see cref="RequestAsync"/> core
/// (task 7.1) and delegates shaping to <see cref="SearchResponseParser"/>.
/// </summary>
public sealed partial class YTMusicClient
{
    /// <inheritdoc />
    public async Task<SearchResponse> SearchAsync(string query, SearchFilter? filter = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);

        var body = new JsonObject { ["query"] = query };

        // A filter carries the InnerTube params token (already base64) that scopes the search to
        // a single content type (songs / albums / artists / playlists / podcasts).
        if (!string.IsNullOrEmpty(filter?.Params))
        {
            body["params"] = filter.Params;
        }

        var node = await RequestAsync("search", body, ApiCacheTtl.Search, ct).ConfigureAwait(false);

        // TEMP diag: the universal (unfiltered) search comes back nearly empty for this client while
        // the web gets full shelves — dump the raw response for offline shape analysis.
        if (filter is null)
        {
            // Query in the log line + inside the dump so a stale dump can't mislead the analysis.
            Diag.Write($"search base: query='{query}'");
            Diag.Dump("search-base.json", $"{{\"__query\":{JsonValue.Create(query).ToJsonString()},\"response\":{node?.ToJsonString() ?? "null"}}}");
        }
        else if (string.Equals(filter.Label, "Podcasts", StringComparison.Ordinal))
        {
            // The podcasts-filtered results have started looking unrelated to the query too —
            // record the raw response for the same offline verdict as the base search.
            Diag.Dump("search-podcasts.json", $"{{\"__query\":{JsonValue.Create(query).ToJsonString()},\"response\":{node?.ToJsonString() ?? "null"}}}");
        }

        return SearchResponseParser.Parse(node);
    }

    /// <inheritdoc />
    public async Task<SearchResponse> SearchContinuationAsync(string token, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        // Search continuations reuse the InnerTube continuation-in-body pattern (same as browse).
        var body = new JsonObject { ["continuation"] = token };
        var node = await RequestAsync("search", body, ApiCacheTtl.Search, ct).ConfigureAwait(false);
        return SearchResponseParser.ParseContinuation(node);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetSearchSuggestionsAsync(string input, CancellationToken ct = default)
    {
        // Suggestions are ephemeral autocomplete; empty input yields no suggestions and no request.
        if (string.IsNullOrEmpty(input))
        {
            return Array.Empty<string>();
        }

        var body = new JsonObject { ["input"] = input };

        // No caching for suggestions — they change keystroke-to-keystroke.
        var node = await RequestAsync("music/get_search_suggestions", body, ttl: null, ct).ConfigureAwait(false);
        return ParseSuggestions(node);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchSuggestion>> GetRichSearchSuggestionsAsync(string input, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(input))
        {
            return Array.Empty<SearchSuggestion>();
        }

        var body = new JsonObject { ["input"] = input };
        var node = await RequestAsync("music/get_search_suggestions", body, ttl: null, ct).ConfigureAwait(false);
        return SearchSuggestionsParser.Parse(node);
    }

    /// <summary>
    /// Extracts the suggestion query strings from a <c>music/get_search_suggestions</c> response.
    /// The suggestion text lives on <c>searchSuggestionRenderer.suggestion.runs[].text</c> (and,
    /// for prior searches, <c>historySuggestionRenderer.suggestion</c>); both are collected by
    /// joining the runs of every <c>suggestion</c> node found in the tree, in document order and
    /// de-duplicated.
    /// </summary>
    private static IReadOnlyList<string> ParseSuggestions(JsonNode? root)
    {
        if (root is null)
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var suggestions = new List<string>();

        // Each suggestion renderer exposes its display text under a "suggestion" property with
        // the standard { runs: [ { text } ] } shape.
        foreach (var suggestion in ResponseTreeSearch.FindAll(root, "suggestion"))
        {
            var runs = (suggestion as JsonObject)?["runs"] as JsonArray;
            if (runs is null)
            {
                continue;
            }

            var text = JoinRunTexts(runs);
            if (!string.IsNullOrEmpty(text) && seen.Add(text))
            {
                suggestions.Add(text);
            }
        }

        return suggestions;
    }

    /// <summary>Concatenates the <c>text</c> of each run in a <c>runs</c> array.</summary>
    private static string JoinRunTexts(JsonArray runs)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var run in runs)
        {
            if (run is JsonObject obj
                && obj.TryGetPropertyValue("text", out var value)
                && value is JsonValue jv
                && jv.TryGetValue<string>(out var text))
            {
                builder.Append(text);
            }
        }

        return builder.ToString();
    }
}
