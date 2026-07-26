using System.Text.Json;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Sanity tests verifying the fixture pipeline: fixtures are copied to the test
/// output directory and the <see cref="TestFixtures"/> loader can read them.
/// These guard the fixture infrastructure used by the parser property tests.
/// </summary>
public class FixtureLoaderSanityTests
{
    [Fact]
    public void Loads_home_fixture_as_nonempty_string()
    {
        var json = TestFixtures.LoadString(TestFixtures.Surfaces.Home, "FEmusic_home");

        Assert.False(string.IsNullOrWhiteSpace(json));
        Assert.Contains("singleColumnBrowseResultsRenderer", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Loads_home_fixture_as_parseable_json()
    {
        using var doc = TestFixtures.LoadJson(TestFixtures.Surfaces.Home, "FEmusic_home");

        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.TryGetProperty("contents", out _));
    }

    [Theory]
    [InlineData(TestFixtures.Surfaces.Home, "FEmusic_home")]
    [InlineData(TestFixtures.Surfaces.Charts, "FEmusic_charts_ID")]
    [InlineData(TestFixtures.Surfaces.Charts, "FEmusic_charts_JP")]
    [InlineData(TestFixtures.Surfaces.Search, "search")]
    [InlineData(TestFixtures.Surfaces.Library, "FEmusic_library_landing")]
    [InlineData(TestFixtures.Surfaces.Playlist, "playlist")]
    [InlineData(TestFixtures.Surfaces.Artist, "artist")]
    [InlineData(TestFixtures.Surfaces.RadioQueue, "next_radio_queue")]
    [InlineData(TestFixtures.Surfaces.SongMetadata, "next_song_metadata")]
    [InlineData(TestFixtures.Surfaces.Lyrics, "lyrics")]
    public void Each_surface_fixture_is_present_and_valid_json(string surface, string name)
    {
        var json = TestFixtures.LoadString(surface, name);
        Assert.False(string.IsNullOrWhiteSpace(json));

        // Must be valid JSON.
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }
}
