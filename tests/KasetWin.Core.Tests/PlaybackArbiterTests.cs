using KasetWin.Core.Services.Player;
using KasetWin.Core.Tests.Properties.Fakes;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="PlaybackArbiter"/> (Feature: kaset-winui3, Task 25.2, Req 32.3).
/// Exercises the single-audio-source guarantee with headless fake sources and the real music
/// player wiring (<see cref="MusicAudioSource"/> + <see cref="PlayerService"/> +
/// <see cref="YouTubePlayerService"/>), so no WebView2/WinRT runtime is required.
/// </summary>
public class PlaybackArbiterTests
{
    [Fact]
    public void Starting_video_pauses_music()
    {
        var music = new FakeAudioSource();
        var video = new FakeAudioSource();
        using var arbiter = new PlaybackArbiter(music, video);

        music.StartPlaying();
        Assert.Equal(AppSource.Music, arbiter.ActiveSource);

        video.StartPlaying();

        Assert.Equal(AppSource.Video, arbiter.ActiveSource);
        Assert.False(music.IsPlaying);
        Assert.True(video.IsPlaying);
        Assert.True(arbiter.RoutesMediaKeysToVideo);
    }

    [Fact]
    public void Starting_music_pauses_video()
    {
        var music = new FakeAudioSource();
        var video = new FakeAudioSource();
        using var arbiter = new PlaybackArbiter(music, video);

        video.StartPlaying();
        Assert.Equal(AppSource.Video, arbiter.ActiveSource);

        music.StartPlaying();

        Assert.Equal(AppSource.Music, arbiter.ActiveSource);
        Assert.False(video.IsPlaying);
        Assert.True(music.IsPlaying);
        Assert.False(arbiter.RoutesMediaKeysToVideo);
    }

    [Fact]
    public void Real_wiring_music_start_pauses_youtube_video()
    {
        // Music player (headless) + YouTube video player + arbiter via the production adapter.
        var queue = new QueueService(_ => 0);
        var controller = new FakePlaybackController();
        var bridge = new FakeJsBridge();
        using var player = new PlayerService(queue, controller, bridge);
        using var musicSource = new MusicAudioSource(player);
        var youtube = new YouTubePlayerService();
        using var arbiter = new PlaybackArbiter(musicSource, youtube);

        // Video starts first → arbiter routes to video.
        youtube.PlayVideoAsync(new Core.Models.YouTubeVideo
        {
            Id = "PLACEHOLDER_VID",
            VideoId = "PLACEHOLDER_VID",
            Title = "Placeholder",
        }).GetAwaiter().GetResult();
        Assert.Equal(AppSource.Video, arbiter.ActiveSource);
        Assert.True(youtube.IsPlaying);

        // Music starts → the arbiter pauses the YouTube video (single audio source, Req 32.3).
        player.PlayAsync("PLACEHOLDER_MUSIC").GetAwaiter().GetResult();

        Assert.Equal(AppSource.Music, arbiter.ActiveSource);
        Assert.True(player.IsPlaying);
        Assert.False(youtube.IsPlaying);
    }

    [Fact]
    public void Real_wiring_video_start_pauses_music()
    {
        var queue = new QueueService(_ => 0);
        var controller = new FakePlaybackController();
        var bridge = new FakeJsBridge();
        using var player = new PlayerService(queue, controller, bridge);
        using var musicSource = new MusicAudioSource(player);
        var youtube = new YouTubePlayerService();
        using var arbiter = new PlaybackArbiter(musicSource, youtube);

        // Music starts first.
        player.PlayAsync("PLACEHOLDER_MUSIC").GetAwaiter().GetResult();
        Assert.Equal(AppSource.Music, arbiter.ActiveSource);
        Assert.True(player.IsPlaying);

        // Video starts → the arbiter pauses music (Req 32.3).
        youtube.PlayVideoAsync(new Core.Models.YouTubeVideo
        {
            Id = "PLACEHOLDER_VID",
            VideoId = "PLACEHOLDER_VID",
            Title = "Placeholder",
        }).GetAwaiter().GetResult();

        Assert.Equal(AppSource.Video, arbiter.ActiveSource);
        Assert.True(youtube.IsPlaying);
        Assert.False(player.IsPlaying);
    }
}
