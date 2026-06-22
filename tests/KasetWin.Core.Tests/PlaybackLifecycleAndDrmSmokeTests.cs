using KasetWin.Core.Services.Player;
using KasetWin.Core.Tests.Properties.Fakes;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Smoke tests for the playback WebView2 singleton/lifecycle and DRM detection (task 8.5,
/// Req 1.1/1.3/1.5/1.7). The real <c>WebView2PlaybackController</c> needs a live
/// <c>CoreWebView2</c>, which cannot be created on the headless test runner, so these tests
/// cover the two headless-reachable seams:
/// <list type="bullet">
///   <item>the untrusted bridge-message validation + DRM-availability logic, now extracted into
///   the pure <see cref="PlaybackMessageParser"/> the controller delegates to; and</item>
///   <item>the singleton lifecycle contract (init-once / release) via
///   <see cref="FakeLifecyclePlaybackController"/>.</item>
/// </list>
/// The live <c>CoreWebView2</c> creation/attach and the actual EME probe are out of scope here
/// (they require a real WebView2 runtime and are exercised manually / in app smoke runs).
/// </summary>
public class PlaybackLifecycleAndDrmSmokeTests
{
    // ── Singleton / lifecycle (Req 1.1, 1.5) ───────────────────────────────────────────────

    [Fact]
    public async Task EnsureInitialized_creates_a_single_instance_and_is_idempotent()
    {
        var controller = new FakeLifecyclePlaybackController();

        await controller.EnsureInitializedAsync();
        await controller.EnsureInitializedAsync();
        await controller.EnsureInitializedAsync();

        Assert.True(controller.IsInitialized);
        Assert.Equal(1, controller.InitializeCount); // one and only one surface (Req 1.1)
    }

    [Fact]
    public async Task Release_tears_down_and_a_later_initialize_recreates_once()
    {
        var controller = new FakeLifecyclePlaybackController();

        await controller.EnsureInitializedAsync();
        await controller.ReleaseAsync(); // app quit (Req 1.5)

        Assert.False(controller.IsInitialized);

        await controller.EnsureInitializedAsync();

        Assert.True(controller.IsInitialized);
        Assert.Equal(2, controller.InitializeCount); // exactly one creation per live lifetime
    }

    [Fact]
    public void Controller_reports_drm_available_by_default()
    {
        // The Evergreen WebView2 runtime ships Widevine, so the surface starts optimistic until a
        // probe says otherwise (Req 1.7).
        var controller = new FakeLifecyclePlaybackController();

        Assert.True(controller.IsDrmAvailable);
    }

    // ── DRM detection message path (Req 1.3, 1.7) ──────────────────────────────────────────

    [Fact]
    public void Drm_unavailable_message_maps_to_unavailable_state()
    {
        var controller = new FakeLifecyclePlaybackController();

        var message = controller.ApplyWebMessage("""{"type":"DRM_STATUS","available":false}""");

        Assert.Equal(PlaybackMessageKind.DrmStatus, message.Kind);
        Assert.False(message.DrmAvailable);
        Assert.False(controller.IsDrmAvailable); // app would surface "playback unavailable" (Req 1.7)
    }

    [Fact]
    public void Drm_available_message_keeps_playback_available()
    {
        var controller = new FakeLifecyclePlaybackController();

        // Flip to unavailable, then back — the latest probe wins.
        controller.ApplyWebMessage("""{"type":"DRM_STATUS","available":false}""");
        var message = controller.ApplyWebMessage("""{"type":"DRM_STATUS","available":true}""");

        Assert.Equal(PlaybackMessageKind.DrmStatus, message.Kind);
        Assert.True(message.DrmAvailable);
        Assert.True(controller.IsDrmAvailable);
    }

    [Theory]
    [InlineData("""{"type":"DRM_STATUS"}""")]                 // missing 'available'
    [InlineData("""{"type":"DRM_STATUS","available":"no"}""")] // non-boolean 'available'
    public void Drm_status_without_a_boolean_flag_does_not_change_state(string json)
    {
        var controller = new FakeLifecyclePlaybackController();

        var message = controller.ApplyWebMessage(json);

        Assert.Equal(PlaybackMessageKind.Ignored, message.Kind);
        Assert.True(controller.IsDrmAvailable); // unchanged from default
    }

    // ── Untrusted message validation (Req 2 defensive parsing) ─────────────────────────────

    [Fact]
    public void State_update_message_is_parsed_into_a_state()
    {
        var message = PlaybackMessageParser.Parse(
            """{"type":"STATE_UPDATE","isPlaying":true,"progress":12.5,"duration":200,"videoId":"vid1","title":"Song","artist":"Artist","trackChanged":true,"hasVideo":false}""");

        Assert.Equal(PlaybackMessageKind.StateUpdate, message.Kind);
        Assert.NotNull(message.State);
        Assert.True(message.State!.IsPlaying);
        Assert.Equal(12.5, message.State.Progress);
        Assert.Equal("vid1", message.State.VideoId);
        Assert.True(message.State.TrackChanged);
        Assert.False(message.State.HasVideo);
    }

    [Fact]
    public void Track_ended_message_carries_the_video_id()
    {
        var message = PlaybackMessageParser.Parse("""{"type":"TRACK_ENDED","videoId":"vid7"}""");

        Assert.Equal(PlaybackMessageKind.TrackEnded, message.Kind);
        Assert.Equal("vid7", message.TrackEndedMessage!.VideoId);
    }

    [Fact]
    public void Track_ended_without_a_video_id_is_ignored()
    {
        var message = PlaybackMessageParser.Parse("""{"type":"TRACK_ENDED","videoId":""}""");

        Assert.Equal(PlaybackMessageKind.Ignored, message.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]                          // not an object
    [InlineData("\"a string\"")]                // not an object
    [InlineData("{}")]                          // no type
    [InlineData("""{"type":123}""")]            // type not a string
    [InlineData("""{"type":"UNKNOWN_KIND"}""")] // unknown type
    public void Malformed_or_unknown_messages_are_ignored(string? json)
    {
        var message = PlaybackMessageParser.Parse(json);

        Assert.Equal(PlaybackMessageKind.Ignored, message.Kind);
        Assert.Null(message.State);
        Assert.Null(message.TrackEndedMessage);
        Assert.Null(message.DrmAvailable);
    }
}
