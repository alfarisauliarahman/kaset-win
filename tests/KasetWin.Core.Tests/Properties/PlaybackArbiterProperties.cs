using CsCheck;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Tests.Properties.Fakes;
using Xunit;

namespace KasetWin.Core.Tests.Properties;

/// <summary>
/// Property-based test for <see cref="PlaybackArbiter"/> (Feature: kaset-winui3, Task 25.2,
/// Req 32.3). Verifies the single-audio-source invariant holds across any interleaving of source
/// starts. Runs headless against <see cref="FakeAudioSource"/> — no WebView2/WinRT runtime required.
/// </summary>
public class PlaybackArbiterProperties
{
    // Feature: kaset-winui3, Property: Hanya satu sumber audio aktif pada satu waktu
    // Validates: Requirements 32.3
    [Fact]
    public void Only_one_audio_source_is_active_at_a_time()
    {
        // For any sequence of source-start events (true = video, false = music), after each start
        // the two sources are never both playing, the just-started source is playing, and the
        // arbiter's active source / media-key routing reflect the most recent start.
        Gen.Bool.Array[1, 20].Sample(
            events =>
            {
                var music = new FakeAudioSource();
                var video = new FakeAudioSource();
                using var arbiter = new PlaybackArbiter(music, video);

                foreach (bool startVideo in events)
                {
                    if (startVideo)
                    {
                        video.StartPlaying();
                    }
                    else
                    {
                        music.StartPlaying();
                    }

                    // Single audio source: never both playing.
                    Assert.False(music.IsPlaying && video.IsPlaying);

                    // The source that just started is the one playing and the active source.
                    if (startVideo)
                    {
                        Assert.True(video.IsPlaying);
                        Assert.False(music.IsPlaying);
                        Assert.Equal(AppSource.Video, arbiter.ActiveSource);
                        Assert.True(arbiter.RoutesMediaKeysToVideo);
                    }
                    else
                    {
                        Assert.True(music.IsPlaying);
                        Assert.False(video.IsPlaying);
                        Assert.Equal(AppSource.Music, arbiter.ActiveSource);
                        Assert.False(arbiter.RoutesMediaKeysToVideo);
                    }
                }
            },
            iter: 100);
    }
}
