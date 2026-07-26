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

    [Fact]
    public async Task UnknownVideoType_IsSettledFromMetadata_AndSubstitutesWhenItIsAVideo()
    {
        // Album/playlist rows never carry a VideoType — requiring a known one is exactly how a
        // video hid behind an album row in round 11.
        var albumRow = Video("omv1", "Song", albumId: null) with { VideoType = null };
        var meta = Video("omv1", "Song", albumId: "MPREb_A"); // metadata names both type and album
        var resolver = Resolver([AlbumTrack("atv1", "Song")], metadata: meta);

        Song? song = await resolver.ResolveAsync(albumRow);

        Assert.Equal("atv1", song?.VideoId);
    }

    [Fact]
    public async Task UnknownVideoType_ThatTurnsOutToBeAudio_IsLeftAlone()
    {
        int albumFetches = 0;
        var meta = AlbumTrack("atv9", "Song"); // metadata says: already the audio version
        var resolver = new SongVersionResolver(
            (_, _) => Task.FromResult<Song?>(meta),
            (_, _) => { albumFetches++; return Task.FromResult<IReadOnlyList<Song>>([]); });

        Song? song = await resolver.ResolveAsync(AlbumTrack("atv9", "Song") with { VideoType = null });

        Assert.Null(song);
        Assert.Equal(0, albumFetches); // settled by the type alone — no album lookup wasted
    }

    // ── Round 15: the album route dead-ends when the album lists the video itself ─────

    [Fact]
    public async Task WhenTheAlbumListsTheVideoItself_TheGatedSearchAnswers()
    {
        // The proven failure: "0 title matches on album …" because the album's own row IS the
        // video id, and the self-exclusion leaves nothing. Search must answer — under gates.
        var video = new Song
        {
            Id = "omv1", VideoId = "omv1", Title = "Lemon Tang",
            VideoType = MusicVideoType.Omv,
            Artists = [new Artist { Id = "", Name = "Hearts2Hearts" }],
            Album = new Album { Id = "MPREb_V", Title = "The 2nd Mini Album" },
        };
        var resolver = new SongVersionResolver(
            (_, _) => Task.FromResult<Song?>(null),
            (_, _) => Task.FromResult<IReadOnlyList<Song>>(
                [video with { VideoType = null }]), // album lists the video id itself
            (_, _) => Task.FromResult<IReadOnlyList<Song>>(
            [
                new Song { Id = "wrong", VideoId = "wrong", Title = "Lemon Tang", VideoType = MusicVideoType.Atv,
                           Artists = [new Artist { Id = "", Name = "Somebody Else" }] },
                new Song { Id = "atv1", VideoId = "atv1", Title = "Lemon Tang", VideoType = MusicVideoType.Atv,
                           Artists = [new Artist { Id = "", Name = "Hearts2Hearts" }] },
            ]));

        Song? song = await resolver.ResolveAsync(video);

        // The artist gate rejects the first hit; the second passes all four gates.
        Assert.Equal("atv1", song?.VideoId);
    }

    [Fact]
    public async Task SearchFallback_RefusesWhenNoCandidatePassesEveryGate()
    {
        var video = new Song
        {
            Id = "omv1", VideoId = "omv1", Title = "Song",
            VideoType = MusicVideoType.Omv,
            Artists = [new Artist { Id = "", Name = "Artist" }],
            Album = new Album { Id = "MPREb_V", Title = "A" },
        };
        var resolver = new SongVersionResolver(
            (_, _) => Task.FromResult<Song?>(null),
            (_, _) => Task.FromResult<IReadOnlyList<Song>>([]),
            (_, _) => Task.FromResult<IReadOnlyList<Song>>(
            [
                // Wrong title, right artist; right title, wrong artist; right pair but a video.
                new Song { Id = "a", VideoId = "a", Title = "Song 2", VideoType = MusicVideoType.Atv, Artists = [new Artist { Id = "", Name = "Artist" }] },
                new Song { Id = "b", VideoId = "b", Title = "Song", VideoType = MusicVideoType.Atv, Artists = [new Artist { Id = "", Name = "Other" }] },
                new Song { Id = "c", VideoId = "c", Title = "Song", VideoType = MusicVideoType.Omv, Artists = [new Artist { Id = "", Name = "Artist" }] },
            ]));

        Assert.Null(await resolver.ResolveAsync(video));
    }

    [Fact]
    public async Task SearchFallback_IsSkippedEntirely_WithoutAnArtistToVerify()
    {
        bool searched = false;
        var resolver = new SongVersionResolver(
            (_, _) => Task.FromResult<Song?>(null),
            (_, _) => Task.FromResult<IReadOnlyList<Song>>([]),
            (_, _) => { searched = true; return Task.FromResult<IReadOnlyList<Song>>([]); });

        var video = new Song
        {
            Id = "omv1", VideoId = "omv1", Title = "Song",
            VideoType = MusicVideoType.Omv,
            Album = new Album { Id = "MPREb_V", Title = "A" },
        };

        Assert.Null(await resolver.ResolveAsync(video));
        Assert.False(searched); // no artist gate possible → no search, no guess
    }

    // ── PlayerService integration: the swap happens before anything observes the load ──

    [Fact]
    public async Task ADeliberatelyPickedVideo_PlaysAsTheVideo()
    {
        // Round-13 correction from the owner: rows the user picks AS a video (music-video cards,
        // search's video tab) carry a known Omv/Ugc type — those play untouched. The substitution
        // exists for videos hiding behind song-context rows, not for videos the user asked for.
        var queue = new QueueService(bound => 0);
        var controller = new FakePlaybackController();
        var resolver = Resolver([AlbumTrack("atv1", "Song")]);
        var player = new PlayerService(
            queue, controller, new FakeJsBridge(),
            songVersionResolver: resolver,
            preferSongVersion: () => true);

        await player.PlaySongAsync(Video("omv1", "Song (Official Video)"));

        Assert.Equal(["omv1"], controller.LoadedVideoIds);
    }

    [Fact]
    public async Task AnAlbumRow_HidingAVideo_ComesOutAsTheSongVersion()
    {
        // Album/single/EP/playlist rows never carry a VideoType; when the id behind one turns out
        // to be a video, the song version must play — and the queue's identity moves with it, or
        // atv1's own reports would look like foreign drift.
        var queue = new QueueService(bound => 0);
        var controller = new FakePlaybackController();
        var meta = Video("omv1", "Song", albumId: "MPREb_A");
        var resolver = Resolver([AlbumTrack("atv1", "Song")], metadata: meta);
        var player = new PlayerService(
            queue, controller, new FakeJsBridge(),
            songVersionResolver: resolver,
            preferSongVersion: () => true);

        await player.PlaySongAsync(Video("omv1", "Song") with { VideoType = null, Album = null });

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

        await player.PlaySongAsync(Video("omv1", "Song") with { VideoType = null, Album = null });

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
