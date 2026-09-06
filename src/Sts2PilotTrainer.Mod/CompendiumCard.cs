using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Puts a fourth button in the Compendium's bottom row, beside Leaderboards,
/// Statistics and Run History.
///
/// The Compendium is where the game already keeps the things you look at rather than
/// play, which is what makes it the honest place for a library of runs. The button is
/// a duplicate of the game's own Run History button rather than a control built from
/// parts, for the reason <see cref="ModeCard"/> gives: the panel, the shader, the
/// tween, the focus behaviour and the label font are MegaCrit's, and a hand-built
/// lookalike is a worse copy of them that also drifts.
///
/// Two hooks, and each is the honest one for its question.
/// <c>_Ready</c> is where the row is built and where every focus neighbour is assigned
/// index by index, so a button added anywhere else exists and is unreachable on a
/// controller. <c>OnSubmenuOpened</c> is where the game decides what is visible
/// per-run - it hides Leaderboards unconditionally there and decides Run History and
/// the Bestiary each time - so a button whose presence depends on what is installed
/// belongs there rather than in <c>_Ready</c>, where it would be decided once and
/// never again.
///
/// The row is not re-centred. The singleplayer row could be, because three cards were
/// centred and four still are; this row already ships with a member the game hides
/// every time it opens, so there is no centring here to read and a fourth button
/// extends the row rather than moving the game's own three.
/// </summary>
[HarmonyPatch(typeof(NCompendiumSubmenu))]
internal static class CompendiumCard
{
    /// <summary>The button this one is duplicated from. The submenu's own
    /// <c>_Ready</c> resolves it by this name, so a build that renamed it would have
    /// stopped working before reaching us.</summary>
    private const string SourceButtonPath = "%RunHistoryButton";

    /// <summary>The one before it, for measuring the row's own step.</summary>
    private const string PreviousButtonPath = "%StatisticsButton";

    /// <summary>The top-row button above ours, so a controller travelling up from it
    /// lands somewhere.</summary>
    private const string AboveButtonPath = "%BestiaryButton";

