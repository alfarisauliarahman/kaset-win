using KasetWin.Core.Services.Player;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Unit tests for <see cref="YouTubeWatchMessageParser"/> (Feature: kaset-winui3, Task 25.1 real
/// watch surface, Req 32.2). Validates the untrusted watch-page bridge messages posted by the
/// injected <c>youtubeWatch.js</c> observer, fully headless (no WebView2/WinRT).
/// </summary>
public class YouTubeWatchMessageParserTests
{
    [Fact]
    public void State_update_is_parsed_into_state()
    {
        var message = YouTubeWatchMessageParser.Parse(
            """{"type":"STATE_UPDATE","isPlaying":true,"progress":12.5,"duration":200,"videoId":"vid1","title":"A Song","isAd":false}""");

        Assert.Equal(YouTubeWatchMessageKind.StateUpdate, message.Kind);
        Assert.True(message.IsPlaying);
        Assert.Equal(12.5, message.Progress);
        Assert.Equal(200, message.Duration);
        Assert.Equal("vid1", message.VideoId);
        Assert.Equal("A Song", message.Title);
        Assert.False(message.IsAd);
    }

    [Fact]
    public void State_update_ad_flag_is_read()
    {
        var message = YouTubeWatchMessageParser.Parse(
            """{"type":"STATE_UPDATE","isPlaying":false,"isAd":true}""");

        Assert.Equal(YouTubeWatchMessageKind.StateUpdate, message.Kind);
        Assert.False(message.IsPlaying);
        Assert.True(message.IsAd);
    }

    [Fact]
    public void State_update_negative_progress_is_clamped_to_zero()
    {
        var message = YouTubeWatchMessageParser.Parse(
            """{"type":"STATE_UPDATE","isPlaying":true,"progress":-5,"duration":-1}""");

        Assert.Equal(YouTubeWatchMessageKind.StateUpdate, message.Kind);
        Assert.Equal(0, message.Progress);
        Assert.Equal(0, message.Duration);
    }

    [Fact]
    public void Video_ended_carries_the_video_id()
    {
        var message = YouTubeWatchMessageParser.Parse("""{"type":"VIDEO_ENDED","videoId":"vid7"}""");

        Assert.Equal(YouTubeWatchMessageKind.VideoEnded, message.Kind);
        Assert.Equal("vid7", message.VideoId);
    }

    [Fact]
    public void Video_ended_without_a_video_id_is_ignored()
    {
        var message = YouTubeWatchMessageParser.Parse("""{"type":"VIDEO_ENDED","videoId":""}""");

        Assert.Equal(YouTubeWatchMessageKind.Ignored, message.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"a string\"")]
    [InlineData("{}")]
    [InlineData("""{"type":123}""")]
    [InlineData("""{"type":"UNKNOWN"}""")]
    [InlineData("""{"isPlaying":true}""")]
    public void Malformed_or_unknown_messages_are_ignored(string? json)
    {
        var message = YouTubeWatchMessageParser.Parse(json);

        Assert.Equal(YouTubeWatchMessageKind.Ignored, message.Kind);
    }
}
