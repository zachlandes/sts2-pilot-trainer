using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
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
internal static class MyRunsSettings
{
    /// <summary>The popup scene's own name for its content, resolved the way the
    /// game's code resolves it.</summary>
    private const string VerticalPopupPath = "VerticalPopup";

    /// <summary>Label the confirm's buttons carry until this mod replaces them. Never
    /// shown: a DLL-only mod contributes no localization table, so the game's own keys
    /// stand in and the text is set directly. Same reason as
    /// <see cref="TrainerScreen"/>.</summary>
    private static LocString PlaceholderConfirmLabel => new("main_menu_ui", "GENERIC_POPUP.confirm");

    private static LocString PlaceholderCancelLabel => new("main_menu_ui", "GENERIC_POPUP.cancel");

    private static MyRunsSettingsRow? _row;

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
        _row = MyRunsSettingsRow.Build(
            MyRunsRow.For(facts), facts.Keep, width, font, Retain, AskToRemove);
        return _row;
    }

    /// <summary>
    /// What the disk holds, or the fact that it could not be asked.
    ///
    /// The store refuses before the game has chosen a save profile, because there is no
    /// answer yet to whose runs these are.
    /// </summary>
    private static MyRunsFacts OnDisk()
    {
        try
        {
            return RecordingRetention.OnDisk();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not read what your runs take on this disk: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
            return new MyRunsFacts(
                Runs: 0,
                Bytes: 0,
                Keep: RunmobileSettings.DefaultKeepRecentRuns,
                SettingsReadable: false,
                StoreReadable: false);
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
        try
        {
            RunmobileSettings.SetKeepRecentRuns(keep);
            RecordingRetention.ReapplyPolicyAtNextMenu();
            Redraw(null);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not write how many runs you keep: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Asks, in the game's own popup, before anything goes.
    ///
    /// The way out is the focused button. A player who opened a destructive
    /// confirmation with a controller should be one press from leaving it, and the
    /// affirmative stays on the right because that is where the game puts its own.
    /// </summary>
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

        try
        {
            Redraw(RecordingRetention.PurgeNow());
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not remove your runs: {ex.GetType().Name}: {ex.Message}", 2);
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
