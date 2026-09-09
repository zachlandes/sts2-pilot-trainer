using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// What connects the settings row to the disk it is about.
///
/// The row draws and reports; this is the thing that reads, writes and asks. Keeping
/// them apart is what lets the row be assembled and asserted on with no game, because
/// this half cannot be: the confirmation a player answers before their runs go is the
/// game's own modal popup, the same one the eligibility screen uses, and it exists only
/// inside the client.
///
/// <para><b>Nothing here decides anything.</b> Which files a removal names is
/// <see cref="RecordingRetention"/>'s and always was; what the row says is
/// <see cref="MyRunsRow"/>'s; and every reading the row shows is taken from the disk
/// after the act rather than predicted from it - so a purge that left the continuable
/// run's journal behind reads as the one run it actually left.</para>
///
/// <para><b>A failure leaves the row saying what it said.</b> The store not being ready,
/// a settings file this build will not write over, a disk that refused: each of those
/// goes to the game's log and changes nothing on screen. A receipt is a claim that
/// something happened, and a row that showed one anyway would be evidence nobody can
/// check.</para>
/// </summary>
[HarmonyPatch(typeof(NSettingsScreen))]
internal static class MyRunsSettings
{
    private const string ModdingButtonPath = "%ModdingButton";
    private const float FallbackWidth = 520f;
    private const float SectionGap = 12f;

    /// <summary>The popup scene's own name for its content, resolved the way the
    /// game's code resolves it.</summary>
    private const string VerticalPopupPath = "VerticalPopup";

    /// <summary>Label the confirm's buttons carry until this mod replaces them. Never
    /// shown: Runmobile contributes no localization table, so the game's own keys
    /// stand in and the text is set directly. Same reason as
    /// another popup surface.</summary>
    private static LocString PlaceholderConfirmLabel => new("main_menu_ui", "GENERIC_POPUP.confirm");

    private static LocString PlaceholderCancelLabel => new("main_menu_ui", "GENERIC_POPUP.cancel");

