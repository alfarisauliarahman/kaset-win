using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="QueueService.InsertNext(System.Collections.Generic.IEnumerable{Song})"/>
/// ("Putar setelah ini"): songs land right after the current track, duplicates are skipped, and an
/// empty queue is seeded so the first inserted track becomes current.
/// </summary>
public class QueueServiceInsertNextTests
{
    private static Song MakeSong(int i) => new()
    {
        Id = $"v{i}",
        VideoId = $"v{i}",
        Title = $"Title {i}",
    };

    private static QueueService QueueOf(int count, int startIndex)
    {
        var queue = new QueueService();
        queue.SetQueue(Enumerable.Range(0, count).Select(MakeSong).ToList(), startIndex);
        return queue;
    }

    [Fact]
    public void InsertNext_PlacesTrackImmediatelyAfterCurrent()
    {
        var queue = QueueOf(count: 3, startIndex: 0);

        var added = queue.InsertNext([MakeSong(9)]);

        Assert.Equal(1, added);
        Assert.Equal("v9", queue.Tracks[1].VideoId);
        Assert.Equal(0, queue.CurrentIndex); // current track unaffected
    }

    [Fact]
    public void InsertNext_PreservesBatchOrderAfterCurrent()
    {
        var queue = QueueOf(count: 2, startIndex: 0);

        queue.InsertNext([MakeSong(7), MakeSong(8)]);

        Assert.Equal("v7", queue.Tracks[1].VideoId);
        Assert.Equal("v8", queue.Tracks[2].VideoId);
    }

    [Fact]
    public void InsertNext_SkipsDuplicateVideoIds()
    {
        var queue = QueueOf(count: 3, startIndex: 0);

        var added = queue.InsertNext([MakeSong(1)]); // already present

        Assert.Equal(0, added);
        Assert.Equal(3, queue.Tracks.Count);
    }

    [Fact]
    public void InsertNext_OnEmptyQueue_SeedsCurrentTrack()
    {
        var queue = new QueueService();

        var added = queue.InsertNext([MakeSong(0), MakeSong(1)]);

        Assert.Equal(2, added);
        Assert.Equal(0, queue.CurrentIndex);
        Assert.Equal("v0", queue.CurrentTrack?.VideoId);
    }
}
