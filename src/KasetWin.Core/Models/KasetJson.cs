using System.Text.Json;
using System.Text.Json.Serialization;

namespace KasetWin.Core.Models;

/// <summary>
/// Single source of truth for <see cref="System.Text.Json"/> serialization options
/// used across the domain models. Configured to be round-trip-safe:
/// <list type="bullet">
///   <item>enums serialize as their names (stable across reorderings of the enum);</item>
///   <item>case-insensitive property matching on read;</item>
///   <item>polymorphic records (<see cref="HomeSectionItem"/>, <see cref="LyricResult"/>)
///         round-trip via their declared <c>$type</c> discriminators;</item>
///   <item><c>null</c> values are written so optional members round-trip predictably.</item>
/// </list>
/// </summary>
public static class KasetJson
{
    /// <summary>
    /// Shared, immutable round-trip-safe options. Reuse this instance everywhere to
    /// benefit from the cached metadata resolver.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>Serialize <paramref name="value"/> using the round-trip-safe options.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Deserialize <paramref name="json"/> using the round-trip-safe options.</summary>
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
