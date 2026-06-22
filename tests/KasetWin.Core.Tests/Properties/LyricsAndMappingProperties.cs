using System;
using System.Globalization;
using System.Linq;
using CsCheck;
using KasetWin.Core.Abstractions;
using KasetWin.Core.Errors;
using KasetWin.Core.Models;
using KasetWin.Core.Services.Api;
using KasetWin.Core.Services.Lyrics;
using Xunit;

namespace KasetWin.Core.Tests.Properties;

/// <summary>
/// Property-based tests (CsCheck, min 100 iterations each) for the kaset-winui3 lyrics and
/// id/enum mapping pure logic:
/// <list type="bullet">
///   <item><description>Property 21 — LRC round-trip (Parse → Format → Parse).</description></item>
///   <item><description>Property 22 — synced-lyric highlighting is monotonic in time.</description></item>
///   <item><description>Property 19 — <see cref="AudioQualityMap.ToYouTubeValue"/> is total.</description></item>
///   <item><description>Property 28 — <see cref="YTMusicIds.StripVlPrefix"/> removes at most one VL.</description></item>
///   <item><description>Property 36 — <see cref="YTMusicIds.ConvertPodcastShowIdToPlaylistId"/> MPSPP→P.</description></item>
///   <item><description>Property 37 — <see cref="MusicVideoTypeExtensions.HasVideoContent"/> true IFF OMV.</description></item>
/// </list>
/// Each property is a single [Fact]. No real secrets/PII are used; all ids are synthetic.
/// </summary>
public class LyricsAndMappingProperties
{
    // ---- Shared alphabets ---------------------------------------------------------------

    // Lyric text: no '[' / ']' (so it can never be mistaken for a timestamp/metadata tag)
    // and no CR/LF (line breaks are structural).
    private const string TextAlphabet =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .,!'";

    // Metadata value alphabet: exclude brackets so the value cannot close/open a tag.
    private const string MetaValueAlphabet =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 ";

    // Podcast-id suffix alphabet (no brackets/whitespace).
    private const string IdAlphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    // Same as IdAlphabet but WITHOUT capital 'L' — used to build invalid podcast suffixes.
    private const string IdAlphabetNoCapitalL =
        "ABCDEFGHIJKMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    // =====================================================================================
    // Property 21 — Round-trip LRC parsing (Req 17.5 / Task 6.3)
    // =====================================================================================

    /// <summary>A single LRC timestamp tag, with no / 2-digit / 3-digit fraction.</summary>
    private static readonly Gen<string> TimestampTag =
        from minutes in Gen.Int[0, 99]
        from seconds in Gen.Int[0, 59]
        from fraction in Gen.OneOf(
            Gen.Const(string.Empty),
            Gen.Int[0, 99].Select(x => "." + x.ToString("D2", CultureInfo.InvariantCulture)),
            Gen.Int[0, 999].Select(x => "." + x.ToString("D3", CultureInfo.InvariantCulture)))
        select string.Create(
            CultureInfo.InvariantCulture,
            $"[{minutes:D2}:{seconds:D2}{fraction}]");

    private static readonly Gen<string> LyricText =
        Gen.Char[TextAlphabet].Array[0, 24].Select(chars => new string(chars));

    // A timed line: 1-3 timestamps (the "double timestamp" edge) followed by free text.
    private static readonly Gen<string> TimedLine =
        from tags in TimestampTag.Array[1, 3]
        from text in LyricText
        select string.Concat(tags) + text;

    // A pure metadata line ([ar:], [ti:], [al:], [by:]) — dropped by Parse.
    private static readonly Gen<string> MetadataLine =
        from key in Gen.OneOfConst("ar", "ti", "al", "by")
        from value in Gen.Char[MetaValueAlphabet].Array[0, 16].Select(c => new string(c))
        select $"[{key}:{value}]";

    // An [offset:N] line — folded into subsequent times by Parse, never re-emitted by Format.
    private static readonly Gen<string> OffsetLine =
        Gen.Int[-500, 500].Select(n => string.Create(CultureInfo.InvariantCulture, $"[offset:{n}]"));

    // Any single physical line of the payload. Timed lines are weighted higher so that
    // most generated payloads carry real timed content (plus blank/metadata/offset edges).
    private static readonly Gen<string> PayloadLine = Gen.OneOf(
        TimedLine,
        TimedLine,
        TimedLine,
        Gen.Const(string.Empty), // blank line edge
        MetadataLine,
        OffsetLine);

    private static readonly Gen<string> LrcPayload =
        PayloadLine.Array[0, 16].Select(lines => string.Join("\n", lines));

