using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Puts Runmobile on the game's own main menu, under Compendium, as the library's one
/// entrance that exists before any progression does.
///
/// <para><b>Why the Compendium card was not enough.</b> The retail client sends a player
/// who has finished no run from Singleplayer straight to character select, and
/// <c>SaveManager.IsCompendiumAvailable</c> is false for them, so the game's Compendium
/// button is not on the menu and is not in the first run's pause menu either. Both of the
/// library's other ways in hang off surfaces that player never sees. This row is a
/// different navigation question - "where are the runs?" rather than "what can I do with
/// the run I am looking at?" - and it opens the same
/// <see cref="RunBrowserScreen"/>. There is no second library and no second playback
/// path.</para>
///
/// <para><b>Whether it is there is not this class's decision.</b>
/// <see cref="MainMenuRow.ShownWhen"/> owns the rule, the settings row states it and
/// moves it, and this reads the same answer. What is decided here is only how a row is
/// built out of the game's own furniture: the button is a duplicate of the Compendium
/// button rather than a control assembled from parts, so its font, colours, reticle
/// animation, hover tween and disabled state are MegaCrit's.</para>
///
/// Three hooks, and each is the honest one for its question.
/// <c>_Ready</c> is where the game builds its buttons and wires their focus behaviour, so
/// it is where a ninth one has to be added and joined to that wiring. <c>RefreshButtons</c>
/// is where the game decides per visit what is visible and enabled - it is called from
/// <c>_Ready</c> and again after a run is abandoned - so visibility is decided there
/// rather than once. <c>SingleplayerButtonPressed</c> is not patched at all: the row does
/// not touch the game's own routes.
/// </summary>
[HarmonyPatch(typeof(NMainMenu))]
internal static class MainMenuLibraryRow
{
    /// <summary>The button this one is duplicated from, and the one whose absence would
    /// mean this build's main menu is not the one this was written against. The game's
    /// own <c>_Ready</c> resolves it by this path.</summary>
    private const string SourceButtonPath = "MainMenuTextButtons/CompendiumButton";

    /// <summary>The name given to our node, so a second pass over the same menu sees its
    /// own work rather than adding another button.</summary>
    internal const string NodeName = "RunmobileLibraryMenuButton";

    /// <summary>The game's own reticle handlers, which every main-menu button is
    /// connected to in <c>ConnectMainMenuTextButtonFocusLogic</c>. That runs inside
    /// <c>_Ready</c>, before this postfix, so a button added afterwards is connected here
    /// instead - by name through the menu's own Godot dispatch, because both handlers are
    /// private.</summary>
    private const string FocusedHandler = "MainMenuButtonFocused";

