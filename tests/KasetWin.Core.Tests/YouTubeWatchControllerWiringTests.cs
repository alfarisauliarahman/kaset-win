using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Tests.Properties.Fakes;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for the real YouTube watch surface wiring (Feature: kaset-winui3, Task 25.1 real
/// watch surface, Req 32.2/32.3). Exercises how <see cref="YouTubePlayerService"/> drives the
/// <see cref="IYouTubeWatchController"/> seam and how the observer→player→arbiter chain enforces a
/// single audio source — all headless against <see cref="FakeYouTubeWatchController"/>, mirroring
/// the App composition root's bridge wiring (no WebView2/WinRT runtime).
/// </summary>
public class YouTubeWatchControllerWiringTests
{
    private static YouTubeVideo Video(string id) => new()
    {
        Id = id,
        VideoId = id,
        Title = "Placeholder",
    };

    [Fact]
    public void Playing_a_video_loads_and_plays_through_the_controller()
    {
        var controller = new FakeYouTubeWatchController();
        var player = new YouTubePlayerService(controller);

        player.PlayVideoAsync(Video("PLACEHOLDER_VID")).GetAwaiter().GetResult();

        Assert.Equal(1, controller.LoadCount);
        Assert.Equal(1, controller.PlayCount);
        Assert.Equal("PLACEHOLDER_VID", controller.CurrentVideoId);
        Assert.True(player.IsPlaying);
        Assert.Equal("PLACEHOLDER_VID", player.CurrentVideo?.VideoId);
    }

    [Fact]
    public void Observed_in_page_play_pauses_music_via_the_arbiter()
    {
        // Wire the watch controller's observer event into the player exactly as AppHost does.
        var controller = new FakeYouTubeWatchController();
        var youtube = new YouTubePlayerService(controller);
        controller.PlaybackStateObserved += (_, isPlaying) => youtube.ReportPlaying(isPlaying);

        var music = new FakeAudioSource();
        using var arbiter = new PlaybackArbiter(music, youtube);

        music.StartPlaying();
        Assert.Equal(AppSource.Music, arbiter.ActiveSource);

        // The page reports the <video> started playing (e.g. autoplay) — arbiter must pause music.
        controller.SimulateObservedPlaying(true);

        Assert.Equal(AppSource.Video, arbiter.ActiveSource);
        Assert.True(youtube.IsPlaying);
        Assert.False(music.IsPlaying);
    }

    [Fact]
    public void Arbiter_pausing_the_video_actually_pauses_the_controller()
    {
        // The real-source pause path: when music starts, the arbiter pauses the YouTube source,
        // which must drive the watch controller's PauseAsync (the real WebView2 pause, Req 32.3).
        var controller = new FakeYouTubeWatchController();
        var youtube = new YouTubePlayerService(controller);
        var music = new FakeAudioSource();
        using var arbiter = new PlaybackArbiter(music, youtube);

        youtube.PlayVideoAsync(Video("PLACEHOLDER_VID")).GetAwaiter().GetResult();
        Assert.Equal(AppSource.Video, arbiter.ActiveSource);
        Assert.True(youtube.IsPlaying);

        // Music starts → arbiter pauses the video source → controller.PauseAsync is invoked.
        music.StartPlaying();

        Assert.Equal(AppSource.Music, arbiter.ActiveSource);
        Assert.False(youtube.IsPlaying);
        Assert.Equal(1, controller.PauseCount);
    }
}
