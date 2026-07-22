using CsCheck;
using KasetWin.Core.Models;
using KasetWin.Core.Services.RichPresence;
using Xunit;

namespace KasetWin.Core.Tests;

/// <summary>
/// Tests for <see cref="DiscordActivityBuilder"/> — the pure mapping from player state to a Discord
/// presence payload. Field-length clamping matters more than it looks: Discord rejects an activity
/// whose details/state falls outside 2–128 characters and reports nothing, so an unclamped field
/// shows up as "rich presence just doesn't work" with no error anywhere.
/// </summary>
public class DiscordActivityBuilderTests
{
    private const long Now = 1_700_000_000;

    private static Song MakeSong(
        string title = "Bohemian Rhapsody",
        string[]? artists = null,
        string? album = "A Night at the Opera",
        string? thumbnail = "https://example.invalid/art.jpg") => new()
        {
            Id = "v1",
            VideoId = "v1",
            Title = title,
            Artists = (artists ?? ["Queen"]).Select(a => new Artist { Id = a, Name = a }).ToList(),
            Album = album is null ? null : new Album { Id = "a1", Title = album },
            ThumbnailUrl = thumbnail is null ? null : new Uri(thumbnail),
        };

    [Fact]
    public void Returns_null_without_a_track()
    {
        Assert.Null(DiscordActivityBuilder.Build(null, isPlaying: true, 0, 0, Now));
    }

    [Fact]
    public void Returns_null_for_a_titleless_track()
    {
        Assert.Null(DiscordActivityBuilder.Build(MakeSong(title: "   "), isPlaying: true, 0, 0, Now));
    }

    [Fact]
    public void Maps_title_artists_album_and_artwork()
    {
        var activity = DiscordActivityBuilder.Build(
            MakeSong(artists: ["Queen", "Freddie Mercury"]), isPlaying: true, 30, 355, Now);

        Assert.NotNull(activity);
        Assert.Equal("Bohemian Rhapsody", activity!.Value.Details);
        Assert.Equal("Queen, Freddie Mercury", activity.Value.State);
        Assert.Equal("A Night at the Opera", activity.Value.LargeImageText);
        Assert.Equal("https://example.invalid/art.jpg", activity.Value.LargeImageUrl);
    }

    [Fact]
    public void Start_timestamp_accounts_for_current_position()
    {
        // Discord renders elapsed time from `start`. Passing "now" would restart the counter at zero
        // on every seek and every reconnect, so it has to be now-minus-progress.
        var activity = DiscordActivityBuilder.Build(MakeSong(), isPlaying: true, progress: 42, duration: 300, Now);

        Assert.Equal(Now - 42, activity!.Value.StartUnixSeconds);
        Assert.Equal(Now - 42 + 300, activity.Value.EndUnixSeconds);
    }

    [Fact]
    public void Paused_playback_carries_no_timestamps()
    {
        var activity = DiscordActivityBuilder.Build(MakeSong(), isPlaying: false, 42, 300, Now);

        Assert.NotNull(activity);
        Assert.Null(activity!.Value.StartUnixSeconds);
        Assert.Null(activity.Value.EndUnixSeconds);
    }

    [Fact]
    public void Unknown_duration_yields_start_without_end()
    {
        // Live streams report a non-positive duration; an end timestamp would be a lie.
        var activity = DiscordActivityBuilder.Build(MakeSong(), isPlaying: true, 10, duration: 0, Now);

        Assert.Equal(Now - 10, activity!.Value.StartUnixSeconds);
        Assert.Null(activity.Value.EndUnixSeconds);
    }

    [Fact]
    public void Missing_artists_fall_back_rather_than_producing_an_empty_state()
    {
        var activity = DiscordActivityBuilder.Build(MakeSong(artists: []), isPlaying: true, 0, 100, Now);

        Assert.Equal("Unknown artist", activity!.Value.State);
    }

    [Fact]
    public void Album_absent_means_no_hover_text()
    {
        var activity = DiscordActivityBuilder.Build(MakeSong(album: null), isPlaying: true, 0, 100, Now);

        Assert.Null(activity!.Value.LargeImageText);
    }

    [Fact]
    public void No_artwork_means_no_image_url()
    {
        var activity = DiscordActivityBuilder.Build(MakeSong(thumbnail: null), isPlaying: true, 0, 100, Now);

        Assert.Null(activity!.Value.LargeImageUrl);
    }

    [Fact]
    public void Negative_or_non_finite_progress_never_pushes_start_into_the_future()
    {
        foreach (var bad in new[] { -5d, double.NaN, double.NegativeInfinity })
        {
            var activity = DiscordActivityBuilder.Build(MakeSong(), isPlaying: true, bad, 100, Now);
            Assert.Equal(Now, activity!.Value.StartUnixSeconds);
        }
    }

    // Feature: kaset-winui3, Property: Discord fields always fit the 2–128 character window
    [Fact]
    public void Property_clamped_fields_always_satisfy_discord_length_limits()
    {
        var text = Gen.Char[' ', 'z'].Array[0, 400].Select(chars => new string(chars));

        text.Sample(
            raw =>
            {
                var clamped = DiscordActivityBuilder.Clamp(raw);

                Assert.True(
                    clamped.Length >= DiscordActivityBuilder.MinFieldLength,
                    $"'{raw}' clamped to {clamped.Length} chars, below Discord's minimum.");
                Assert.True(
                    clamped.Length <= DiscordActivityBuilder.MaxFieldLength,
                    $"'{raw}' clamped to {clamped.Length} chars, above Discord's maximum.");
            },
            iter: 100);
    }

    [Fact]
    public void Long_titles_are_ellipsised_rather_than_dropped()
    {
        var activity = DiscordActivityBuilder.Build(
            MakeSong(title: new string('x', 300)), isPlaying: true, 0, 100, Now);

        Assert.Equal(DiscordActivityBuilder.MaxFieldLength, activity!.Value.Details.Length);
        Assert.EndsWith("…", activity.Value.Details, StringComparison.Ordinal);
    }
}
