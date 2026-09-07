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
/// Puts Runmobile in the Compendium's bottom row beside Statistics and Run History.
///
/// The Compendium is where the game already keeps the things you look at rather than
/// play, which is what makes it the honest place for a library of runs. The button is
/// a duplicate of the game's own Run History button rather than a control built from
/// parts: the panel, shader, tween, focus behaviour and label font are MegaCrit's,
/// while the icon is Runmobile's packaged wagon artwork.
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
/// The game always hides Leaderboards when this submenu opens, so Runmobile occupies
/// that existing slot instead of extending the authored row past the viewport. The
/// hidden button remains untouched; only its vacant position and focus edges are used.
/// </summary>
[HarmonyPatch(typeof(NCompendiumSubmenu))]
internal static class CompendiumCard
{
    /// <summary>The button this one is duplicated from. The submenu's own
    /// <c>_Ready</c> resolves it by this name, so a build that renamed it would have
    /// stopped working before reaching us.</summary>
    private const string SourceButtonPath = "%RunHistoryButton";

    /// <summary>The button the game hides whenever this submenu opens. Its authored
    /// slot is where Runmobile belongs.</summary>
    private const string VacancyButtonPath = "%LeaderboardsButton";

    private const string RightButtonPath = "%StatisticsButton";
    private const string AboveButtonPath = "%RelicCollectionButton";
    private const string UpperLeftButtonPath = "%CardLibraryButton";

    /// <summary>The resource already packaged for the game's mod list and reused here,
    /// so the selected wagon has one owner.</summary>
    internal const string IconPath = "res://Runmobile/mod_image.png";

