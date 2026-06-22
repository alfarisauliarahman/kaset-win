using System.Diagnostics.CodeAnalysis;

namespace KasetWin.Core.Services.Activation;

/// <summary>
/// The kind of content addressed by a <c>kaset://</c> protocol URI (Req 33.1–33.4).
/// </summary>
public enum KasetUriKind
{
    /// <summary>Play a song/video by its YouTube video id (<c>kaset://play?v={videoId}</c>).</summary>
    Play,

    /// <summary>Open a playlist by its id (<c>kaset://playlist?list={id}</c>).</summary>
    Playlist,

    /// <summary>Open an album by its id (<c>kaset://album?id={id}</c>).</summary>
    Album,

    /// <summary>Open an artist page by its id (<c>kaset://artist?id={id}</c>).</summary>
    Artist,
}

/// <summary>
/// A parsed, actionable <c>kaset://</c> command: a <see cref="KasetUriKind"/> plus the non-empty
/// content id extracted from the URI's query string.
/// </summary>
/// <param name="Kind">The content kind the URI addresses.</param>
/// <param name="Id">The non-empty content id (videoId / playlistId / albumId / artistId).</param>
public sealed record KasetUriCommand(KasetUriKind Kind, string Id);

/// <summary>
/// Pure, total parser for the application's custom <c>kaset://</c> URL scheme (Req 33, Property 43).
/// This is the Windows port of the macOS <c>URLHandler.parseKasetURL</c> and mirrors its grammar
/// exactly:
/// <list type="bullet">
///   <item><description><c>kaset://play?v={videoId}</c> → <see cref="KasetUriKind.Play"/></description></item>
///   <item><description><c>kaset://playlist?list={id}</c> → <see cref="KasetUriKind.Playlist"/></description></item>
///   <item><description><c>kaset://album?id={id}</c> → <see cref="KasetUriKind.Album"/></description></item>
///   <item><description><c>kaset://artist?id={id}</c> → <see cref="KasetUriKind.Artist"/></description></item>
/// </list>
/// The action is taken from the URI <em>host</em> (case-insensitive) and the id from the relevant
/// query parameter (matched case-sensitively, like the macOS implementation). Any URI that does not
/// match this grammar — a different scheme, an unknown action, a missing/empty id, or outright
/// garbage — yields <see langword="null"/> ("ignore"), so the activation handler can drop it without
/// changing playback state (Req 33.5). The parser is <b>total</b>: it never throws on malformed input.
/// </summary>
public static class KasetUriParser
{
    /// <summary>The custom URL scheme registered for protocol activation.</summary>
    public const string Scheme = "kaset";

    /// <summary>
    /// Parses a <c>kaset://</c> URI string into a <see cref="KasetUriCommand"/>, or returns
    /// <see langword="null"/> when the URI is invalid or unknown and should be ignored (Req 33.5).
    /// Never throws.
    /// </summary>
    /// <param name="uri">The raw URI string (e.g. from protocol activation). May be null/empty.</param>
    /// <returns>The parsed command, or <see langword="null"/> to ignore the URI.</returns>
    public static KasetUriCommand? Parse(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        if (!Uri.TryCreate(uri.Trim(), UriKind.Absolute, out var parsed))
        {
            return null;
        }

        // URI schemes are case-insensitive (RFC 3986); System.Uri already lower-cases Scheme.
        if (!string.Equals(parsed.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // The action lives in the authority/host segment (mirrors the macOS host switch).
        var action = parsed.Host.ToLowerInvariant();

        return action switch
        {
            "play" => Build(KasetUriKind.Play, QueryValue(parsed, "v")),
            "playlist" => Build(KasetUriKind.Playlist, QueryValue(parsed, "list")),
            "album" => Build(KasetUriKind.Album, QueryValue(parsed, "id")),
            "artist" => Build(KasetUriKind.Artist, QueryValue(parsed, "id")),
            _ => null,
        };
    }

    /// <summary>
    /// Attempts to parse a <c>kaset://</c> URI string. Returns <see langword="true"/> with a non-null
    /// <paramref name="command"/> for a valid URI; otherwise <see langword="false"/> (ignore).
    /// </summary>
    public static bool TryParse(string? uri, [NotNullWhen(true)] out KasetUriCommand? command)
    {
        command = Parse(uri);
        return command is not null;
    }

    /// <summary>Builds a command only when the extracted id is present (non-empty), else ignore.</summary>
    private static KasetUriCommand? Build(KasetUriKind kind, string? id)
        => string.IsNullOrEmpty(id) ? null : new KasetUriCommand(kind, id);

    /// <summary>
    /// Returns the (URL-decoded) value of the first query parameter whose name matches
    /// <paramref name="name"/> case-sensitively, or <see langword="null"/> when absent. Mirrors the
    /// macOS <c>queryValue(for:in:)</c> first-match semantics without depending on System.Web.
    /// </summary>
    private static string? QueryValue(Uri uri, string name)
    {
        var query = uri.Query;
        if (query.Length <= 1)
        {
            // Empty (no query) or just "?".
            return null;
        }

        // Skip the leading '?'.
        foreach (var pair in query.AsSpan(1).ToString().Split('&'))
        {
            if (pair.Length == 0)
            {
                continue;
            }

            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var key = separator < 0 ? pair : pair[..separator];
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];

            if (Uri.UnescapeDataString(key) == name)
            {
                return Uri.UnescapeDataString(value);
            }
        }

        return null;
    }
}