    // Feature: kaset-winui3, Property 21: Round-trip parsing LRC
    // Validates: Requirements 17.5
    [Fact]
    public void Property21_Lrc_parse_format_parse_roundtrips()
    {
        // For any valid LRC payload: Parse → Format → Parse yields SyncedLyrics equivalent to
        // the first parse (identical per-line TimeInMs and Text, in the same order). Format
        // prints full-millisecond timestamps, so the second parse recovers the exact times.
        LrcPayload.Sample(
            payload =>
            {
                var first = LrcParser.Parse(payload);
                var roundTripped = LrcParser.Parse(LrcParser.Format(first));

                Assert.Equal(first.Lines.Count, roundTripped.Lines.Count);
                for (var i = 0; i < first.Lines.Count; i++)
                {
                    Assert.Equal(first.Lines[i].TimeInMs, roundTripped.Lines[i].TimeInMs);
                    Assert.Equal(first.Lines[i].Text, roundTripped.Lines[i].Text);
                }
            },
            iter: 100);
    }

    // =====================================================================================
    // Property 22 — Synced highlighting is monotonic in time (Req 17.2 / Task 6.4)
    // =====================================================================================

    // Lyrics sorted ascending by time (the post-condition LrcParser.Parse guarantees).
    private static readonly Gen<SyncedLyrics> SortedLyrics =
        Gen.Int[0, 600_000].Array[0, 30].Select(times =>
        {
            Array.Sort(times);
            var lines = times
                .Select(t => new SyncedLyricLine { TimeInMs = t, Text = "line" })
                .ToList();
            return new SyncedLyrics(lines, "test");
        });

    // A non-decreasing sequence of playback positions (ms).
    private static readonly Gen<long[]> AscendingPositions =
        Gen.Long[-1_000L, 700_000L].Array[1, 30].Select(positions =>
        {
            Array.Sort(positions);
            return positions;
        });

    // Feature: kaset-winui3, Property 22: Penyorotan lirik synced monoton terhadap waktu
    // Validates: Requirements 17.2
    [Fact]
    public void Property22_CurrentLineIndex_is_monotonic_and_future_lines_are_upcoming()
    {
        // For any sorted lyrics and ascending positions: CurrentLineIndex never decreases, and
        // every line whose TimeInMs is strictly greater than the position sits at an index
        // beyond the current line (i.e. is "upcoming").
        Gen.Select(SortedLyrics, AscendingPositions).Sample(
            t =>
            {
                var (lyrics, positions) = t;

                var previousIndex = int.MinValue;
                foreach (var position in positions)
                {
                    var current = SyncedLyricsNavigator.CurrentLineIndex(lyrics, position);

                    // Monotonic: never decreases as the position advances.
                    Assert.True(current >= previousIndex);
                    previousIndex = current;

                    // Upcoming: any line later than the position is at an index past current.
                    for (var i = 0; i < lyrics.Lines.Count; i++)
                    {
                        if (lyrics.Lines[i].TimeInMs > position)
                        {
                            Assert.True(i > current);
                        }
                    }
                }
            },
            iter: 100);
    }

    // =====================================================================================
    // Property 19 — Audio quality mapping is total (Req 7.1 / 7.3 / Task 8.4)
    // =====================================================================================

    private static readonly Gen<AudioQuality> AudioQualities = Gen.OneOfConst(
        AudioQuality.Low,
        AudioQuality.Medium,
        AudioQuality.High);

    // Feature: kaset-winui3, Property 19: Pemetaan kualitas audio bersifat total
    // Validates: Requirements 7.1, 7.3
    [Fact]
    public void Property19_AudioQualityMap_is_total_and_correct()
    {
        // For any AudioQuality value: ToYouTubeValue returns the correct, non-empty string
        // (Low→small, Medium→medium, High→highres) and never throws.
        AudioQualities.Sample(
            quality =>
            {
                var value = AudioQualityMap.ToYouTubeValue(quality);

                Assert.False(string.IsNullOrEmpty(value));

                var expected = quality switch
                {
                    AudioQuality.Low => "small",
                    AudioQuality.Medium => "medium",
                    AudioQuality.High => "highres",
                    _ => throw new InvalidOperationException("Unexpected AudioQuality value."),
                };
                Assert.Equal(expected, value);
            },
            iter: 100);
    }

    // =====================================================================================
    // Property 28 — Playlist mutation id strips VL prefix (Req 13.3 / Task 7.6)
    // =====================================================================================

    private static readonly Gen<(string Id, bool Prefixed)> PlaylistIds =
        from baseId in PbtGenerators.ShortToken.Where(s => !s.StartsWith("VL", StringComparison.Ordinal))
        from prefixed in Gen.Bool
        select (prefixed ? YTMusicIds.PlaylistBrowsePrefix + baseId : baseId, prefixed);

