using Godot;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The game's own floor markers for the run strip.
///
/// The run-history screen draws each floor a run visited as an icon from
/// <c>images/ui/run_history/</c> with its <c>_outline</c> behind it at a quarter of
/// black - <c>NMapPointHistoryEntry</c> and its scene <c>map_point_history_entry.tscn</c>
/// are where that is settled, and the icon names come from
/// <c>ImageHelper.GetRoomIconPath</c>. The strip is the same row of floors, so it wears
/// the same art; a strip of the mod's own hollow glyphs read as a different game.
///
/// A kind the recording did not establish has no icon here on purpose: the game draws
/// an unknown room as a question mark and so does an event, and the strip must not say
/// "event" about a floor nothing was read from. That cell keeps the mod's own hollow
/// ring, which says the place exists and no more. An icon this build has not got
/// answers null and the caller draws the ring there too, which is the honest answer
/// and never the wrong picture.
/// </summary>
internal static class FloorMarkerArt
{
    private const string Directory = "res://images/ui/run_history/";

    private static readonly Dictionary<string, Texture2D?> Loaded = new(StringComparer.Ordinal);

    /// <summary>The game's own icon name for a floor's kind, or null for a kind that
    /// has none.</summary>
    internal static string? IconName(FloorKind kind) => kind switch
    {
        FloorKind.Combat => "monster",
        FloorKind.Shop => "shop",
        FloorKind.Rest => "rest_site",
        FloorKind.Event => "event",
        FloorKind.Treasure => "treasure",
        FloorKind.Unknown => null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "There is no floor marker for it."),
    };

    internal static string IconPath(string name) => $"{Directory}{name}.png";

    internal static string OutlinePath(string name) => $"{Directory}{name}_outline.png";

    /// <summary>The icon and its outline for a kind, or null where this build has no
    /// icon for it.</summary>
    internal static (Texture2D Icon, Texture2D? Outline)? Of(FloorKind kind)
    {
        if (IconName(kind) is not { } name) return null;
        if (Load(IconPath(name)) is not { } icon) return null;
        return (icon, Load(OutlinePath(name)));
    }

    private static Texture2D? Load(string path)
    {
        if (Loaded.TryGetValue(path, out var known)) return known;

        // A path this build has not got is asked once and answered null; the cell then
        // wears the mod's own glyph, which is the honest answer and never a wrong picture
        var texture = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        Loaded[path] = texture;
        return texture;
    }
}