    private const string UnfocusedHandler = "MainMenuButtonUnfocused";

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NMainMenu._Ready))]
    internal static void AddButton(NMainMenu __instance)
    {
        try
        {
            if (Existing(__instance) is not null) return;

            // The first moment there is demonstrably a running game to read, and the
            // moment retention is applied for this profile. Mod loading is neither: it
            // runs before the game has a model database at all.
            if (!RunmobileMod.EnsureAdopted()) return;

            var source = __instance.GetNodeOrNull<NMainMenuTextButton>(SourceButtonPath);
            if (source is null)
            {
                Log.Warn(
                    $"[{RunmobileMod.ModId}] this build's main menu has no '{SourceButtonPath}' to model a " +
                    "Runmobile row on; not adding one.", 2);
                return;
            }

            const int duplicateFlags =
                (int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts |
                      Node.DuplicateFlags.UseInstantiation);
            if (source.Duplicate(duplicateFlags) is not NMainMenuTextButton button)
            {
                Log.Warn(
                    $"[{RunmobileMod.ModId}] the main menu button could not be duplicated; not adding a " +
                    "Runmobile row.", 2);
                return;
            }

            Install(__instance, source, button);
            SetVisibility(__instance);
        }
        catch (Exception ex)
        {
            // The player's main menu is not ours to break. A row that failed to appear
            // is a bug report; a menu that failed to open is a broken game.
            Log.Error(
                $"[{RunmobileMod.ModId}] could not add the Runmobile row: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Decides, every time the game refreshes its own buttons, whether the row belongs on
    /// this menu and whether it may be pressed.
    ///
    /// Two separate questions and both are somebody else's. Whether it is drawn at all is
    /// the shell's permission first - a multiplayer session gets nothing, not even a
    /// greyed row - and then <see cref="MainMenuRow.ShownWhen"/>. Whether it is pressable
    /// mirrors the Compendium button, because the game disables its own destinations
    /// while an undiscovered epoch is waiting and a mod row that stayed live through that
    /// would be a way around a gate the game put up.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NMainMenu.RefreshButtons))]
    internal static void SetVisibility(NMainMenu __instance)
    {
        try
        {
            if (Existing(__instance) is not { } button) return;

            button.Visible = RunmobileMod.MayDraw && Shown();
            if (__instance.GetNodeOrNull<NMainMenuTextButton>(SourceButtonPath) is { } compendium)
            {
                button.SetEnabled(compendium.IsEnabled);
            }
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not decide whether the Runmobile row belongs on this menu: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Whether the player's settings and this profile's history put the row on the menu.
    ///
    /// A settings file this build cannot read says nothing, which is what the
    /// <c>bool?</c> already means, so such a player gets the run-count default rather
    /// than losing the library's only entrance to a file fault.
    /// </summary>
    internal static bool Shown() =>
        MainMenuRow.ShownWhen(RunmobileSettings.Read().ShowMainMenuRow, RunsFinished.Any());

    /// <summary>
    /// Adds the row, labels it, places it under Compendium and joins it to the menu's
    /// focus wiring - and leaves the menu exactly as it found it if any of that fails.
    ///
    /// The rollback is the Compendium card's, for the same reason: half a row on a
    /// player's main menu - added, unlabelled, unreachable on a controller - is worse
    /// than no row, and it is the failure nobody sees in a test.
    /// </summary>
    internal static void Install(NMainMenu menu, NMainMenuTextButton source, NMainMenuTextButton button)
    {
        var row = source.GetParent()
            ?? throw new InvalidOperationException(
                "This build's main-menu buttons are not in a row, so there is nowhere to add one beside them.");

        var focus = FocusSnapshot.Capture(source);
        var added = false;
        try
        {
            button.Name = NodeName;

            // Hidden until something has decided it belongs here. The duplicate carries
            // the Compendium button's own visibility, which is the answer to a different
            // question, and a failure part way through this would otherwise leave that
            // answer on screen as if it were ours.
            button.Visible = false;
            row.AddChild(button);
            added = true;
            SetLabel(button, LibraryCopy.MainMenuRow);
            Place(row, source, button);
            JoinFocusChain(menu, source, button);

            var error = button.Connect(
                NClickableControl.SignalName.Released,
                Callable.From<NButton>(_ => RunBrowserScreen.Open()));
            if (error != Error.Ok)
            {
                throw new InvalidOperationException($"Connecting the Runmobile row failed with {error}.");
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
    /// Replaces the duplicate's label with this mod's own name.
    ///
    /// Set on the label node rather than through <c>SetLocalization</c>, which reads the
    /// game's own localization tables: Runmobile contributes none, so asking for a key
    /// that does not exist would put a key on the player's main menu. The duplicate's
    /// own <c>LocString</c> is cleared with it, because the button re-reads that on every
    /// translation change and would otherwise put "COMPENDIUM" back when the player
    /// changes language.
    /// </summary>
    internal static void SetLabel(NMainMenuTextButton button, string text)
    {
        ButtonField("_locString").SetValue(button, null);
        var label = button.label
            ?? throw new InvalidOperationException(
                "NMainMenuTextButton has no label on this build, so the row's wording cannot be set.");
        label.SetTextAutoSize(text);

        // The button scales its label about that label's own pivot when hovered, and the
        // game sets the pivot a frame after the text, once Godot has laid the label out.
        // Setting it now would centre the animation on the width of the word this button
        // was duplicated from.
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(label)) label.PivotOffset = label.Size * 0.5f;
        }).CallDeferred();
    }

    /// <summary>The row this menu already has, wherever in its tree it sits.</summary>
    private static NMainMenuTextButton? Existing(Node menu) =>
        menu.FindChild(NodeName, recursive: true, owned: false) as NMainMenuTextButton;

    private static FieldInfo ButtonField(string name) =>
        typeof(NMainMenuTextButton).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            $"NMainMenuTextButton has no '{name}' on this build, so the row cannot be relabelled.");

    /// <summary>
    /// Puts the row directly under Compendium. Child order is what places it, because the
    /// menu's buttons are laid out by their parent - which is how hiding Continue,
    /// Compendium or Timeline closes the gap rather than leaving one. The explicit
    /// position is the fallback for a parent that is not a container, where a duplicate
    /// would otherwise sit exactly on top of the button it was copied from.
    /// </summary>
    private static void Place(Node row, Control source, Control button)
    {
        row.MoveChild(button, source.GetIndex() + 1);
        if (row is Container) return;

        // Below the source by its own height, which is the spacing an authored column
        // uses between two adjacent buttons.
        button.Position = source.Position + new Vector2(0f, source.Size.Y);
    }

    /// <summary>
    /// Connects the row to the reticle the game slides beside a focused button, and
    /// inserts it into the explicit focus chain where this build has one.
    ///
    /// <c>ConnectMainMenuTextButtonFocusLogic</c> walks the button column inside
    /// <c>_Ready</c>, before this row exists, so the two signals are connected here by
    /// hand. They are dispatched through the menu's own Godot method table because both
    /// handlers are private; the focused one is deferred exactly as the game defers it,
    /// because the reticle is positioned from the button's global position and that is
    /// not settled in the frame focus moves.
    ///
    /// The neighbour rewiring is conditional because this build's column may not use one:
    /// where <c>FocusNeighborBottom</c> is empty Godot finds the nearest control by
    /// geometry, which already includes a new sibling, and writing a path in would
    /// replace a working answer with a brittle one.
    /// </summary>
    private static void JoinFocusChain(NMainMenu menu, NMainMenuTextButton source, NMainMenuTextButton button)
    {
        button.Connect(
            NClickableControl.SignalName.Focused,
            Callable.From<NMainMenuTextButton>(focused => Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(menu)) menu.Call(FocusedHandler, focused);
            }).CallDeferred()));
        button.Connect(
            NClickableControl.SignalName.Unfocused,
            Callable.From<NMainMenuTextButton>(unfocused =>
            {
                if (GodotObject.IsInstanceValid(menu)) menu.Call(UnfocusedHandler, unfocused);
            }));

        if (source.FocusNeighborBottom.IsEmpty) return;

        var below = source.GetNodeOrNull<Control>(source.FocusNeighborBottom);
        button.FocusNeighborTop = source.GetPath();
        button.FocusNeighborBottom = source.FocusNeighborBottom;
        source.FocusNeighborBottom = button.GetPath();
        if (below is not null && !below.FocusNeighborTop.IsEmpty) below.FocusNeighborTop = button.GetPath();
    }

    /// <summary>What the menu's focus chain said before the row was inserted into it, so
    /// a failure half way through leaves a column that still navigates.</summary>
    private sealed record FocusSnapshot(
        Control Source, NodePath SourceBottom, Control? Below, NodePath BelowTop)
    {
        internal static FocusSnapshot Capture(NMainMenuTextButton source)
        {
            var below = source.FocusNeighborBottom.IsEmpty
                ? null
                : source.GetNodeOrNull<Control>(source.FocusNeighborBottom);
            return new FocusSnapshot(
                source, source.FocusNeighborBottom, below, below?.FocusNeighborTop ?? new NodePath());
        }

        internal void Restore()
        {
            Source.FocusNeighborBottom = SourceBottom;
            if (Below is not null) Below.FocusNeighborTop = BelowTop;
        }
    }
}
