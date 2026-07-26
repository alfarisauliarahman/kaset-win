using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>One selectable country in the Charts country menu (e.g. <c>("ID", "Indonesia")</c>).</summary>
/// <param name="Code">ISO 3166-1 alpha-2 region code sent back in <c>formData.selectedValues</c>;
/// <c>"ZZ"</c> is YouTube's code for the "Global" chart.</param>
/// <param name="Name">Display name, already localized by the server (<c>hl</c>).</param>
public sealed record ChartCountry(string Code, string Name);

/// <summary>
/// The Charts country menu as parsed from a <c>FEmusic_charts</c> response: every selectable
/// country plus which one the server applied to this response.
/// </summary>
public sealed record ChartCountrySelector
{
    /// <summary>Shared empty selector for responses that carry no country menu.</summary>
    public static readonly ChartCountrySelector Empty = new();

    /// <summary>All selectable countries, in server order (selected + Global first, then A–Z).</summary>
    public IReadOnlyList<ChartCountry> Options { get; init; } = Array.Empty<ChartCountry>();

    /// <summary>Region code the server applied to this response, or <c>null</c> when unknown.</summary>
    public string? SelectedCode { get; init; }

    /// <summary>Localized display name of the applied country (the filter button's own label).</summary>
    public string? SelectedName { get; init; }
}

/// <summary>
/// Parses the country dropdown of the <c>FEmusic_charts</c> surface
/// (<c>musicSortFilterButtonRenderer</c>). The list of countries is read from the response itself —
/// never hardcoded — so region additions/removals on YouTube's side flow through automatically.
/// </summary>
/// <remarks>
/// Follows the resilient-parser contract of this folder: never throws on malformed input, returning
/// <see cref="ChartCountrySelector.Empty"/> instead, and is pure/deterministic (Property 23/34).
/// </remarks>
public static class ChartsCountryParser
{
    // InnerTube exposes no plain "countryCode" field on the menu options; the region code only
    // exists inside the protobuf-encoded formItemEntityKey ("…explore_charts_country_menu_<formId><CC>…").
    // The \d+ keeps the match anchored to the menu's numeric form id so an unrelated pair of
    // capitals elsewhere in the blob can't be mistaken for a region code.
    private static readonly Regex CountryKeyRegex =
        new(@"country_menu_\d+([A-Z]{2})", RegexOptions.CultureInvariant);

    /// <summary>
    /// Extracts the country selector from a <c>FEmusic_charts</c> browse response. Responses
    /// without the filter button (or with an unreadable menu) yield
    /// <see cref="ChartCountrySelector.Empty"/> rather than an error — the charts content itself is
    /// still usable without the dropdown.
    /// </summary>
    public static ChartCountrySelector Parse(JsonNode? root)
    {
        var button = ResponseTreeSearch.FindFirst(root, "musicSortFilterButtonRenderer");
        if (button is null)
        {
            return ChartCountrySelector.Empty;
        }

        // The button's own label names the country the server actually applied (e.g. "Indonesia"),
        // which is the only signal of the effective region when the request sent no formData.
        var selectedName = ParsingHelpers.ExtractText(button, "title");

        var options = new List<ChartCountry>();
        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        var menu = ResponseTreeSearch.FindFirst(button, "musicMultiSelectMenuRenderer");
        if (menu is JsonObject menuObj
            && menuObj.TryGetPropertyValue("options", out var optionsNode)
            && optionsNode is JsonArray optionArray)
        {
            foreach (var option in optionArray)
            {
                // Divider rows (musicMenuItemDividerRenderer) simply don't carry the item renderer.
                if (option is not JsonObject optionObj
                    || !optionObj.TryGetPropertyValue("musicMultiSelectMenuItemRenderer", out var itemNode)
                    || itemNode is not JsonObject item)
                {
                    continue;
                }

                var name = ParsingHelpers.ExtractText(item, "title");
                var code = DecodeCountryCode(
                    item.TryGetPropertyValue("formItemEntityKey", out var keyNode)
                        && keyNode is JsonValue keyValue && keyValue.TryGetValue<string>(out var key)
                        ? key
                        : null);

                // The selected country appears once in the menu; duplicates would only confuse the
                // dropdown's SelectedItem matching, so keep the first occurrence per code.
                if (name is null || code is null || !seenCodes.Add(code))
                {
                    continue;
                }

                options.Add(new ChartCountry(code, name));
            }
        }

        string? selectedCode = null;
        if (selectedName is not null)
        {
            foreach (var option in options)
            {
                if (string.Equals(option.Name, selectedName, StringComparison.Ordinal))
                {
                    selectedCode = option.Code;
                    break;
                }
            }
        }

        return new ChartCountrySelector
        {
            Options = options,
            SelectedCode = selectedCode,
            SelectedName = selectedName,
        };
    }

    /// <summary>
    /// Decodes the region code out of a menu option's <c>formItemEntityKey</c> (URL-escaped
    /// base64 of a protobuf blob). Returns <c>null</c> for anything unreadable — a single mangled
    /// option must not take the whole menu down.
    /// </summary>
    public static string? DecodeCountryCode(string? formItemEntityKey)
    {
        if (string.IsNullOrEmpty(formItemEntityKey))
        {
            return null;
        }

        try
        {
            // Keys arrive with their base64 padding URL-escaped ("%3D"); some InnerTube keys use
            // the URL-safe alphabet, so normalize both before decoding.
            var unescaped = Uri.UnescapeDataString(formItemEntityKey)
                .Replace('-', '+')
                .Replace('_', '/');
            var padded = unescaped.PadRight(unescaped.Length + ((4 - (unescaped.Length % 4)) % 4), '=');
            var bytes = Convert.FromBase64String(padded);

            // Latin-1, not UTF-8: the blob is protobuf, and stray high bytes must map 1:1 to chars
            // instead of turning into replacement characters that could shift the ASCII we match on.
            var text = Encoding.Latin1.GetString(bytes);
            var match = CountryKeyRegex.Match(text);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
