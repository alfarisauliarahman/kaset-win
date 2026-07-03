using KasetWin.Core.Models;
using KasetWin.Core.Services.Player;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for the explicit-skip semantics of <see cref="QueueService.AdvanceToNext(bool)"/>
/// (Task 30.7, Req 37.7; upstream #319). An explicit user "Next" must move forward even under
/// <see cref="RepeatMode.One"/> — Repeat One only governs auto-advance at track end. Without this,
/// pressing Next (or the media key) merely replays the current song.
/// </summary>
public class QueueServiceSkipTests
{
    private static Song MakeSong(int i) => new()
    {
        Id = $"v{i}",
        VideoId = $"v{i}",
        Title = $"Title {i}",
    };

    private static QueueService QueueOf(int count, int startIndex, RepeatMode mode)
    {
        var queue = new QueueService();
        queue.SetQueue(Enumerable.Range(0, count).Select(MakeSong).ToList(), startIndex);
        queue.SetRepeatMode(mode);
        return queue;
    }

    [Fact]
    public void ExplicitNext_UnderRepeatOne_MovesToNextTrack()
    {
        var queue = QueueOf(count: 3, startIndex: 0, RepeatMode.One);

        var next = queue.AdvanceToNext(ignoreRepeatOne: true);

        Assert.Equal("v1", next?.VideoId);
        Assert.Equal(1, queue.CurrentIndex);
    }

    [Fact]
    public void AutoAdvance_UnderRepeatOne_StaysOnSameTrack()
    {
        var queue = QueueOf(count: 3, startIndex: 0, RepeatMode.One);

        // Default (auto-advance at track end) keeps Repeat One semantics.
        var next = queue.AdvanceToNext();

        Assert.Equal("v0", next?.VideoId);
        Assert.Equal(0, queue.CurrentIndex);
    }

    [Fact]
    public void ExplicitNext_UnderRepeatOne_AtEnd_WrapsToFirst()
    {
        var queue = QueueOf(count: 3, startIndex: 2, RepeatMode.One);

        var next = queue.AdvanceToNext(ignoreRepeatOne: true);

        Assert.Equal("v0", next?.VideoId);
        Assert.Equal(0, queue.CurrentIndex);
    }

    [Theory]
    [InlineData(RepeatMode.All)]
    [InlineData(RepeatMode.Off)]
    public void ExplicitNext_IgnoreFlag_DoesNotChangeNonRepeatOneModes(RepeatMode mode)
    {
        var withFlag = QueueOf(count: 3, startIndex: 0, mode);
        var withoutFlag = QueueOf(count: 3, startIndex: 0, mode);

        var a = withFlag.AdvanceToNext(ignoreRepeatOne: true);
        var b = withoutFlag.AdvanceToNext();

        Assert.Equal(b?.VideoId, a?.VideoId);
        Assert.Equal("v1", a?.VideoId);
    }

    [Fact]
    public void ExplicitNext_SingleTrackRepeatOne_StaysOnItsOnlyTrack()
    {
        var queue = QueueOf(count: 1, startIndex: 0, RepeatMode.One);

        var next = queue.AdvanceToNext(ignoreRepeatOne: true);

        Assert.Equal("v0", next?.VideoId);
        Assert.Equal(0, queue.CurrentIndex);
    }
}
