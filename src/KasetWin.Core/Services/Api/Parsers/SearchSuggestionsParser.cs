using System.Text.Json.Nodes;
using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// Pure parser for <c>music/get_search_suggestions</c> responses (ported from the macOS
/// <c>SearchSuggestionsParser</c>, extended with the rich entity rows the endpoint also returns):
/// plain query completions (<c>searchSuggestionRenderer</c>), history entries
/// (<c>historySuggestionRenderer</c>), and rich song/artist/album rows
/// (<c>musicResponsiveListItemRenderer</c>). Malformed input yields an empty list, never a throw.
/// </summary>
public static class SearchSuggestionsParser
{
    /// <summary>Parses the suggestions response into an ordered, de-duplicated list.</summary>
    public static IReadOnlyList<SearchSuggestion> Parse(JsonNode? root)
    {
        var suggestions = new List<SearchSuggestion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Prop(root, "contents") is not JsonArray contents)
        {
            return suggestions;
        }

        foreach (var content in contents)
        {
            if (Prop(Prop(content, "searchSuggestionsSectionRenderer"), "contents") is not JsonArray items)
            {
                continue;
            }

            foreach (var item in items)
            {
                var suggestion = ParseItem(item);
                if (suggestion is not null && seen.Add(suggestion.Query + "|" + suggestion.Subtitle))
                {
                    suggestions.Add(suggestion);
                }
            }
        }

        return suggestions;
    }

    private static SearchSuggestion? ParseItem(JsonNode? item)
    {
        if (Prop(item, "searchSuggestionRenderer") is { } plain)
        {
            return FromSuggestionRenderer(plain, isHistory: false);
        }

        if (Prop(item, "historySuggestionRenderer") is { } history)
        {
            return FromSuggestionRenderer(history, isHistory: true);
        }

        if (Prop(item, "musicResponsiveListItemRenderer") is { } rich)
        {
            return FromRichRenderer(rich);
        }

        return null;
    }

    private static SearchSuggestion? FromSuggestionRenderer(JsonNode renderer, bool isHistory)
    {
        var query = JoinRuns(Prop(Prop(renderer, "suggestion"), "runs"));
        return string.IsNullOrEmpty(query) ? null : new SearchSuggestion(query, IsHistory: isHistory);
    }

    /// <summary>Rich entity row: title from the first flex column, subtitle joined from the second.</summary>
    private static SearchSuggestion? FromRichRenderer(JsonNode renderer)
    {
        if (Prop(renderer, "flexColumns") is not JsonArray columns || columns.Count == 0)
        {
            return null;
        }

        var title = JoinRuns(ColumnRuns(columns, 0));
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var subtitle = columns.Count > 1 ? JoinRuns(ColumnRuns(columns, 1)) : null;

        // Navigation target so choosing the row can go straight to the artist/album (or play the song).
        var navigation = Prop(renderer, "navigationEndpoint");
        var browse = Prop(navigation, "browseEndpoint");

        return new SearchSuggestion(title, subtitle, ParsingHelpers.BestThumbnailUrl(renderer))
        {
            BrowseId = Str(browse, "browseId"),
            PageType = Str(
                Prop(Prop(browse, "browseEndpointContextSupportedConfigs"), "browseEndpointContextMusicConfig"),
                "pageType"),
            VideoId = Str(Prop(navigation, "watchEndpoint"), "videoId"),
        };
    }

    private static string? Str(JsonNode? node, string key) =>
        Prop(node, key) is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static JsonArray? ColumnRuns(JsonArray columns, int index) =>
        Prop(Prop(Prop(columns[index], "musicResponsiveListItemFlexColumnRenderer"), "text"), "runs") as JsonArray;

    private static string JoinRuns(JsonNode? runs)
    {
        if (runs is not JsonArray array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var run in array)
        {
            if (Prop(run, "text") is JsonValue value && value.TryGetValue<string>(out var text))
            {
                parts.Add(text);
            }
        }

        return string.Concat(parts).Trim();
    }

    private static JsonNode? Prop(JsonNode? node, string key) =>
        node is JsonObject obj && obj.TryGetPropertyValue(key, out var value) ? value : null;
}
