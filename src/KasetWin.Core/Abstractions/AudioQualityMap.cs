using KasetWin.Core.Models;

namespace KasetWin.Core.Abstractions;

/// <summary>
/// Pure, total mapping from the app's <see cref="AudioQuality"/> preference to the YouTube
/// player quality string passed to <c>setAudioQuality</c>/<c>setOption</c> (Req 7.1/7.3).
/// </summary>
/// <remarks>
/// Mapping (Design: "Audio quality"): <c>Low → "small"</c>, <c>Medium → "medium"</c>,
/// <c>High → "highres"</c>. Kept as a pure helper in <c>Core</c> so it can be exercised
/// headless by Property 19 ("Pemetaan kualitas audio bersifat total") without WebView2.
/// </remarks>
public static class AudioQualityMap
{
    /// <summary>
    /// Returns the YouTube player quality string for <paramref name="quality"/>. Total over
    /// every defined <see cref="AudioQuality"/> value; an undefined value throws
    /// <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    public static string ToYouTubeValue(AudioQuality quality) => quality switch
    {
        AudioQuality.Low => "small",
        AudioQuality.Medium => "medium",
        AudioQuality.High => "highres",
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "Unknown audio quality."),
    };
}
