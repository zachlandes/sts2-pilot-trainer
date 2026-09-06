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
/// <para><b>Nothing is cached, and the cheap question does not build the list.</b> The
/// library is re-read each time it is asked for, because the recorder writes into it
/// while the game is running and a list built once would be a list that goes stale the
/// moment a player finishes a run. It is not a few files: the retention default keeps
/// fifty recordings, each hundreds of kilobytes, and each costs a deserialization and a
/// preflight. So <see cref="HasAnythingToShow"/>, which the Compendium asks on every
/// menu open, stops at the first listed run. In the ordinary case that is the shipped
/// recording: one preflight, and not one of the player's own manifests read. It reaches
/// them only when no shipped recording is playable on this build, which is the case
/// where the answer genuinely depends on them.</para>
///
/// <para>It is one walk that stops early rather than a second walk of its own.
/// <see cref="Gather"/> is the only place a <c>LibraryRun</c> is built from a recording,
/// so the cheap question and the whole list cannot disagree about source order or about
/// what a run is - there is nothing to keep in step. A lazy sequence would have been the
/// obvious shape and is not available here: an iterator in this assembly is a
/// compiler-written class whose fields include the element type, and <c>LibraryRun</c>
/// lives in a sibling the game cannot resolve at the phase it enumerates these types,
/// which <c>ModAssemblyLoadOrderTests</c> refuses.</para>
/// </summary>
internal static class RunLibrary
{
    /// <summary>
    /// Every run, listed or not, with the verdict that decides which.
    ///
    /// The hidden ones are here on purpose: the numeral under the list counts them, and
    /// a run code finds them. A list filtered before it arrived could do neither.
    /// </summary>
    internal static IReadOnlyList<LibraryRun> Runs() => Gather(stopAtFirstListed: false);

    /// <summary>
    /// The one walk of the two sources, optionally stopping the moment it has a listed
    /// run.
    ///
    /// The shipped recordings come first, so a caller that only wants to know whether
    /// anything is playable usually answers without reading a manifest off this
    /// computer. Stopping early changes how far it gets and nothing about what it built
    /// on the way, which is what makes the cheap answer and the list the same answer.
    /// </summary>
    private static IReadOnlyList<LibraryRun> Gather(bool stopAtFirstListed)
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
            if (stopAtFirstListed && runs[^1].Listed) return runs;
        }

        foreach (var stored in RunLibraryStore.MyRecordings())
        {
            runs.Add(LibraryRun.From(
                stored.Recording,
                RunOrigin.Mine,
                RunVerdicts.For(stored.Recording, build),
                progress.PlayedFrom(stored.Recording.RunId),
                recorded: stored.Started));
            if (stopAtFirstListed && runs[^1].Listed) return runs;
        }

        return runs;
    }

    /// <summary>
    /// Whether the library has a single run to offer.
    ///
    /// The Compendium button asks this each time the menu opens. A button opening an
    /// empty browser would be a promise the mod cannot keep on a game whose build no
    /// run in it was recorded on.
    ///
    /// It stops at the first listed run rather than building the library, which is what
    /// makes it cheap enough to ask on a menu open at all. It is the same walk
    /// <see cref="Runs"/> makes, cut short - so it answers true exactly when the list
    /// would hold a row, without a second reading of what "listed" means.
    /// </summary>
    internal static bool HasAnythingToShow()
    {
        try
        {
            return Gather(stopAtFirstListed: true).Any(run => run.Listed);
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
