using KasetWin.Core.Models;
using KasetWin.Core.Services.Sharing;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="ShareUrlBuilder"/> (task 27.1, Req 34.1/34.2). Verify the
/// <c>music.youtube.com</c> URL shapes per content kind, the navigable-id guards that disable
/// sharing (Req 34.2), and the formatted share text — mirroring the macOS <c>ShareService</c>.
/// </summary>
public class ShareUrlBuilderTests
{
    [Theory]
    [InlineData(ShareContentKind.Song, "dQw4w9WgXcQ", "https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData(ShareContentKind.Playlist, "PL12345", "https://music.youtube.com/playlist?list=PL12345")]
    [InlineData(ShareContentKind.Album, "MPREb_abc123", "https://music.youtube.com/browse/MPREb_abc123")]
    [InlineData(ShareContentKind.Album, "OLAK5uy_xyz", "https://music.youtube.com/browse/OLAK5uy_xyz")]
    [InlineData(ShareContentKind.Artist, "UC1234567890", "https://music.youtube.com/channel/UC1234567890")]
    [InlineData(ShareContentKind.Podcast, "MPSPPL_show1", "https://music.youtube.com/browse/MPSPPL_show1")]
    public void BuildUrl_produces_canonical_url(ShareContentKind kind, string id, string expected)
    {
        var url = ShareUrlBuilder.BuildUrl(kind, id);
        Assert.NotNull(url);
        Assert.Equal(expected, url!.ToString());
    }

    [Theory]
    [InlineData(ShareContentKind.Song, null)]
    [InlineData(ShareContentKind.Song, "")]
    [InlineData(ShareContentKind.Album, "PL_not_an_album")] // playlist prefix → not navigable as album
    [InlineData(ShareContentKind.Album, "")]
    [InlineData(ShareContentKind.Artist, "MPLAUC_library_artist")] // library-artist id, no public channel
    [InlineData(ShareContentKind.Artist, "VL_playlist")]
    [InlineData(ShareContentKind.Podcast, "OLAK_not_podcast")]
    public void BuildUrl_returns_null_when_not_shareable(ShareContentKind kind, string? id)
    {
        Assert.Null(ShareUrlBuilder.BuildUrl(kind, id));
    }

    [Fact]
    public void BuildUrl_percent_encodes_id()
    {
        var url = ShareUrlBuilder.BuildUrl(ShareContentKind.Song, "a b&c");
        Assert.NotNull(url);
        // AbsoluteUri preserves the percent-encoding (what SetWebLink serializes); ToString()
        // returns a partially-decoded display form, so assert on the wire form.
        Assert.Equal("https://music.youtube.com/watch?v=a%20b%26c", url!.AbsoluteUri);
    }

    [Fact]
    public void TryCreate_song_carries_title_and_artists()
    {
        var song = new Song
        {
            Id = "vid1",
            VideoId = "vid1",
            Title = "My Song",
            Artists = [new Artist { Id = "UCx", Name = "The Artist" }],
        };

        var target = ShareUrlBuilder.TryCreate(song);

        Assert.NotNull(target);
        Assert.Equal("My Song", target!.Title);
        Assert.Equal("The Artist", target.Subtitle);
        Assert.Equal("https://music.youtube.com/watch?v=vid1", target.Url.ToString());
        Assert.Equal("My Song by The Artist", target.ShareText);
    }

    [Fact]
    public void TryCreate_artist_has_no_subtitle_and_uses_channel_url()
    {
        var artist = new Artist { Id = "UC987", Name = "Solo" };

        var target = ShareUrlBuilder.TryCreate(artist);

        Assert.NotNull(target);
        Assert.Null(target!.Subtitle);
        Assert.Equal("Solo", target.ShareText);
        Assert.Equal("https://music.youtube.com/channel/UC987", target.Url.ToString());
    }

    [Fact]
    public void TryCreate_album_without_navigable_id_is_not_shareable()
    {
        var album = new Album { Id = "not_navigable", Title = "Untitled" };
        Assert.Null(ShareUrlBuilder.TryCreate(album));
    }

    [Fact]
    public void TryCreate_null_inputs_return_null()
    {
        Assert.Null(ShareUrlBuilder.TryCreate((Song?)null));
        Assert.Null(ShareUrlBuilder.TryCreate((Playlist?)null));
        Assert.Null(ShareUrlBuilder.TryCreate((Album?)null));
        Assert.Null(ShareUrlBuilder.TryCreate((Artist?)null));
        Assert.Null(ShareUrlBuilder.TryCreate((PodcastShow?)null));
        Assert.Null(ShareUrlBuilder.TryCreate((HomeSectionItem?)null));
        Assert.Null(ShareUrlBuilder.TryCreate((FavoriteItem?)null));
    }

    [Fact]
    public void TryCreate_home_union_item_dispatches_by_kind()
    {
        var item = new HomeSectionItem.PlaylistItem(new Playlist
        {
            Id = "PLabc",
            Title = "Mix",
            Author = new Artist { Id = "UCa", Name = "DJ" },
        });

        var target = ShareUrlBuilder.TryCreate(item);

        Assert.NotNull(target);
        Assert.Equal("Mix", target!.Title);
        Assert.Equal("DJ", target.Subtitle);
        Assert.Equal("https://music.youtube.com/playlist?list=PLabc", target.Url.ToString());
    }

    [Fact]
    public void TryCreate_favorite_maps_type_to_kind()
    {
        var favorite = new FavoriteItem("UCchannel", FavoriteItemType.Artist, "Star", null, null);

        var target = ShareUrlBuilder.TryCreate(favorite);

        Assert.NotNull(target);
        Assert.Equal("https://music.youtube.com/channel/UCchannel", target!.Url.ToString());
    }

    [Fact]
    public void BuildUrl_is_deterministic()
    {
        Assert.Equal(
            ShareUrlBuilder.BuildUrl(ShareContentKind.Song, "vid"),
            ShareUrlBuilder.BuildUrl(ShareContentKind.Song, "vid"));
    }
}