    /// <summary>The name given to our node, so a second pass over the same menu sees
    /// its own work rather than adding another button.</summary>
    internal const string NodeName = "RunmobileLibraryButton";

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NCompendiumSubmenu._Ready))]
    internal static void AddButton(NCompendiumSubmenu __instance)
    {
        try
        {
            if (Existing(__instance) is not null) return;

            // The first moment there is demonstrably a running game to read. Mod
            // loading is not: it runs before the game has a model database at all.
            if (!RunmobileMod.EnsureAdopted()) return;

            var source = __instance.GetNodeOrNull<NCompendiumBottomButton>(SourceButtonPath);
            var vacancy = __instance.GetNodeOrNull<NCompendiumBottomButton>(VacancyButtonPath);
            var right = __instance.GetNodeOrNull<NCompendiumBottomButton>(RightButtonPath);
            if (source is null || vacancy is null || right is null)
            {
                Log.Warn(
                    $"[{RunmobileMod.ModId}] this build's Compendium has no complete bottom row to model " +
                    "a library button on; not adding one.", 2);
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

            Install(__instance, source, vacancy, right, button);
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
    /// Decides, each time the Compendium opens, whether the shell permits this surface.
    /// The browser stays reachable with an empty local library and with automatic index
    /// fetching disabled because direct run-code lookup is still available.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NCompendiumSubmenu.OnSubmenuOpened))]
    internal static void SetVisibility(NCompendiumSubmenu __instance)
    {
        try
        {
            if (Existing(__instance) is not { } button) return;
            button.Visible = ShowsButton();
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
        NCompendiumBottomButton vacancy,
        NCompendiumBottomButton right,
        NCompendiumBottomButton button)
    {
        // The row the game's own bottom buttons sit in, not the submenu. A position is
        // only meaningful in its own parent's space, and the buttons are resolved by
        // Godot unique name, which searches the whole scene rather than the submenu's
        // direct children - so nothing says the two are the same node.
        var row = source.GetParent()
            ?? throw new InvalidOperationException(
                "This build's Compendium button is not in a row, so there is nowhere to add one beside it.");

        if (vacancy.GetParent() != row || right.GetParent() != row)
        {
            throw new InvalidOperationException(
                "This build's Compendium bottom buttons do not share one row.");
        }

        var focus = FocusSnapshot.Capture(submenu, vacancy, right);
        var added = false;
        try
        {
            button.Name = NodeName;
            row.AddChild(button);
            added = true;
            SetLabel(button, LibraryCopy.CompendiumCard);
            SetIcon(button);
            Place(row, vacancy, button);
            JoinFocusChain(submenu, right, button);

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
                focus.Restore();
            }
            finally
            {
                try
                {
                    if (added && button.GetParent() == row) row.RemoveChild(button);
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
    /// the game's own localization tables: Runmobile contributes no tables, so asking
    /// for a key that does not exist would put a key on screen. The duplicate's
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

    private static void SetIcon(NCompendiumBottomButton button)
    {
        if (!ResourceLoader.Exists(IconPath))
        {
            throw new InvalidOperationException(
                $"Runmobile's packaged Compendium icon is missing at '{IconPath}'.");
        }

        button.GetNode<TextureRect>("Icon").Texture = ResourceLoader.Load<Texture2D>(IconPath);
    }

    /// <summary>
    /// Whether the button is there at all: the shell has to allow this mod a surface,
    /// and the library has to have something behind the button.
    ///
    /// The shell is asked first and its answer is not a state to draw. A button greyed
    /// or a popup explaining itself would each be this mod speaking in a game it was
    /// told to stay out of, so silence here means the button is simply not there.
    /// </summary>
    internal static bool ShowsButton() => RunmobileMod.MayDraw;

    /// <summary>
    /// The library button this Compendium already has, wherever in the submenu's own
    /// tree it sits. Searched rather than named as a direct child, because it is added
    /// to the game's bottom row and the row is not the submenu itself on every
    /// build.</summary>
    private static Control? Existing(Node submenu) =>
        submenu.FindChild(NodeName, recursive: true, owned: false) as Control;

    private static FieldInfo ButtonField(string name) =>
        typeof(NCompendiumBottomButton).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            $"NCompendiumBottomButton has no '{name}' on this build, so the button cannot be relabelled.");

    /// <summary>
    /// Takes the authored slot of the always-hidden Leaderboards button. Child order is
    /// set as well as position so a future container row makes the same decision.
    /// </summary>
    private static void Place(Node row, Control vacancy, Control button)
    {
        row.MoveChild(button, vacancy.GetIndex() + 1);
        if (row is not Container) button.Position = vacancy.Position;
    }

    /// <summary>Joins the visible button into the game's existing controller chain.</summary>
    private static void JoinFocusChain(
        NCompendiumSubmenu submenu, Control right, Control button)
    {
        var path = button.GetPath();
        right.FocusNeighborLeft = path;
        foreach (var upperPath in new[] { UpperLeftButtonPath, AboveButtonPath })
        {
            if (submenu.GetNodeOrNull<Control>(upperPath) is { } upper)
            {
                upper.FocusNeighborBottom = path;
            }
        }

        button.FocusNeighborLeft = path;
        button.FocusNeighborRight = right.GetPath();
        button.FocusNeighborBottom = path;
        button.FocusNeighborTop = submenu.GetNodeOrNull<Control>(AboveButtonPath) is { } above
            ? above.GetPath()
            : right.GetPath();
    }

    private sealed record FocusSnapshot(
        Control Right,
        NodePath RightLeft,
        Control? UpperLeft,
        NodePath UpperLeftBottom,
        Control? Above,
        NodePath AboveBottom)
    {
        internal static FocusSnapshot Capture(
            NCompendiumSubmenu submenu, Control vacancy, Control right)
        {
            var upperLeft = submenu.GetNodeOrNull<Control>(UpperLeftButtonPath);
            var above = submenu.GetNodeOrNull<Control>(AboveButtonPath);
            return new FocusSnapshot(
                right,
                right.FocusNeighborLeft,
                upperLeft,
                upperLeft?.FocusNeighborBottom ?? vacancy.GetPath(),
                above,
                above?.FocusNeighborBottom ?? vacancy.GetPath());
        }

        internal void Restore()
        {
            Right.FocusNeighborLeft = RightLeft;
            if (UpperLeft is not null) UpperLeft.FocusNeighborBottom = UpperLeftBottom;
            if (Above is not null) Above.FocusNeighborBottom = AboveBottom;
        }
    }
}
