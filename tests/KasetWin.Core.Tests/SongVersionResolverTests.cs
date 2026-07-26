using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using KasetWin.Core.Tests.Properties.Fakes;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Tests for the "prefer the song version" flow: <see cref="SongVersionResolver"/>'s decision
/// table, and <see cref="PlayerService"/> swapping the queue entry before anything observes the
/// load. The counterpart shortcut was proven absent from the watch-next response (signed-in probe:
/// <c>counterpart=False</c>), so album-track title matching is the contract under test.
/// </summary>
public class SongVersionResolverTests
{
    private static Song Video(string videoId, string title, string? albumId = "MPREb_A") => new()
    {
        Id = videoId,
        VideoId = videoId,
        Title = title,
        VideoType = MusicVideoType.Omv,
        Album = albumId is null ? null : new Album { Id = albumId, Title = "Album" },
    };

    private static Song AlbumTrack(string videoId, string title) => new()
    {
        Id = videoId,
        VideoId = videoId,
        Title = title,
        VideoType = MusicVideoType.Atv,
    };

    private static SongVersionResolver Resolver(IReadOnlyList<Song> albumTracks, Song? metadata = null) => new(
        (_, _) => Task.FromResult(metadata),
        (_, _) => Task.FromResult(albumTracks));

    [Fact]
    public async Task Resolves_WhenExactlyOneAlbumTrackMatchesTheDecoratedTitle()
    {
        var resolver = Resolver([AlbumTrack("atv1", "Despacito"), AlbumTrack("atv2", "Otra Cosa")]);

        Song? song = await resolver.ResolveAsync(Video("omv1", "Despacito (Official Video) [4K]"));

        Assert.Equal("atv1", song?.VideoId);
        // The album travels with the swap — landing on the album version is the whole point.
        Assert.Equal("MPREb_A", song?.Album?.Id);
    }

    [Fact]
    public async Task ReturnsNull_WhenTheVideoHasNoAlbumAnywhere()
    {
        var resolver = Resolver([AlbumTrack("atv1", "Song")], metadata: null);

        Assert.Null(await resolver.ResolveAsync(Video("omv1", "Song", albumId: null)));
    }

    [Fact]
    public async Task ReturnsNull_WhenTheMatchIsAmbiguous()
    {
        // Two album rows normalize to the same title (an album with a reprise marked only by
        // brackets). Guessing between them is worse than playing the video that was asked for.
        var resolver = Resolver([AlbumTrack("atv1", "Song"), AlbumTrack("atv2", "Song (Reprise)")]);

        Assert.Null(await resolver.ResolveAsync(Video("omv1", "Song")));
    }

    [Fact]
    public async Task ReturnsNull_WhenTheOnlyMatchIsTheSameVideoId()
    {
        // An album row that references the video itself gains nothing and must not recurse.
        var resolver = Resolver([AlbumTrack("omv1", "Song")]);

        Assert.Null(await resolver.ResolveAsync(Video("omv1", "Song")));
    }

    [Fact]
    public async Task ReturnsNull_WhenALookupThrows()
    {
        var resolver = new SongVersionResolver(
            (_, _) => Task.FromResult<Song?>(null),
            (_, _) => throw new InvalidOperationException("offline"));

        Assert.Null(await resolver.ResolveAsync(Video("omv1", "Song")));
    }

    [Theory]
    [InlineData("Song (Official Music Video)", "song")]
    [InlineData("SONG  [MV]  ", "song")]
    [InlineData("Song（Official）", "song")]
    [InlineData("A (feat. B) C", "a c")]
    public void NormalizeTitle_StripsBracketsAndCase(string raw, string expected) =>
        Assert.Equal(expected, SongVersionResolver.NormalizeTitle(raw));

    // ── PlayerService integration: the swap happens before anything observes the load ──

    [Fact]
    public async Task PlayingAVideo_LoadsTheSongVersion_AndRewritesTheQueueEntry()
    {
        var queue = new QueueService(bound => 0);
        var controller = new FakePlaybackController();
        var resolver = Resolver([AlbumTrack("atv1", "Song")]);
        var player = new PlayerService(
            queue, controller, new FakeJsBridge(),
            songVersionResolver: resolver,
            preferSongVersion: () => true);

        await player.PlaySongAsync(Video("omv1", "Song (Official Video)"));

        // The controller only ever saw the audio id, and the queue's identity moved with it — a
        // queue still holding omv1 would treat atv1's own reports as foreign drift.
        Assert.Equal(["atv1"], controller.LoadedVideoIds);
        Assert.Equal("atv1", queue.CurrentTrack?.VideoId);
        Assert.Equal("atv1", player.CurrentTrack?.VideoId);
    }

    [Fact]
    public async Task PlayingAVideo_PlaysItUnchanged_WhenTheToggleIsOff()
    {
        var queue = new QueueService(bound => 0);
        var controller = new FakePlaybackController();
        var resolver = Resolver([AlbumTrack("atv1", "Song")]);
        var player = new PlayerService(
            queue, controller, new FakeJsBridge(),
            songVersionResolver: resolver,
            preferSongVersion: () => false);

        await player.PlaySongAsync(Video("omv1", "Song"));

        Assert.Equal(["omv1"], controller.LoadedVideoIds);
    }

    [Fact]
    public async Task PlayingAnAudioTrack_NeverConsultsTheResolver()
    {
        var queue = new QueueService(bound => 0);
        var controller = new FakePlaybackController();
        bool consulted = false;
        var resolver = new SongVersionResolver(
            (_, _) => { consulted = true; return Task.FromResult<Song?>(null); },
            (_, _) => { consulted = true; return Task.FromResult<IReadOnlyList<Song>>([]); });
        var player = new PlayerService(
            queue, controller, new FakeJsBridge(),
            songVersionResolver: resolver,
            preferSongVersion: () => true);

        await player.PlaySongAsync(AlbumTrack("atv9", "Song"));

        Assert.False(consulted);
        Assert.Equal(["atv9"], controller.LoadedVideoIds);
    }
}
