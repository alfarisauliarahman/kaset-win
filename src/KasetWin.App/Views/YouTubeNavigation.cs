using KasetWin.App.ViewModels;
using KasetWin.Core.Models;
using Microsoft.UI.Xaml.Controls;

namespace KasetWin.App.Views;

/// <summary>
/// Navigation parameter describing which YouTube (full mode) feed a <c>YouTubeFeedPage</c> should
/// render (Task 25.1, Req 32.1) — Subscriptions, History, or an Explore destination.
/// </summary>
public sealed record YouTubeFeedRequest(YouTubeFeedKind Kind, YouTubeDestination Destination = YouTubeDestination.Gaming);

/// <summary>
/// Routes activation of a YouTube (full mode) video card to the watch page (Task 25.1, Req 32.2).
/// Mirrors the music <see cref="FeedNavigation"/> helper: navigation is guarded by resolving the
/// page <see cref="Type"/> by name so the surface keeps compiling even if the watch page is swapped
/// out, and a Short or a regular video both open the same watch view (the watch ViewModel drives
/// playback through the arbitrated YouTube video player).
/// </summary>
internal static class YouTubeNavigation
{
    private const string WatchPageTypeName = "KasetWin.App.Views.YouTubeWatchPage";

    /// <summary>Opens <paramref name="video"/> in the watch page, passing its videoId as the parameter.</summary>
    public static void OpenWatch(Frame? frame, YouTubeVideo? video)
    {
        if (frame is null || video is null || string.IsNullOrEmpty(video.VideoId))
        {
            return;
        }

        OpenWatch(frame, video.VideoId);
    }

    /// <summary>Opens the watch page for <paramref name="videoId"/> (Req 32.2).</summary>
    public static void OpenWatch(Frame? frame, string videoId)
    {
        if (frame is null || string.IsNullOrEmpty(videoId))
        {
            return;
        }

        if (Type.GetType(WatchPageTypeName) is { } pageType)
        {
            frame.Navigate(pageType, videoId);
        }
    }
}
