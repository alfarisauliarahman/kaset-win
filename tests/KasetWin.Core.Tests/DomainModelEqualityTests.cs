using KasetWin.Core.Models;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for the core domain records (Task 3.2, Req 16.1): value-based equality and a
/// stable, non-empty <c>Id</c> identity. These records back UI virtualization, so equal
/// values must compare equal and every model must carry a non-empty identity.
/// </summary>
public class DomainModelEqualityTests
{
    private static Song MakeSong(string id = "vid_123") =>
        new() { Id = id, VideoId = id, Title = "A Song" };

    private static Artist MakeArtist(string id = "UC_artist_1") =>
        new() { Id = id, Name = "An Artist" };

    private static Album MakeAlbum(string id = "MPREb_album_1") =>
        new() { Id = id, Title = "An Album" };

    private static Playlist MakePlaylist(string id = "VL_playlist_1") =>
        new() { Id = id, Title = "A Playlist" };

    [Fact]
    public void Song_records_with_equal_values_are_equal()
    {
        var a = MakeSong();
        var b = MakeSong();

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Song_records_with_different_identity_are_not_equal()
    {
        var a = MakeSong("vid_aaa");
        var b = a with { Id = "vid_bbb", VideoId = "vid_bbb" };

        Assert.NotEqual(a, b);
        Assert.False(a == b);
    }

    [Fact]
    public void Artist_records_value_equality()
    {
        Assert.Equal(MakeArtist(), MakeArtist());
        Assert.NotEqual(MakeArtist("UC_a"), MakeArtist("UC_b"));
    }

    [Fact]
    public void Album_records_value_equality()
    {
        Assert.Equal(MakeAlbum(), MakeAlbum());
        Assert.NotEqual(MakeAlbum("MPRE_a"), MakeAlbum("MPRE_b"));
    }

    [Fact]
    public void Playlist_records_value_equality()
    {
        Assert.Equal(MakePlaylist(), MakePlaylist());
        Assert.NotEqual(MakePlaylist("VL_a"), MakePlaylist("VL_b"));
    }

    [Fact]
    public void HomeSectionItem_ids_are_kind_prefixed_and_nonempty()
    {
        HomeSectionItem song = new HomeSectionItem.SongItem(MakeSong("vid_x"));
        HomeSectionItem album = new HomeSectionItem.AlbumItem(MakeAlbum("MPRE_x"));
        HomeSectionItem playlist = new HomeSectionItem.PlaylistItem(MakePlaylist("VL_x"));
        HomeSectionItem artist = new HomeSectionItem.ArtistItem(MakeArtist("UC_x"));

        Assert.Equal("song-vid_x", song.Id);
        Assert.Equal("album-MPRE_x", album.Id);
        Assert.Equal("playlist-VL_x", playlist.Id);
        Assert.Equal("artist-UC_x", artist.Id);

        foreach (var item in new[] { song, album, playlist, artist })
        {
            Assert.False(string.IsNullOrEmpty(item.Id));
        }
    }

    [Theory]
    [InlineData("vid_123")]
    [InlineData("another_id")]
    public void Core_models_carry_nonempty_identity(string id)
    {
        Assert.False(string.IsNullOrEmpty(MakeSong(id).Id));
        Assert.False(string.IsNullOrEmpty(MakeArtist(id).Id));
        Assert.False(string.IsNullOrEmpty(MakeAlbum(id).Id));
        Assert.False(string.IsNullOrEmpty(MakePlaylist(id).Id));
    }

    [Fact]
    public void Song_with_copy_preserves_unchanged_fields_and_equality()
    {
        var original = MakeSong("vid_copy");
        var copy = original with { };

        Assert.Equal(original, copy);

        var changed = original with { Title = "Different" };
        Assert.NotEqual(original, changed);
        Assert.Equal(original.Id, changed.Id);
    }
}
