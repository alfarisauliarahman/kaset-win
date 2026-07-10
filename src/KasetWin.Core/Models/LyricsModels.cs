using System.Text.Json.Serialization;

namespace KasetWin.Core.Models;

/// <summary>
/// A single timed word within a synced lyric line (word-level karaoke timing).
/// </summary>
public sealed record TimedWord(int TimeInMs, string Word);

/// <summary>
/// One synced lyric line with a start time, optional duration, text, and optional
/// per-word timing.
/// </summary>
public sealed record SyncedLyricLine
{
    public required int TimeInMs { get; init; }

    public int Duration { get; init; }

    public required string Text { get; init; }

    public IReadOnlyList<TimedWord>? Words { get; init; }
}

/// <summary>
/// A full set of synced lyric lines together with their source provider.
/// </summary>
public sealed record SyncedLyrics(IReadOnlyList<SyncedLyricLine> Lines, string Source)
{
    public bool IsEmpty => Lines.Count == 0;
}

/// <summary>
/// Plain (unsynced) lyrics with an optional source provider.
/// </summary>
public sealed record PlainLyrics(string Text, string? Source);

/// <summary>
/// One selectable caption (CC) track of a video: its display name (e.g. "Indonesia",
/// "Indonesia (dibuat otomatis)"), the timedtext download URL, and whether it is
/// auto-generated (ASR) rather than creator-provided.
/// </summary>
public sealed record CaptionTrack(string Name, string BaseUrl, bool IsAsr);

/// <summary>
/// Outcome of a lyrics lookup: synced, plain, or unavailable. Modelled as a
/// discriminated union so the UI can pick the right rendering path (Req 17).
/// <para>
/// Annotated for <see cref="System.Text.Json"/> polymorphic round-trip.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Synced), "synced")]
[JsonDerivedType(typeof(Plain), "plain")]
[JsonDerivedType(typeof(Unavailable), "unavailable")]
public abstract record LyricResult
{
    public sealed record Synced(SyncedLyrics Lyrics) : LyricResult;

    public sealed record Plain(PlainLyrics Lyrics) : LyricResult;

    public sealed record Unavailable : LyricResult;
}
