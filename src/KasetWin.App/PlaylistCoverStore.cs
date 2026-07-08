using System;
using System.Collections.Generic;
using System.Text.Json;

namespace KasetWin.App;

/// <summary>
/// Local custom playlist covers. YouTube Music has no API to upload a playlist cover, so a chosen
/// image is copied into the app's local folder and remembered per playlist id; surfaces that render
/// the playlist prefer this override when present.
/// </summary>
public static class PlaylistCoverStore
{
    private const string Key = "PlaylistCovers";

    private static Dictionary<string, string> Load()
    {
        try
        {
            var raw = Windows.Storage.ApplicationData.Current.LocalSettings.Values[Key] as string;
            return string.IsNullOrEmpty(raw)
                ? []
                : JsonSerializer.Deserialize<Dictionary<string, string>>(raw) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static void Save(Dictionary<string, string> map)
    {
        try
        {
            Windows.Storage.ApplicationData.Current.LocalSettings.Values[Key] = JsonSerializer.Serialize(map);
        }
        catch (Exception)
        {
            // Covers are a local convenience; persistence failures are ignored.
        }
    }

    /// <summary>The stored local cover path for <paramref name="playlistId"/>, or null.</summary>
    public static string? Get(string? playlistId)
    {
        if (string.IsNullOrEmpty(playlistId))
        {
            return null;
        }

        var map = Load();
        return map.TryGetValue(Normalize(playlistId), out var path) && System.IO.File.Exists(path) ? path : null;
    }

    /// <summary>Copies <paramref name="sourcePath"/> into local storage as the playlist's cover.</summary>
    public static string? Set(string playlistId, string sourcePath)
    {
        try
        {
            var folder = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "covers");
            System.IO.Directory.CreateDirectory(folder);
            var target = System.IO.Path.Combine(folder, Normalize(playlistId) + System.IO.Path.GetExtension(sourcePath));
            System.IO.File.Copy(sourcePath, target, overwrite: true);

            var map = Load();
            map[Normalize(playlistId)] = target;
            Save(map);
            return target;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Normalize(string id) => id.StartsWith("VL", StringComparison.Ordinal) ? id[2..] : id;
}