    // Feature: kaset-winui3, Property 28: Pembersihan id mutasi playlist
    // Validates: Requirements 13.3
    [Fact]
    public void Property28_StripVlPrefix_removes_exactly_one_leading_VL()
    {
        // For any playlist id (with or without the VL prefix): StripVlPrefix removes exactly one
        // leading VL when present and passes the id through unchanged otherwise. Because the base
        // id never starts with VL, the result never starts with VL.
        PlaylistIds.Sample(
            t =>
            {
                var (id, prefixed) = t;

                var result = YTMusicIds.StripVlPrefix(id);

                var expected = id.StartsWith("VL", StringComparison.Ordinal) ? id["VL".Length..] : id;
                Assert.Equal(expected, result);

                // Result carries no leading VL (base never starts with VL; at most one removed).
                Assert.False(result.StartsWith("VL", StringComparison.Ordinal));

                if (prefixed)
                {
                    // Exactly one VL removed → the original base id remains.
                    Assert.Equal(id["VL".Length..], result);
                }
                else
                {
                    // Passthrough for ids without the prefix.
                    Assert.Equal(id, result);
                }
            },
            iter: 100);
    }

    // =====================================================================================
    // Property 36 — Podcast id conversion MPSPP→P (Req 27.4 / Task 7.7)
    // =====================================================================================

    // Valid: MPSPP + "L" + rest → expected "P" + "L" + rest (i.e. PL...).
    private static readonly Gen<(string Id, string? Expected)> ValidPodcastIds =
        Gen.Char[IdAlphabet].Array[0, 20].Select(chars =>
        {
            var suffix = "L" + new string(chars);
            return (YTMusicIds.PodcastShowPrefix + suffix, (string?)("P" + suffix));
        });

    // Invalid: exactly the prefix, so the suffix is empty.
    private static readonly Gen<(string Id, string? Expected)> EmptySuffixIds =
        Gen.Const((YTMusicIds.PodcastShowPrefix, (string?)null));

    // Invalid: no MPSPP prefix ("X" guarantees the id never starts with MPSPP).
    private static readonly Gen<(string Id, string? Expected)> MissingPrefixIds =
        PbtGenerators.ShortToken.Select(s => ("X" + s, (string?)null));

    // Invalid: MPSPP + non-'L' first char + rest.
    private static readonly Gen<(string Id, string? Expected)> SuffixNotLIds =
        from first in Gen.Char[IdAlphabetNoCapitalL]
        from rest in Gen.Char[IdAlphabet].Array[0, 20]
        select (YTMusicIds.PodcastShowPrefix + first + new string(rest), (string?)null);

    private static readonly Gen<(string Id, string? Expected)> PodcastIds = Gen.OneOf(
        ValidPodcastIds,
        ValidPodcastIds,
        EmptySuffixIds,
        MissingPrefixIds,
        SuffixNotLIds);

    // Feature: kaset-winui3, Property 36: Konversi ID podcast MPSPP→P
    // Validates: Requirements 27.4
    [Fact]
    public void Property36_ConvertPodcastShowId_maps_valid_and_rejects_invalid()
    {
        // For any valid MPSPP-prefixed show id: conversion yields "P" + suffix (PL..., never a
        // spurious double-L). Ids without the prefix, with an empty suffix, or whose suffix does
        // not start with 'L' throw KasetError(ParseError).
        PodcastIds.Sample(
            t =>
            {
                var (id, expected) = t;

                if (expected is null)
                {
                    var error = Assert.Throws<KasetError>(
                        () => YTMusicIds.ConvertPodcastShowIdToPlaylistId(id));
                    Assert.Equal(KasetErrorKind.ParseError, error.Kind);
                }
                else
                {
                    var result = YTMusicIds.ConvertPodcastShowIdToPlaylistId(id);

                    Assert.Equal(expected, result);
                    Assert.StartsWith("PL", result, StringComparison.Ordinal);

                    // No spurious double-L: PLL only if the suffix genuinely began with "LL".
                    if (!id.StartsWith(YTMusicIds.PodcastShowPrefix + "LL", StringComparison.Ordinal))
                    {
                        Assert.False(result.StartsWith("PLL", StringComparison.Ordinal));
                    }
                }
            },
            iter: 100);
    }

    // =====================================================================================
    // Property 37 — Video availability from music video type (Req 26.1)
    // =====================================================================================

    private static readonly Gen<MusicVideoType> MusicVideoTypes = Gen.OneOfConst(
        MusicVideoType.Omv,
        MusicVideoType.Atv,
        MusicVideoType.Ugc,
        MusicVideoType.PodcastEpisode,
        MusicVideoType.Unknown);

    // Feature: kaset-winui3, Property 37: Deteksi ketersediaan video dari tipe video musik
    // Validates: Requirements 26.1
    [Fact]
    public void Property37_HasVideoContent_true_iff_Omv()
    {
        // For any MusicVideoType: HasVideoContent is true if and only if the type is Omv.
        MusicVideoTypes.Sample(
            type => Assert.Equal(type == MusicVideoType.Omv, type.HasVideoContent()),
            iter: 100);
    }
}
