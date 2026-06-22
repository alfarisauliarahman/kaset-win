using KasetWin.Core.Services.Api.Parsers;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="BrowseIdClassifier"/> (task 5.3). Verify the prefix table from
/// api-discovery.md / design.md maps to the correct <see cref="BrowseIdKind"/>, including the
/// precedence rules (more specific prefixes win) reused by navigation routing (Property 25).
/// </summary>
public class BrowseIdClassifierTests
{
    [Theory]
    [InlineData("VLPL0000000playlist1", BrowseIdKind.Playlist)]
    [InlineData("VLLM", BrowseIdKind.Playlist)]
    [InlineData("PL0000000000000000", BrowseIdKind.Playlist)]
    [InlineData("RDCLAK5uy_000000000", BrowseIdKind.Playlist)]
    [InlineData("MPREb_0000000album1", BrowseIdKind.Album)]
    [InlineData("OLAK5uy_000000000", BrowseIdKind.Album)]
    [InlineData("UCxxxxxxxxxxxxxxxxxxxxxx", BrowseIdKind.Artist)]
    [InlineData("MPLAUCxxxxxxxxxxxxxxxx", BrowseIdKind.Artist)]
    [InlineData("MPSPP000000000000", BrowseIdKind.Podcast)]
    public void Classifies_known_prefixes(string browseId, BrowseIdKind expected)
    {
        Assert.Equal(expected, BrowseIdClassifier.Classify(browseId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("FEmusic_home")]
    [InlineData("XYZ_unknown")]
    public void Unknown_or_empty_yields_unknown(string? browseId)
    {
        Assert.Equal(BrowseIdKind.Unknown, BrowseIdClassifier.Classify(browseId));
    }

    [Fact]
    public void Podcast_prefix_wins_over_album_family()
    {
        // MPSPP shares the MP* family with album-ish prefixes; the more specific
        // podcast prefix must win.
        Assert.Equal(BrowseIdKind.Podcast, BrowseIdClassifier.Classify("MPSPP_show_123"));
    }

    [Fact]
    public void Library_artist_prefix_classifies_as_artist()
    {
        Assert.Equal(BrowseIdKind.Artist, BrowseIdClassifier.Classify("MPLAUC_artist_1"));
    }

    [Fact]
    public void Classify_is_deterministic()
    {
        Assert.Equal(
            BrowseIdClassifier.Classify("MPREb_0000000album1"),
            BrowseIdClassifier.Classify("MPREb_0000000album1"));
    }
}
