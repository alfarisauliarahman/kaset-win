using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="LikeStateStore"/> (session like/collection sync) and
/// <see cref="QueueService.InsertNext"/> ("Putar setelah ini").
/// </summary>
public sealed class LikeStateStoreTests
{
    private static Song MakeSong(int i) => new()
    {
        Id = $"v{i}",
        VideoId = $"v{i}",
        Title = $"Title {i}",
    };

    [Fact]
    public void Set_then_TryGet_roundtrips_all_statuses()
    {
        var store = new LikeStateStore();

        store.Set("a", LikeStatus.Like);
        store.Set("b", LikeStatus.Dislike);
        store.Set("c", LikeStatus.Indifferent);

        Assert.True(store.TryGet("a", out var like));
        Assert.Equal(LikeStatus.Like, like);
        Assert.True(store.TryGet("b", out var dislike));
        Assert.Equal(LikeStatus.Dislike, dislike);
        Assert.True(store.TryGet("c", out var cleared));
        Assert.Equal(LikeStatus.Indifferent, cleared);
    }

    [Fact]
    public void TryGet_unknown_or_empty_id_is_false()
    {
        var store = new LikeStateStore();

        Assert.False(store.TryGet("nope", out _));
        Assert.False(store.TryGet(string.Empty, out _));
    }

    [Fact]
    public void Set_overwrites_and_raises_Changed_with_the_videoId()
    {
        var store = new LikeStateStore();
        var changes = new List<string>();
        store.Changed += changes.Add;

        store.Set("a", LikeStatus.Like);
        store.Set("a", LikeStatus.Indifferent);
        store.Set(string.Empty, LikeStatus.Like); // ignored: no id

        Assert.True(store.TryGet("a", out var status));
        Assert.Equal(LikeStatus.Indifferent, status);
        Assert.Equal(new[] { "a", "a" }, changes);
    }

    [Fact]
    public void InsertNext_places_songs_right_after_the_current_track_and_dedupes()
    {
        var queue = new QueueService();
        queue.SetQueue(new[] { MakeSong(0), MakeSong(1), MakeSong(2) }, startIndex: 0);

        // v1 already queued → skipped; v9 inserted directly after the current track (index 0).
        var added = queue.InsertNext(new[] { MakeSong(9), MakeSong(1) });

        Assert.Equal(1, added);
        Assert.Equal(new[] { "v0", "v9", "v1", "v2" }, queue.Tracks.Select(t => t.VideoId).ToArray());
        Assert.Equal(0, queue.CurrentIndex); // current track untouched
    }

    [Fact]
    public void InsertNext_on_an_empty_queue_appends_and_makes_the_first_current()
    {
        var queue = new QueueService();

        var added = queue.InsertNext(new[] { MakeSong(0), MakeSong(1) });

        Assert.Equal(2, added);
        Assert.Equal(0, queue.CurrentIndex);
        Assert.Equal("v0", queue.CurrentTrack?.VideoId);
    }
}