    private static MyRunsSettingsRow? _row;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NSettingsScreen._Ready))]
    internal static void AddRow(NSettingsScreen __instance)
    {
        try
        {
            if (!RunmobileMod.MayDraw ||
                __instance.FindChild(MyRunsSettingsRow.RootName, recursive: true, owned: false) is not null)
            {
                return;
            }

            var anchor = __instance.GetNodeOrNull<Control>(ModdingButtonPath);
            if (anchor is null)
            {
                Log.Warn(
                    $"[{RunmobileMod.ModId}] this build's settings screen has no '{ModdingButtonPath}' " +
                    "to host Runmobile's settings; not adding them.", 2);
                return;
            }

            Attach(anchor, GameFont.Of(__instance));
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not add Runmobile's settings: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    internal static MyRunsSettingsRow Attach(Control anchor, Font? font)
    {
        var parent = anchor.GetParent()
            ?? throw new InvalidOperationException(
                "This build's modding settings button has no parent to host Runmobile's settings.");
        var width = anchor.Size.X > 0f
            ? anchor.Size.X
            : parent is Control host && host.Size.X > 0f
                ? host.Size.X
                : FallbackWidth;
        var row = Build(width, font);
        if (parent is not Container)
        {
            row.Root.Position = anchor.Position + new Vector2(0f, anchor.Size.Y + SectionGap);
            if (parent is Control control)
            {
                control.CustomMinimumSize = new Vector2(
                    control.CustomMinimumSize.X,
                    Math.Max(control.CustomMinimumSize.Y, row.Root.Position.Y + MyRunsSettingsRow.Height));
            }
        }

        parent.AddChild(row.Root);
        return row;
    }

    /// <summary>
    /// Builds the row against what is on the disk right now, wired to act on it.
    ///
    /// The section that hosts it parents <see cref="MyRunsSettingsRow.Root"/> and gives
    /// it a width; everything else about the row is settled here.
    ///
    /// A disk that cannot be asked yet is a row, not an exception. The settings section
    /// hangs off the main menu's own modding entry point, which a player reaches before
    /// choosing a save profile, and the store throws until they have - so a build that
    /// let that out would take the screen down with it. What the row says about it is
    /// <see cref="MyRunsRow"/>'s, from a fact of its own rather than a count of zero:
    /// no runs yet and cannot tell yet are different sentences.
    /// </summary>
    internal static MyRunsSettingsRow Build(float width, Font? font)
    {
        var facts = OnDisk();
        var settings = RunmobileSettings.Read();
        _row = MyRunsSettingsRow.Build(
            MyRunsRow.For(facts), facts.Keep, settings.FetchRunIndex,
            width, font, Retain, AskToRemove, SetFetchRunIndex, SetMainMenuRow);
        return _row;
    }

    /// <summary>
    /// What the disk holds, or the fact that it could not be asked.
    ///
    /// Two failures and not one. The store refuses before the game has chosen a save
    /// profile, which is a state the player resolves by choosing one and the only cause
    /// the row may name; anything else is a fault, and a row that named the profile for
    /// it would be telling a player something untrue about a state they cannot act on.
    /// The log carries the exception either way.
    ///
    /// Neither answer says anything about <c>settings.json</c>. A read that never ran
    /// establishes nothing about that file, so the fact goes back unestablished rather
    /// than as a refusal nobody made.
    /// </summary>
    private static MyRunsFacts OnDisk()
    {
        try
        {
            return RecordingRetention.OnDisk();
        }
        catch (StoreNotReadyException ex)
        {
            return Unread(MyRunsDisk.NoSaveProfileYet, ex);
        }
        catch (Exception ex)
        {
            return Unread(MyRunsDisk.Refused, ex);
        }
    }

    private static MyRunsFacts Unread(MyRunsDisk disk, Exception ex)
    {
        Log.Error(
            $"[{RunmobileMod.ModId}] could not read what your runs take on this disk: " +
            $"{ex.GetType().Name}: {ex.Message}", 2);
        return new MyRunsFacts(
            Runs: 0,
            Bytes: 0,
            Keep: RunmobileSettings.DefaultKeepRecentRuns,
            SettingsReadable: null,
            Disk: disk,
            MainMenuRowShown: MainMenuRowShown());
    }

    /// <summary>
    /// What the main menu is doing about Runmobile's row, asked separately from the disk
    /// reading above.
    ///
    /// It is a different question from a different pair of sources - the settings file and
    /// the game's own run count - and neither of them is the recordings directory that
    /// just refused. A row that reported "off" because the store had no save profile yet
    /// would be stating something about a menu it never asked, so the same reader the
    /// menu patch uses is asked here too. Where even that cannot answer, the control is
    /// already refused by <see cref="MyRunsRow"/> and the line under it says the disk
    /// could not be read.
    /// </summary>
    private static bool MainMenuRowShown()
    {
        try
        {
            return MainMenuLibraryRow.Shown();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not tell whether Runmobile is on the main menu: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
            return false;
        }
    }

    /// <summary>
    /// Writes the player's new standing policy and says what it now means.
    ///
    /// Nothing is removed here. The policy takes effect at the next main menu, which is
    /// where <see cref="RecordingRetention.ApplyOnce"/> already applies it, and the row's
    /// second line says how many runs that will be - so the standing act is as visible
    /// as the immediate one without this screen quietly performing it.
    ///
    /// That is only true because the latch is forgotten with the write. The retention
    /// owner applies a profile's policy once per process, and this profile's turn has
    /// already been taken by the time a player can reach this control, so a row
    /// promising the next main menu would otherwise be describing the next launch.
    /// </summary>
    private static void Retain(int keep)
    {
        // Each failure says only what happened. A component may not report a claim it
        // did not establish, and a refusal logged over a write that succeeded is one.
        try
        {
            RunmobileSettings.SetKeepRecentRuns(keep);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not write how many runs you keep: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
            return;
        }

        try
        {
            RecordingRetention.ReapplyPolicyAtNextMenu();
            Redraw(null);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] wrote how many runs you keep, but could not arm it for this " +
                $"session or say so on the screen: {ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Asks, in the game's own popup, before anything goes.
    ///
    /// The way out is the focused button. A player who opened a destructive
    /// confirmation with a controller should be one press from leaving it, and the
    /// affirmative stays on the right because that is where the game puts its own.
    /// </summary>
    private static void SetFetchRunIndex(bool fetch)
    {
        try
        {
            RunmobileSettings.SetFetchRunIndex(fetch);
            if (_row is { } row)
                row.Apply(row.Row, RunmobileSettings.Read().KeepRecentRuns, fetch);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not write whether the run index is fetched: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
            if (_row is { } row) row.Apply(row.Row, RunmobileSettings.Read().KeepRecentRuns,
                RunmobileSettings.Read().FetchRunIndex);
        }
    }

    /// <summary>
    /// Writes whether Runmobile is a row on the game's main menu, and says what the menu
    /// now does.
    ///
    /// Nothing on screen moves here: the menu behind this screen re-decides in its own
    /// <c>RefreshButtons</c>, which the player reaches by leaving settings, and this
    /// build has no way to redraw a menu it is not standing on. The row is redrawn from
    /// the disk instead, so what the control says is what the file now holds - and a
    /// write that failed leaves the control exactly where it was, saying what is still
    /// true.
    /// </summary>
    private static void SetMainMenuRow(bool show)
    {
        try
        {
            RunmobileSettings.SetShowMainMenuRow(show);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not write whether Runmobile is on the main menu: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
            return;
        }

        try
        {
            Redraw(null);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] wrote whether Runmobile is on the main menu, but could not say so " +
                $"on the screen: {ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    private static void AskToRemove()
    {
        if (_row is not { } row) return;

        var confirm = row.Row.Confirm;
        NGenericPopup? popup = null;
        NModalContainer? container = null;
        var added = false;
        try
        {
            popup = NGenericPopup.Create()
                ?? throw new InvalidOperationException("This process has no popup surface.");
            container = NModalContainer.Instance
                ?? throw new InvalidOperationException("This process has no modal container.");
            container.Add(popup, showBackstop: true);
            added = true;

            var content = popup.GetNode<NVerticalPopup>(VerticalPopupPath);
            content.SetText(confirm.Title, confirm.Body);

            content.InitYesButton(PlaceholderConfirmLabel, _ => Remove());
            content.YesButton.SetText(confirm.Remove);
            content.InitNoButton(PlaceholderCancelLabel, _ => NModalContainer.Instance?.Clear());
            content.NoButton.SetText(confirm.Keep);

            // Deferred for the reason the eligibility screen defers: adding the modal
            // updates the game's active screen context, which decides what is focused,
            // and grabbing focus before that has finished loses it.
            Callable.From(() => content.NoButton.GrabFocus()).CallDeferred();
        }
        catch (Exception ex)
        {
            try
            {
                if (added) container!.Clear();
                else popup?.QueueFree();
            }
            catch (Exception cleanup)
            {
                Log.Error(
                    $"[{RunmobileMod.ModId}] could not clear a failed confirmation: " +
                    $"{cleanup.GetType().Name}: {cleanup.Message}", 2);
            }

            Log.Error(
                $"[{RunmobileMod.ModId}] could not ask before removing your runs, so nothing was removed: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Removes them, having asked.
    ///
    /// The popup is cleared first so the screen behind it is what a player is looking at
    /// while the disk work happens, and the row is redrawn from the disk afterwards.
    /// </summary>
    private static void Remove()
    {
        NModalContainer.Instance?.Clear();

        int removed;
        try
        {
            removed = RecordingRetention.PurgeNow();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not remove your runs: {ex.GetType().Name}: {ex.Message}", 2);
            return;
        }

        try
        {
            Redraw(removed);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] removed your runs, but could not say so on the screen: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Reads the disk again and says what it now holds.
    ///
    /// <paramref name="removedJustNow"/> is the one thing here that is not read back,
    /// because it cannot be: how many runs went is what the removal returned, and the
    /// disk afterwards only shows what is left.
    /// </summary>
    private static void Redraw(int? removedJustNow)
    {
        if (_row is not { } row) return;

        var facts = OnDisk() with { RemovedJustNow = removedJustNow };
        row.Apply(MyRunsRow.For(facts), facts.Keep);
    }
}
