using System.Text.Json.Nodes;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="ChartsCountryParser"/> against sanitized <c>FEmusic_charts</c>
/// fixtures captured with <c>formData.selectedValues=["ID"]</c> and <c>["JP"]</c> (the request
/// shape verified against the live API). They pin down the two contracts the Charts country
/// dropdown relies on: the country list comes from the response (never hardcoded), and the
/// response identifies which country the server applied.
/// </summary>
public class ChartsCountryParserTests
{
    private static JsonNode LoadCharts(string name) =>
        JsonNode.Parse(TestFixtures.LoadString(TestFixtures.Surfaces.Charts, name))!;

    [Fact]
    public void Parses_selected_country_indonesia_from_id_response()
    {
        var selector = ChartsCountryParser.Parse(LoadCharts("FEmusic_charts_ID"));

        Assert.Equal("Indonesia", selector.SelectedName);
        Assert.Equal("ID", selector.SelectedCode);
    }

    [Fact]
    public void Parses_selected_country_japan_from_jp_response()
    {
        var selector = ChartsCountryParser.Parse(LoadCharts("FEmusic_charts_JP"));

        Assert.Equal("Jepang", selector.SelectedName);
        Assert.Equal("JP", selector.SelectedCode);
    }

    [Fact]
    public void Parses_full_country_menu_from_response()
    {
        var selector = ChartsCountryParser.Parse(LoadCharts("FEmusic_charts_ID"));

        // The live menu carries ~70 countries; a healthy parse must see far more than the
        // handful a hardcoded list would tempt someone to ship.
        Assert.True(selector.Options.Count >= 50, $"expected >= 50 countries, got {selector.Options.Count}");

        Assert.Contains(selector.Options, o => o is { Code: "ZZ", Name: "Global" });
        Assert.Contains(selector.Options, o => o.Code == "JP");
        Assert.Contains(selector.Options, o => o.Code == "US");
    }

    [Fact]
    public void Country_codes_are_unique_two_letter_uppercase()
    {
        var selector = ChartsCountryParser.Parse(LoadCharts("FEmusic_charts_ID"));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in selector.Options)
        {
            Assert.Matches("^[A-Z]{2}$", option.Code);
            Assert.True(seen.Add(option.Code), $"duplicate country code {option.Code}");
            Assert.False(string.IsNullOrWhiteSpace(option.Name));
        }
    }

    [Fact]
    public void Decodes_country_code_from_form_item_entity_key()
    {
        // Real (anonymous) key from the live menu: URL-escaped base64 of a protobuf blob whose
        // readable tail is "explore_charts_country_menu_<formId>ID".
        var code = ChartsCountryParser.DecodeCountryCode(
            "EidleHBsb3JlX2NoYXJ0c19jb3VudHJ5X21lbnVfMzE2NzY2NTY3SUQgkQEoAQ%3D%3D");

        Assert.Equal("ID", code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 at all!!!")]
    public void Decode_returns_null_for_unreadable_keys(string? key)
    {
        Assert.Null(ChartsCountryParser.DecodeCountryCode(key));
    }

    [Fact]
    public void Missing_filter_button_yields_empty_selector_not_error()
    {
        Assert.Same(ChartCountrySelector.Empty, ChartsCountryParser.Parse(new JsonObject()));
        Assert.Same(ChartCountrySelector.Empty, ChartsCountryParser.Parse(null));
    }

    [Fact]
    public void Charts_sections_still_parse_through_home_response_parser()
    {
        // The same response feeds HomeResponseParser for the shelves; the filter shelf (which has
        // no contents) must be skipped, not break the page.
        var response = HomeResponseParser.Parse(LoadCharts("FEmusic_charts_ID"));

        Assert.Equal(2, response.Sections.Count);
        Assert.Equal("Tangga lagu video", response.Sections[0].Title);
        Assert.Equal("Artis teratas", response.Sections[1].Title);

        // Top-artist rows carry rank + movement from customIndexColumn.
        var artist = Assert.IsType<HomeSectionItem.ArtistItem>(response.Sections[1].Items[0]);
        Assert.Equal(1, artist.Artist.Rank);
    }

    [Fact]
    public void Country_formdata_changes_the_returned_charts()
    {
        // The proof the formData request parameter works end-to-end: same browseId, different
        // selectedValues, different country's "Trending 20" playlist in the response.
        var indonesia = HomeResponseParser.Parse(LoadCharts("FEmusic_charts_ID"));
        var japan = HomeResponseParser.Parse(LoadCharts("FEmusic_charts_JP"));

        var idFirst = Assert.IsType<HomeSectionItem.PlaylistItem>(indonesia.Sections[0].Items[0]);
        var jpFirst = Assert.IsType<HomeSectionItem.PlaylistItem>(japan.Sections[0].Items[0]);
        Assert.Equal("Trending 20 Indonesia", idFirst.Pl.Title);
        Assert.Equal("Trending 20 Jepang", jpFirst.Pl.Title);
    }
}
