using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Debug;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The recorder's row of the game's own version overlay.
///
/// In a run the client keeps a small column at the top right - <c>[v0.111.0]
/// (2026.08.14)</c>, the seed, <c>MODDED (1)</c> - filled by
/// <c>NDebugInfoLabelManager.UpdateText</c>. It is a <c>VBoxContainer</c> of three
/// right-aligned labels sharing one font and one size, so one more label added after
/// MODDED is laid out under it by the game's own container, in the game's own font,
/// and is the fourth row of that column rather than a thing of this mod's hung beside
/// it. Every word and every state is <see cref="RecorderPresence"/>'s; this draws.
///
/// <para>It follows the MODDED row rather than the container. The hotkey that hides the
/// overlay toggles each label rather than the box they sit in, so a row that read its
/// own visibility off its parent would stay up after the player asked for nothing on
/// screen. And the facts under it change while the overlay stands: the recorder
/// attaches after the run's interface is built, a continued run attaches after its
/// load, and a watch can break on any decision. So the row is re-derived every frame,
/// the way the transport re-derives on every fact that can change under it, and is
/// disconnected when it leaves the tree.</para>
///
/// <para>Whether this mod may draw at all is the shell's, asked here the way
/// <see cref="RunHistoryPlateHost.PlateFor"/> asks it: a no hides the row and says
/// nothing about why.</para>
/// </summary>
internal static class RecorderPresenceRow
{
    /// <summary>The row's node name, so the fill running twice finds the one it
    /// already added rather than adding a second.</summary>
    internal const string RowName = "RunmobileRecording";

    private static readonly StringName FontEntry = "font";

    private static readonly StringName FontSizeEntry = "font_size";

    private static readonly StringName FontColorEntry = "font_color";

    private static readonly StringName LabelType = "Label";

    /// <summary>The eligibility screen's warning hue, so the stopped row is drawn in
    /// the colour that screen already uses for a prerequisite the player has to see
    /// to.</summary>
    private static readonly Color WarningColor = new(ScreenMarkup.NotMetColor);

    /// <summary>
    /// The fill of the version overlay, where the row is added.
    ///
    /// A postfix on the method that writes MODDED, so it runs once the label this row
    /// follows has its text, its font and its place in the column. The main menu's
    /// overlay is the same class laid out differently and has no run to record, so it is
    /// left alone.
    /// </summary>
    [HarmonyPatch(typeof(NDebugInfoLabelManager))]
    internal static class VersionOverlay
    {
        [HarmonyPostfix]
        [HarmonyPatch("UpdateText")]
        internal static void AddRow(NDebugInfoLabelManager __instance, MegaLabel ____moddedWarning)
        {
            try
            {
                if (__instance.isMainMenu) return;
                Attach(____moddedWarning);
            }
            catch (Exception ex)
            {
                // The game's own overlay is not ours to break. A row that never
                // appeared is a bug report; an overlay that failed to fill is a broken
                // game.
                Log.Error(
                    $"[{RunmobileMod.ModId}] could not add the recording row to the version overlay: " +
                    $"{ex.GetType().Name}: {ex.Message}", 2);
            }
        }
    }

    /// <summary>
    /// Adds the row under the MODDED label, once, and keeps it derived.
    ///
    /// The row takes the MODDED label's font, size and alignment at the moment it is
    /// added - after that label's own <c>_Ready</c>, so a locale font substitution the
    /// game applied there is what gets copied. It takes no colour of its own: it is a
    /// child of the same container and reads the same theme, which is what "the
    /// overlay's own colour" means. It does not copy the MODDED label's modulate, which
    /// the game turns red to say a mod failed to load; that is a claim about a mod and
    /// not about this recording.
    /// </summary>
    /// <returns>The row, or null where the MODDED label has no parent to add one
    /// to.</returns>
    internal static Label? Attach(Label moddedRow)
    {
        if (moddedRow.GetParent() is not { } column) return null;
        if (column.GetNodeOrNull<Label>(RowName) is { } existing) return existing;

        var row = new Label
        {
            Name = RowName,
            Visible = false,
            HorizontalAlignment = moddedRow.HorizontalAlignment,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddThemeFontOverride(FontEntry, moddedRow.GetThemeFont(FontEntry, LabelType));
        row.AddThemeFontSizeOverride(FontSizeEntry, moddedRow.GetThemeFontSize(FontSizeEntry, LabelType));

        column.AddChild(row);
        column.MoveChild(row, moddedRow.GetIndex() + 1);
        Refresh(row, moddedRow);

        if (row.GetTree() is { } tree)
        {
            // Captures Godot's own types and nothing of a sibling assembly's: a closure is
            // a class whose fields are what it captured, and the game enumerates this
            // assembly's types before the siblings can be resolved.
            Action tick = () => Refresh(row, moddedRow);
            tree.ProcessFrame += tick;
            row.TreeExiting += () => tree.ProcessFrame -= tick;
        }

        return row;
    }

    /// <summary>Reads the facts and applies what they derive to.</summary>
    private static void Refresh(Label row, Label moddedRow)
    {
        try
        {
            Apply(row, moddedRow, RecorderPresence.For(Facts()), RunmobileMod.MayDraw);
        }
        catch (Exception ex)
        {
            row.Visible = false;
            Log.Error(
                $"[{RunmobileMod.ModId}] could not derive the recording row: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>Exactly the two facts the derivation is over.</summary>
    private static RecorderFacts Facts()
    {
        var recorder = RunRecorder.Active;
        return new RecorderFacts(recorder is not null, recorder?.Capture.State);
    }

    /// <summary>
    /// Puts one derived presence on the row.
    ///
    /// Visible only where the derivation drew it, the shell allows it and the MODDED row
    /// it follows is itself visible - the last is how hiding the overlay hides this. The
    /// warning tone is a colour override; the overlay's own is the absence of one.
    /// </summary>
    internal static void Apply(Label row, Label moddedRow, RecorderPresence presence, bool mayDraw)
    {
        row.Text = presence.Text;
        row.Visible = mayDraw && presence.Row.Presence == Presence.Drawn && moddedRow.Visible;

        if (presence.Tone == RecorderRowTone.Warning) row.AddThemeColorOverride(FontColorEntry, WarningColor);
        else row.RemoveThemeColorOverride(FontColorEntry);
    }
}
