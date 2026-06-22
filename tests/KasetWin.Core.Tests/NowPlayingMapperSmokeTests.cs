using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Smoke tests for the SMTC now-playing projection (task 12.2, Req 10.1/10.3). The real
/// <c>SmtcController</c> lives in <c>KasetWin.Platform</c> and needs a live WinRT
/// <c>MediaPlayer</c>/SMTC session, which cannot be created on the headless runner. The
/// metadata/status mapping that <i>feeds</i> the SMTC was extracted into the pure
/// <see cref="NowPlayingMapper"/> the controller now delegates to, so it is verified here.
/// The actual <c>DisplayUpdater.Update()</c> / <c>MediaPlaybackStatus</c> plumbing and media-button
/// forwarding require a live SMTC and are out of scope for this headless test.
/// </summary>
public class NowPlayingMapperSmokeTests
{
    private static Song MakeSong(
        string videoId = "vid1",
        string title = "Some Song",
        string[]? artists = null,
        Album? album = null,
        Uri? thumbnail = null) => new()
    {
        Id = videoId,
        VideoId = videoId,
        Title = title,
        Artists = (artists ?? ["Artist One", "Artist Two"])
            .Select(name => new Artist { Id = "UC" + name, Name = name })
            .ToList(),
        Album = album,
        ThumbnailUrl = thumbnail,
    };

    // ── Status mapping (Req 10.3) ──────────────────────────────────────────────────────────

    [Fact]
    public void Status_is_closed_when_no_track()
    {
        Assert.Equal(NowPlayingStatus.Closed, NowPlayingMapper.MapStatus(track: null, isPlaying: true));
        Assert.Equal(NowPlayingStatus.Closed, NowPlayingMapper.MapStatus(track: null, isPlaying: false));
    }

    [Fact]
    public void Status_is_playing_when_track_is_playing()
    {
        Assert.Equal(NowPlayingStatus.Playing, NowPlayingMapper.MapStatus(MakeSong(), isPlaying: true));
    }

    [Fact]
    public void Status_is_paused_when_track_is_not_playing()
    {
        Assert.Equal(NowPlayingStatus.Paused, NowPlayingMapper.MapStatus(MakeSong(), isPlaying: false));
    }

    // ── Display mapping (Req 10.1) ─────────────────────────────────────────────────────────

    [Fact]
    public void Display_is_null_when_no_track()
    {
        Assert.Null(NowPlayingMapper.MapDisplay(track: null));
    }

    [Fact]
    public void Display_projects_title_and_joined_artists()
    {
        var display = NowPlayingMapper.MapDisplay(MakeSong(title: "Hello", artists: ["A", "B"]));

        Assert.NotNull(display);
        Assert.Equal("Hello", display!.Title);
        Assert.Equal("A, B", display.Artist);
    }

    [Fact]
    public void Display_includes_album_title_when_present()
    {
        var album = new Album { Id = "MPRE1", Title = "Greatest Hits" };
        var display = NowPlayingMapper.MapDisplay(MakeSong(album: album));

        Assert.Equal("Greatest Hits", display!.AlbumTitle);
    }

    [Fact]
    public void Display_omits_album_title_when_absent_or_empty()
    {
        var noAlbum = NowPlayingMapper.MapDisplay(MakeSong(album: null));
        Assert.Null(noAlbum!.AlbumTitle);

        var emptyAlbum = NowPlayingMapper.MapDisplay(
            MakeSong(album: new Album { Id = "MPRE2", Title = string.Empty }));
        Assert.Null(emptyAlbum!.AlbumTitle);
    }

    [Fact]
    public void Display_uses_track_thumbnail_when_available()
    {
        var thumb = new Uri("https://example.test/art.jpg");
        var display = NowPlayingMapper.MapDisplay(MakeSong(thumbnail: thumb));

        Assert.Equal(thumb, display!.ArtworkUri);
    }

    [Fact]
    public void Display_falls_back_to_deterministic_videoid_thumbnail()
    {
        var display = NowPlayingMapper.MapDisplay(MakeSong(videoId: "vidXYZ", thumbnail: null));

        // Always-present artwork derived from the videoId (Req 10.1).
        Assert.Equal(new Uri("https://i.ytimg.com/vi/vidXYZ/hqdefault.jpg"), display!.ArtworkUri);
    }

    [Fact]
    public void Display_title_is_never_null_for_a_track()
    {
        var display = NowPlayingMapper.MapDisplay(MakeSong(title: string.Empty));

        Assert.NotNull(display);
        Assert.Equal(string.Empty, display!.Title);
    }
}
