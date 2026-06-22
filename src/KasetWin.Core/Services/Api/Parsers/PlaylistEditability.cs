using System.Text.Json.Nodes;

namespace KasetWin.Core.Services.Api.Parsers;

/// <summary>
/// Pure, dependency-free detection of playlist ownership / delete affordances from a
/// YouTube Music playlist response tree. Mirrors the macOS <c>PlaylistEditability</c>
/// counterpart and backs the delete-affordance guarantee (Property 27, Req 14.3).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IsOwnedByUser"/> returns <see langword="true"/> only when the payload exposes
/// an affordance that is present exclusively for playlists the signed-in user can delete:
/// a <c>deletePlaylistEndpoint</c> command, the editable header renderer
/// (<c>musicEditablePlaylistDetailHeaderRenderer</c>), or a <c>playlist/delete</c> command
/// string. Unknown ownership is treated as not owned (<see langword="false"/>), so a delete
/// affordance is never shown speculatively.
/// </para>
/// <para>
/// The method is <c>static</c>, side-effect free and deterministic, and lives in
/// <c>KasetWin.Core</c> with no WinUI/WinRT dependency.
/// </para>
/// </remarks>
public static class PlaylistEditability
{
    /// <summary>
    /// Whether the response tree indicates the signed-in user owns (and can delete) the
    /// playlist. See the type remarks for the exact affordances inspected.
    /// </summary>
    /// <param name="node">The playlist response subtree. <c>null</c> yields <see langword="false"/>.</param>
    public static bool IsOwnedByUser(JsonNode? node)
    {
        if (node is null)
        {
            return false;
        }

        return ResponseTreeSearch.ContainsKey(node, "deletePlaylistEndpoint")
            || ResponseTreeSearch.ContainsKey(node, "musicEditablePlaylistDetailHeaderRenderer")
            || ResponseTreeSearch.ContainsText(node, "playlist/delete");
    }
}
