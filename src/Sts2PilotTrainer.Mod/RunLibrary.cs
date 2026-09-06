using MegaCrit.Sts2.Core.Logging;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Every run this game could offer, gathered from the two places a run can come from,
/// with this build's verdict on each.
///
/// It is the seam between the disk and the derivation: <see cref="RunLibraryStore"/>
/// reads files, <see cref="RunVerdicts"/> asks the preflight, and
/// <c>RunBrowser</c> turns what comes back into a screen. Nothing here decides what
/// is shown - the hidden rule lives in <c>LibraryRun.Listed</c>, in one place, so
/// "why is this run not in the list" has one field to look at.
///
/// <para>Two sources today and the shape allows a third. The recordings that travel
/// inside the mod are the Community tab's "Included with Runmobile" group and are
/// there with no network and no index; the recorder's own output is My runs. Featured
/// and Recent are the fetched index's groups, and until an index exists they are
/// simply empty - which is what the browser draws when a group has nothing in it,
/// rather than a placeholder saying so.</para>
///
/// <para><b>Nothing is cached.</b> The library is re-read each time it is asked for,
/// because the recorder writes into it while the game is running and a list built once
/// would be a list that goes stale the moment a player finishes a run. It is a few
/// files on a menu screen.</para>
/// </summary>
internal static class RunLibrary
{
    /// <summary>
    /// Every run, listed or not, with the verdict that decides which.
    ///
    /// The hidden ones are here on purpose: the numeral under the list counts them, and
    /// a run code finds them. A list filtered before it arrived could do neither.
    /// </summary>
    internal static IReadOnlyList<LibraryRun> Runs()
    {
        var build = ThisBuild();
        var progress = RunLibraryStore.ReadProgress();
        var runs = new List<LibraryRun>();

        foreach (var included in Included())
        {
            runs.Add(LibraryRun.From(
                included,
                RunOrigin.Included,
                RunVerdicts.For(included, build),
                progress.PlayedFrom(included.RunId)));
        }

        foreach (var stored in RunLibraryStore.MyRecordings())
        {
            runs.Add(LibraryRun.From(
                stored.Recording,
                RunOrigin.Mine,
                RunVerdicts.For(stored.Recording, build),
                progress.PlayedFrom(stored.Recording.RunId),
                recorded: stored.Started));
        }

        return runs;
    }

    /// <summary>
    /// Whether the library has a single run to offer.
    ///
    /// The Compendium button asks this each time the menu opens. A button opening an
    /// empty browser would be a promise the mod cannot keep on a game whose build no
    /// run in it was recorded on.
    /// </summary>
    internal static bool HasAnythingToShow()
    {
        try
        {
            return Runs().Any(run => run.Listed);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not read the run library: {ex.GetType().Name}: {ex.Message}", 2);
            return false;
        }
    }

    /// <summary>The recording behind one row, or null when nothing here is that
    /// run.</summary>
    internal static ReplayManifest? RecordingFor(string runId)
    {
        foreach (var included in Included())
        {
            if (string.Equals(included.RunId, runId, StringComparison.Ordinal)) return included;
        }

        return RunLibraryStore.MyRecordings()
            .FirstOrDefault(stored =>
                string.Equals(stored.Recording.RunId, runId, StringComparison.Ordinal))
            ?.Recording;
    }

    /// <summary>
    /// The build this game is, as every verdict and every refusal names it.
    ///
    /// Read from the game rather than remembered, because it is the one value the whole
    /// hidden rule turns on and a stale copy of it would hide the wrong runs.
    /// </summary>
    internal static string ThisBuild() => GameIdentity.Read().BuildVersion;

    /// <summary>
    /// The recordings that travel inside the mod.
    ///
    /// The Combat Trainer's, because it is the module that ships one and reads it. A
    /// second copy of that resource here would be a second thing to keep in step with
    /// what the assembly actually carries, and a Combat Trainer that refused to read
    /// its own recording would be one this list quietly disagreed with.
    /// </summary>
    private static IReadOnlyList<ReplayManifest> Included() =>
        CombatTrainerModule.Instance.Enabled ? [CombatTrainerModule.Instance.Recording] : [];
}
