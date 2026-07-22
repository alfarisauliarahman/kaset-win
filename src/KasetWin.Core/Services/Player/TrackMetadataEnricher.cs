using KasetWin.Core.Models;

namespace KasetWin.Core.Services.Player;

/// <summary>
/// The metadata seam <see cref="PlayerService"/> uses to complete a now-playing track that was
/// started from a surface carrying nothing but a videoId (a <c>kaset://play?v=…</c> launch, a Home
/// card). It fetches the song's metadata and — because the watch-next response only carries the
/// album's browse id, never its name — backfills the album title so the now-playing UI can render
/// an album line at all.
/// </summary>
/// <remarks>
/// <para>
/// The album detail is only fetched when the song's album is present but unnamed, which is exactly
/// the watch-next shape; a song that already came with a titled album (an album/playlist page)
/// costs no extra request. Every failure is swallowed: enrichment is a nicety and must never
/// disturb playback.
/// </para>
/// <para>
/// Lives in Core (not in the App's composition root) so the "album with an id but no title" case is
/// covered headless. Both fetchers are delegates, so no InnerTube client is required under test.
/// </para>
/// </remarks>
public sealed class TrackMetadataEnricher
{
    private readonly Func<string, CancellationToken, Task<Song?>> _fetchSong;
    private readonly Func<string, CancellationToken, Task<string?>>? _fetchAlbumTitle;

    /// <summary>
    /// Creates the enricher.
    /// </summary>
    /// <param name="fetchSong">Fetches the song metadata for a videoId (the <c>next</c> endpoint).</param>
    /// <param name="fetchAlbumTitle">
    /// Optional: given an album browse id, returns its display title. When <c>null</c> the album
    /// title is left as it came.
    /// </param>
    public TrackMetadataEnricher(
        Func<string, CancellationToken, Task<Song?>> fetchSong,
        Func<string, CancellationToken, Task<string?>>? fetchAlbumTitle = null)
    {
        _fetchSong = fetchSong ?? throw new ArgumentNullException(nameof(fetchSong));
        _fetchAlbumTitle = fetchAlbumTitle;
    }

    /// <summary>
    /// Fetches metadata for <paramref name="videoId"/>, naming the album when it arrived unnamed.
    /// Returns <c>null</c> when nothing could be fetched.
    /// </summary>
    public async Task<Song?> FetchAsync(string videoId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoId);

        Song? song;
        try
        {
            song = await _fetchSong(videoId, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        if (song?.Album is not { } album
            || _fetchAlbumTitle is null
            || string.IsNullOrEmpty(album.Id)
            || !string.IsNullOrEmpty(album.Title))
        {
            return song;
        }

        try
        {
            string? title = await _fetchAlbumTitle(album.Id, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(title))
            {
                song = song with { Album = album with { Title = title } };
            }
        }
        catch
        {
            // An unnamed album still navigates; only the label is missing.
        }

        return song;
    }
}
