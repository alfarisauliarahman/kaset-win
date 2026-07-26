using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Player;

/// <summary>
/// Finds the album ("song") version of a track that is about to play as a music video, for the
/// "prefer the song version" setting.
/// </summary>
/// <remarks>
/// <para>
/// The obvious source for this mapping — the counterpart entry behind YT Music's own SONG/VIDEO
/// toggle — is simply not present in the watch-next response this client receives. That was proven
/// three ways (anonymous, consent-cookied, and from a signed-in session inside the app:
/// <c>next-probe … counterpart=False</c>), so this resolver takes the only deterministic route
/// left: the video's own metadata names its album; the album's track list names the audio
/// (<c>ATV</c>) videoIds; match by title.
/// </para>
/// <para>
/// Matching is by <em>normalized title only</em>, deliberately. Durations differ between an MV and
/// its album track (intros, outros) — that difference is the very reason the feature exists, so
/// duration is disqualified as a signal. And an ambiguous or empty match returns <c>null</c>
/// rather than a guess: playing the video the user actually picked is strictly better than
/// confidently playing the wrong song.
/// </para>
/// <para>
/// Lives in Core with both lookups as delegates, so the whole decision table is covered headless.
/// </para>
/// </remarks>
public sealed class SongVersionResolver
{
    private readonly Func<string, CancellationToken, Task<Song?>> _fetchSong;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<Song>>> _fetchAlbumTracks;

    /// <summary>
    /// Creates the resolver.
    /// </summary>
    /// <param name="fetchSong">Fetches a track's metadata (the <c>next</c> endpoint) — used for its album id.</param>
    /// <param name="fetchAlbumTracks">Fetches an album's track list by browse id.</param>
    public SongVersionResolver(
        Func<string, CancellationToken, Task<Song?>> fetchSong,
        Func<string, CancellationToken, Task<IReadOnlyList<Song>>> fetchAlbumTracks)
    {
        _fetchSong = fetchSong ?? throw new ArgumentNullException(nameof(fetchSong));
        _fetchAlbumTracks = fetchAlbumTracks ?? throw new ArgumentNullException(nameof(fetchAlbumTracks));
    }

    /// <summary>
    /// Resolves the audio version of <paramref name="video"/>, or <c>null</c> when there is none
    /// to be found with certainty. Never throws: any lookup failure means "play what was asked".
    /// </summary>
    public async Task<Song?> ResolveAsync(Song video, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(video);

        if (string.IsNullOrEmpty(video.VideoId))
        {
            return null;
        }

        try
        {
            // Round 11 taught that gating on a KNOWN video type misses the most common path: album
            // and playlist rows never carry a VideoType at all, so a video hiding behind an album
            // row sailed straight past the substitution ("played it from the album and still got
            // the video"). An unknown type is therefore resolved here, from the same (cached)
            // metadata fetch that names the album: not a video → nothing to do, and an audio track
            // costs exactly one cached lookup.
            Song? meta = null;
            if (video.VideoType is null)
            {
                meta = await _fetchSong(video.VideoId, ct).ConfigureAwait(false);
                if (meta?.VideoType is not (MusicVideoType.Omv or MusicVideoType.Ugc))
                {
                    return null;
                }
            }

            // The video's album may already be on the Song (a search "video" row often carries it);
            // otherwise one metadata fetch names it. No album — no deterministic mapping.
            string? albumId = video.Album?.Id ?? meta?.Album?.Id;
            if (string.IsNullOrEmpty(albumId))
            {
                meta ??= await _fetchSong(video.VideoId, ct).ConfigureAwait(false);
                albumId = meta?.Album?.Id;
            }

            if (string.IsNullOrEmpty(albumId))
            {
                Diag.Write($"song-version videoId={video.VideoId}: no album, keeping the video");
                return null;
            }

            IReadOnlyList<Song> tracks = await _fetchAlbumTracks(albumId, ct).ConfigureAwait(false);

            string wanted = NormalizeTitle(video.Title);
            if (wanted.Length == 0)
            {
                return null;
            }

            // Candidates must actually be a different playable id; an album row that happens to
            // reference the same videoId gains nothing and would recurse the substitution.
            var matches = tracks
                .Where(t => !string.IsNullOrEmpty(t.VideoId)
                    && !string.Equals(t.VideoId, video.VideoId, StringComparison.Ordinal)
                    && string.Equals(NormalizeTitle(t.Title), wanted, StringComparison.Ordinal))
                .ToList();

            if (matches.Count != 1)
            {
                Diag.Write($"song-version videoId={video.VideoId}: {matches.Count} title matches on album {albumId}, keeping the video");
                return null;
            }

            Song song = matches[0];
            Diag.Write($"song-version videoId={video.VideoId} -> {song.VideoId} (\"{song.Title}\" on {albumId})");

            // Keep the album attached even when the album row itself did not carry it — the whole
            // point of swapping is landing on the album version.
            return song.Album is null && video.Album is not null
                ? song with { Album = video.Album }
                : song;
        }
        catch
        {
            // Resolution is an optimization on top of playback; a failed lookup must never block
            // the video from playing.
            return null;
        }
    }

    /// <summary>
    /// Reduces a title to its comparable core: lowercased, bracketed decorations removed
    /// ("(Official Video)", "[MV]" and friends live in brackets, album rows don't), whitespace
    /// collapsed.
    /// </summary>
    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[title.Length];
        int length = 0;
        int depth = 0;
        bool lastWasSpace = true;

        foreach (char raw in title)
        {
            if (raw is '(' or '[' or '（' or '【')
            {
                depth++;
                continue;
            }

            if (raw is ')' or ']' or '）' or '】')
            {
                if (depth > 0)
                {
                    depth--;
                }

                continue;
            }

            if (depth > 0)
            {
                continue;
            }

            if (char.IsWhiteSpace(raw))
            {
                if (!lastWasSpace)
                {
                    buffer[length++] = ' ';
                    lastWasSpace = true;
                }

                continue;
            }

            buffer[length++] = char.ToLowerInvariant(raw);
            lastWasSpace = false;
        }

        return new string(buffer[..length]).TrimEnd();
    }
}
