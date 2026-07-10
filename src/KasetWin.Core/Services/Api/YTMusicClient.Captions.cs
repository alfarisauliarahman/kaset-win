using System.Linq;
using System.Text.Json.Nodes;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api.Parsers;

namespace KasetWin.Core.Services.Api;

/// <summary>
/// Podcast episode captions (CC): reads the video's caption tracks from the InnerTube
/// <c>player</c> response and downloads the chosen track as timed lines (<c>fmt=json3</c>),
/// shaped as <see cref="SyncedLyrics"/> so the lyrics panel renders them like synced lyrics.
/// </summary>
public sealed partial class YTMusicClient
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CaptionTrack>> GetPodcastCaptionTracksAsync(string videoId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        var body = new JsonObject { ["videoId"] = videoId };
        var node = await RequestAsync("player", body, ttl: null, ct).ConfigureAwait(false);

        var trackList = ResponseTreeSearch.FindFirst(node, "playerCaptionsTracklistRenderer");
        if (trackList is not JsonObject tl
            || !tl.TryGetPropertyValue("captionTracks", out var tracksNode)
            || tracksNode is not JsonArray tracks)
        {
            return [];
        }

        var result = new List<CaptionTrack>(tracks.Count);
        foreach (var track in tracks)
        {
            if (track is not JsonObject t
                || t["baseUrl"] is not JsonValue urlValue
                || !urlValue.TryGetValue<string>(out var baseUrl)
                || string.IsNullOrEmpty(baseUrl))
            {
                continue;
            }

            var isAsr = t["kind"] is JsonValue kv && kv.TryGetValue<string>(out var kind)
                && string.Equals(kind, "asr", StringComparison.Ordinal);
            result.Add(new CaptionTrack(ParsingHelpers.ExtractText(t, "name") ?? "CC", baseUrl, isAsr));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<SyncedLyrics?> GetPodcastCaptionsAsync(
        string videoId, string? trackBaseUrl = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        string baseUrl;
        string trackName;
        if (!string.IsNullOrEmpty(trackBaseUrl))
        {
            baseUrl = trackBaseUrl;
            trackName = "CC";
        }
        else
        {
            // Default pick: creator-provided subtitles beat auto-generated (ASR); within each
            // group take the first (YT orders them by the account's language preference already).
            var tracks = await GetPodcastCaptionTracksAsync(videoId, ct).ConfigureAwait(false);
            var chosen = tracks.FirstOrDefault(t => !t.IsAsr) ?? tracks.FirstOrDefault();
            if (chosen is null)
            {
                return null;
            }

            baseUrl = chosen.BaseUrl;
            trackName = chosen.Name;
        }

        // json3 gives events with tStartMs/dDurationMs + utf8 segments (per-word tOffsetMs).
        var url = baseUrl.Contains("fmt=", StringComparison.Ordinal) ? baseUrl : baseUrl + "&fmt=json3";
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Diag.Write($"captions {videoId}: timedtext HTTP {(int)response.StatusCode}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var root = JsonNode.Parse(json);
        if (root is not JsonObject obj
            || !obj.TryGetPropertyValue("events", out var eventsNode)
            || eventsNode is not JsonArray events)
        {
            return null;
        }

        var lines = new List<SyncedLyricLine>(events.Count);
        foreach (var ev in events)
        {
            if (ev is not JsonObject e
                || !e.TryGetPropertyValue("segs", out var segsNode)
                || segsNode is not JsonArray segs)
            {
                continue;
            }

            var start = e["tStartMs"] is JsonValue tv && tv.TryGetValue<long>(out var ms) ? (int)ms : 0;
            var duration = e["dDurationMs"] is JsonValue dv && dv.TryGetValue<long>(out var dm) ? (int)dm : 0;

            // Collect the line text and, when the track carries per-segment offsets (ASR does),
            // per-word timing for a future karaoke rendering.
            var parts = new List<string>(segs.Count);
            List<TimedWord>? words = null;
            foreach (var seg in segs)
            {
                if (seg is not JsonObject so
                    || so["utf8"] is not JsonValue sv
                    || !sv.TryGetValue<string>(out var segText))
                {
                    continue;
                }

                var word = segText.Replace('\n', ' ').Trim();
                parts.Add(segText);
                if (word.Length > 0 && so["tOffsetMs"] is JsonValue ov && ov.TryGetValue<long>(out var offset))
                {
                    (words ??= []).Add(new TimedWord(start + (int)offset, word));
                }
            }

            var text = string.Concat(parts).Replace('\n', ' ').Trim();
            if (text.Length == 0)
            {
                continue;
            }

            lines.Add(new SyncedLyricLine { TimeInMs = start, Duration = duration, Text = text, Words = words });
        }

        Diag.Write($"captions {videoId}: track='{trackName}' lines={lines.Count}");
        return lines.Count == 0 ? null : new SyncedLyrics(lines, trackName);
    }
}