    /// <summary>The name given to our node, so a second pass over the same menu sees
    /// its own work rather than adding another button.</summary>
    internal const string NodeName = "RunmobileLibraryButton";

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NCompendiumSubmenu._Ready))]
    internal static void AddButton(NCompendiumSubmenu __instance)
    {
        try
        {
            if (__instance.GetNodeOrNull<Node>(NodeName) is not null) return;

            // The first moment there is demonstrably a running game to read. Mod
            // loading is not: it runs before the game has a model database at all.
            if (!RunmobileMod.EnsureAdopted()) return;

            var source = __instance.GetNodeOrNull<NCompendiumBottomButton>(SourceButtonPath);
            var previous = __instance.GetNodeOrNull<NCompendiumBottomButton>(PreviousButtonPath);
            if (source is null || previous is null)
            {
                Log.Warn(
                    $"[{RunmobileMod.ModId}] this build's Compendium has no '{SourceButtonPath}' to model a " +
                    "library button on; not adding one.", 2);
                return;
            }

            const int duplicateFlags =
                (int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts |
                      Node.DuplicateFlags.UseInstantiation);
            if (source.Duplicate(duplicateFlags) is not NCompendiumBottomButton button)
            {
                Log.Warn(
                    $"[{RunmobileMod.ModId}] the Compendium button could not be duplicated; not adding " +
                    "one.", 2);
                return;
            }

            Install(__instance, source, previous, button);
        }
        catch (Exception ex)
        {
            // The player's Compendium is not ours to break. A button that failed to
            // appear is a bug report; a menu that failed to open is a broken game.
            Log.Error(
                $"[{RunmobileMod.ModId}] could not add the library button: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Decides, each time the Compendium opens, whether the library has anything to
    /// show.
    ///
    /// Asked here rather than once at <c>_Ready</c> because the answer changes while
    /// the game is running: a player finishes a run, the recorder writes it, and the
    /// library has a run it did not have when the menu was built. This is the same
    /// place the game re-decides Run History and the Bestiary for the same reason.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NCompendiumSubmenu.OnSubmenuOpened))]
    internal static void SetVisibility(NCompendiumSubmenu __instance)
    {
        try
        {
            if (__instance.GetNodeOrNull<Control>(NodeName) is not { } button) return;
            button.Visible = RunLibrary.HasAnythingToShow();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not decide whether the library button belongs on this " +
                $"Compendium: {ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Adds the button, labels it, places it and joins it to the focus chain - and
    /// leaves the menu exactly as it found it if any of that fails.
    ///
    /// The rollback is not defensive habit. Half a button in the player's Compendium -
    /// added, unlabelled, unreachable - is worse than no button, and the failure that
    /// produces it is the one nobody sees in a test.
    /// </summary>
    internal static void Install(
        NCompendiumSubmenu submenu,
        NCompendiumBottomButton source,
        NCompendiumBottomButton previous,
        NCompendiumBottomButton button)
    {
        var sourcePosition = source.Position;
        var added = false;
        try
        {
            button.Name = NodeName;
            submenu.AddChild(button);
            added = true;
            SetLabel(button, LibraryCopy.CompendiumCard);
            Place(submenu, source, previous, button);
            JoinFocusChain(submenu, source, button);

            var error = button.Connect(
                NClickableControl.SignalName.Released,
                Callable.From<NButton>(_ => RunBrowserScreen.Open()));
            if (error != Error.Ok)
            {
                throw new InvalidOperationException($"Connecting the library button failed with {error}.");
            }
        }
        catch
        {
            try
            {
                source.Position = sourcePosition;
                RestoreFocusChain(source);
            }
            finally
            {
                try
                {
                    if (added && button.GetParent() == submenu) submenu.RemoveChild(button);
                }
                finally
                {
                    button.QueueFree();
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Replaces the duplicate's label with this mod's own wording.
    ///
    /// Set on the label node rather than through <c>SetLocalization</c>, which reads
    /// the game's own localization tables: a DLL-only mod contributes no tables, so
    /// asking for a key that does not exist would put a key on screen. The duplicate's
    /// localization prefix is cleared so a translation refresh leaves it unchanged.
    /// </summary>
    internal static void SetLabel(NCompendiumBottomButton button, string text)
    {
        ButtonField("_locKeyPrefix").SetValue(button, null);
        var label = ButtonField("_label").GetValue(button) as MegaLabel
            ?? button.GetNodeOrNull<MegaLabel>("Label")
            ?? throw new InvalidOperationException(
                "NCompendiumBottomButton has no label on this build, so the button's wording cannot be set.");
        label.SetTextAutoSize(text);
    }

    private static FieldInfo ButtonField(string name) =>
        typeof(NCompendiumBottomButton).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            $"NCompendiumBottomButton has no '{name}' on this build, so the button cannot be relabelled.");

    /// <summary>
    /// Where the fourth button goes.
    ///
    /// When the game lays the row out in a container, the container decides and this
    /// does nothing. When it positions the buttons itself - which is what v0.111.0 does
    /// - the step between two of the game's own buttons is measured and reused, so a
    /// build that changes the spacing changes this with it.
    /// </summary>
    private static void Place(
        NCompendiumSubmenu submenu, Control source, Control previous, Control button)
    {
        if (button.GetParent() is Container) return;

        var step = source.Position - previous.Position;
        if (step.LengthSquared() <= 0f)
        {
            throw new InvalidOperationException("The native Compendium row has no usable spacing.");
        }

        button.Position = source.Position + step;
        _ = submenu;
    }

    /// <summary>
    /// Joins the button to the row the game wired index by index.
    ///
    /// Only the two edges that reach the new button are changed: right off Run History
    /// into ours, and left, top and bottom out of ours. The game's own assignments
    /// between its own buttons are left exactly as it made them, so a build that
    /// rearranges the row rearranges it.
    /// </summary>
    private static void JoinFocusChain(
        NCompendiumSubmenu submenu, Control source, Control button)
    {
        source.FocusNeighborRight = button.GetPath();
        button.FocusNeighborLeft = source.GetPath();
        button.FocusNeighborRight = button.GetPath();
        button.FocusNeighborBottom = button.GetPath();
        button.FocusNeighborTop = submenu.GetNodeOrNull<Control>(AboveButtonPath) is { } above
            ? above.GetPath()
            : source.GetPath();
    }

    /// <summary>Puts Run History's own right-hand neighbour back where the game had
    /// it: pointing at itself, which is what the last button in a row does.</summary>
    private static void RestoreFocusChain(Control source) => source.FocusNeighborRight = source.GetPath();
}
