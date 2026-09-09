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
/// it is where a ninth one has to be added and joined to that wiring.
/// <c>OnSubmenuStackChanged</c> is where the game decides its button column is on screen
/// again, so it is where visibility is re-decided per visit; <c>RefreshButtons</c> is
/// where the enabled states this row mirrors are settled, and it is called only twice, so
/// it cannot be the per-visit hook on its own. <c>SingleplayerButtonPressed</c> is not
/// patched at all: the row does not touch the game's own routes.
///
/// <para><b>Nothing here adopts the running game until the row is pressed.</b> The main
/// menu is built one startup phase before the game has a model database, so adopting at
/// <c>_Ready</c> refuses - and the refusal is latched for the process, which would take
/// the Compendium card and the recorder down with it. <see cref="Open"/> says the whole
/// of it.</para>
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

    /// <summary>The game's own "the button column is on screen again" moment, named as a
    /// string because it is private. It is connected to the submenu stack's
    /// <c>StackModified</c>, and Settings, the Compendium and character select are all
    /// pushed onto that stack.</summary>
    private const string SubmenuStackChangedHook = "OnSubmenuStackChanged";

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NMainMenu._Ready))]
    internal static void AddButton(NMainMenu __instance)
    {
        try
        {
            if (Existing(__instance) is not null) return;

            // Nothing here adopts the running game, and that is the whole reason this
            // hook can be the main menu's own _Ready. See AdoptOnPress.
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
    /// Re-decides whether the row belongs on this menu whenever the game shows its button
    /// column again.
    ///
    /// <para><b>This is the per-visit hook, and <c>RefreshButtons</c> is not.</b> The game
    /// calls <c>RefreshButtons</c> exactly twice - at the end of its own <c>_Ready</c>,
    /// and after a run is abandoned - so a row that only followed it kept whatever it was
    /// told at construction: turning the setting off and walking back out of Settings left
    /// the row on the menu until the next launch, which is what the client showed.
    /// <c>OnSubmenuStackChanged</c> is where the game itself decides the column is
    /// visible again - it is connected to the stack's own <c>StackModified</c> - and
    /// Settings is pushed onto that stack, so leaving it fires this. It is the main
    /// menu's analogue of the <c>OnSubmenuOpened</c> the Compendium card follows for the
    /// same reason.</para>
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(SubmenuStackChangedHook)]
    internal static void SetVisibilityOnReturn(NMainMenu __instance) => SetVisibility(__instance);

    /// <summary>
    /// And again whenever the game re-decides its own buttons, which is where the enabled
    /// states this row mirrors are settled.
    ///
    /// Both hooks rather than one: this one is the only place the epoch gate and the
    /// abandoned-run refresh reach, and the one above is the only place a return from a
    /// submenu reaches. Neither covers the other, and the decision they share is
    /// idempotent.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NMainMenu.RefreshButtons))]
    internal static void SetVisibilityOnRefresh(NMainMenu __instance) => SetVisibility(__instance);

    /// <summary>
    /// Whether the row belongs on this menu and whether it may be pressed.
    ///
    /// Two separate questions and both are somebody else's. Whether it is drawn at all is
    /// the shell's permission first - a multiplayer session gets nothing, not even a
    /// greyed row - and then <see cref="MainMenuRow.ShownWhen"/>. Whether it is pressable
    /// mirrors the Compendium button, because the game disables its own destinations
    /// while an undiscovered epoch is waiting and a mod row that stayed live through that
    /// would be a way around a gate the game put up.
    /// </summary>
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
    /// Opens the library, having taken the running game at the first moment this surface
    /// can honestly ask for it.
    ///
    /// <para><b>The press, not <c>_Ready</c>.</b> The main menu is built while the game's
    /// startup phase is still <c>Essential</c> - there is no model database and no
    /// id-serialization cache yet - so <see cref="EngineHost.AdoptRunningGame"/> refuses
    /// there, and it refuses <em>once</em>: the outcome is latched, so a refusal taken at
    /// main-menu construction is the answer every later surface gets for the rest of the
    /// process. Asking there would have cost the Compendium card and the recorder as
    /// well as this row. The Compendium card can ask in its own <c>_Ready</c> because its
    /// submenu is built when a player pushes it, which is later; this row is built
    /// alongside the menu itself, so its first honest moment is the press.</para>
    ///
    /// <para>Building the row needs none of that. Whether it belongs on the menu is the
    /// settings file and <c>SaveManager.Progress</c>, both of which the game's own
    /// <c>RefreshButtons</c> reads in the same method.</para>
    ///
    /// A refusal takes the row off the menu rather than leaving a control that does
    /// nothing. It is the same outcome the Compendium card reaches by never adding
    /// itself, one moment later, because this is the moment the question could first be
    /// asked.
    /// </summary>
    private static void Open(NButton pressed)
    {
        try
        {
            if (RunmobileMod.EnsureAdopted())
            {
                RunBrowserScreen.Open();
                return;
            }

            pressed.Visible = false;
            Log.Error(
                $"[{RunmobileMod.ModId}] cannot read this game, so the Runmobile row has been taken off " +
                "the menu rather than left as a control that does nothing.", 2);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not open the library from the main menu: " +
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
                Callable.From<NButton>(pressed => Open(pressed)));
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
