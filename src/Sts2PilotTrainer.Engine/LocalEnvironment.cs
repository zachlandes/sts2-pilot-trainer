using System.Reflection;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Reads this process's game state and names any host-supplied unlock model.
///
/// The one owner of where v0.111.0 keeps the things a preflight compares: the run
/// in progress, and the unlock state a run here would be generated against. The
/// rules that judge those readings live in
/// <see cref="Sts2PilotTrainer.Replay.EnvironmentPreflight"/>, which has no game
/// code and can therefore be tested on a machine that does not own the game.
///
/// Nothing here writes: the player's save, progress, unlocks and installed build are
/// inputs, and a tool that edited them to make a replay possible would have destroyed
/// the thing the replay was evidence about.
/// </summary>
public static class LocalEnvironment
{
    /// <summary>
    /// The unlock categories a run's content pools are drawn from, as the game's own
    /// <c>UnlockState</c> exposes them, paired with the name they are reported under.
    ///
    /// Read from the build rather than written down: the required count for each is
    /// whatever <c>UnlockState.all</c> holds on the build in front of us, so a game
    /// update that adds cards raises the bar without anyone editing this list.
    /// </summary>
    private static readonly (string Name, string Property)[] UnlockCategories =
    [
        ("characters", "Characters"),
        ("cards", "Cards"),
        ("card_pools", "CardPools"),
        ("character_card_pools", "CharacterCardPools"),
        ("relics", "Relics"),
        ("potions", "Potions"),
        ("shared_ancients", "SharedAncients"),
    ];

    /// <summary>How many missing ids a diagnostic names before it stops listing them.
    /// A shortfall of three hundred cards is a sentence, not a wall of ids; the exact
    /// counts are already in the field's expected and actual values.</summary>
    private const int MissingSampleLimit = 8;

    /// <summary>
    /// Everything checkable before a run exists, using this installation and the
    /// selected, explicitly identified progress source.
    /// </summary>
    /// <param name="expected">
    /// The manifest identity being checked against. Used only to know which
    /// character's ascension ceiling to read - never to fill in a reading.
    /// </param>
    /// <param name="progress">
    /// Which unlock state to read. <see cref="PlayerProgress.LocalProfile"/> reads
    /// this process's profile and is what the in-game host uses. The other two are
    /// states the headless host supplies in place of a profile it does not have, and they are
    /// reported as such rather than as a reading of anyone.
    /// </param>
    public static LocalPrerequisites ReadPrerequisites(
        EnvironmentIdentity expected, PlayerProgress progress = PlayerProgress.AllUnlocked)
    {
        // Which reading answers "what build is this" depends on how the engine got
        // here. Inside the retail client there is no prepared copy and no bootstrap
        // receipt to consult; the running process is the authority on itself.
        var identity = EngineHost.Origin == EngineOrigin.RunningGame
            ? GameIdentity.ReadFromRunningGame()
            : GameIdentity.Read();
        var inventory = ReadUnlockInventory(progress);

        return new LocalPrerequisites
        {
            BuildVersion = identity.BuildVersion,
            BuildDateUtc = identity.BuildDateUtc,
            ContentHash = identity.ContentHash,
            Mods = ReadMods(),
            Unlocks = inventory,
            LockedActs = LockedActs(expected.Acts.Value, progress),
            ProfileAscensionCeiling = inventory.FromPlayerProfile
                ? ReadProfileAscensionCeiling(expected.Character.Value)
                : null,
        };
    }

    private static IReadOnlyList<LocalMod> ReadMods()
    {
        EngineHost.Start();
        return ModManager.Mods
            .Select(mod =>
            {
                var manifest = mod.manifest ?? throw new EngineException(
                    $"The running game's mod manager reported a {mod.state} mod without a manifest.");
                return new LocalMod(
                    manifest.id ?? throw new EngineException("A mod manifest has no id."),
                    manifest.name ?? throw new EngineException("A mod manifest has no name."),
                    manifest.version ?? throw new EngineException("A mod manifest has no version."),
                    manifest.affectsGameplay,
                    mod.state.ToString());
            })
            .OrderBy(mod => mod.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Which of the named acts the game reports locked under this unlock state.
    ///
    /// Asked of the act model rather than worked out from an epoch id, because the
    /// mapping between the two is the game's to know and ours to read.
    /// </summary>
    private static IReadOnlyList<string> LockedActs(
        IReadOnlyList<string> actModelIds, PlayerProgress progress)
    {
        EngineHost.Start();
        var unlockState = ResolveUnlockState(progress);
        var locked = new List<string>();
        foreach (var modelId in actModelIds)
        {
            var act = ModelDb.Acts.FirstOrDefault(a => a.Id.ToString() == modelId);
            // An act this build does not ship is not a locked act, and saying so here
            // would send someone off to unlock something that does not exist. The
            // run-identity gate is where a wrong act id gets caught.
            if (act is null) continue;
            if (!act.IsUnlocked(unlockState)) locked.Add(modelId);
        }

        return locked;
    }

    /// <summary>
    /// The run in progress, or null when there is none.
    ///
    /// Null is an answer, not a failure: a freshly launched game has no run, and the
    /// preflight refuses on that rather than pretending the question was skipped.
    /// </summary>
    public static LocalRunReading? ReadStartedRun()
    {
        var manager = RunManager.Instance;
        if (manager is null || !manager.IsInProgress) return null;

        // RunManager.State is not public on this build, so it is read reflectively
        // rather than reconstructed. Reading the run the game actually holds is the
        // whole point; a second copy assembled from what we passed in would agree
        // with itself no matter what the engine did with it.
        var accessor = typeof(RunManager).GetProperty(
            "State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new EngineException("RunManager.State is absent from this build.");
        if (accessor.GetValue(manager) is not RunState state) return null;

        var character = state.Players.Count > 0 ? state.Players[0].Character : null;

        return new LocalRunReading
        {
            Origin = "run in progress, read from RunManager.State",
            Seed = state.Rng.StringSeed,
            GameMode = state.GameMode.ToString().ToLowerInvariant(),
            Ascension = state.AscensionLevel,
            Character = character?.Id.ToString() ?? "unknown",
            Acts = state.Acts.Select(act => act.Id.ToString()).ToList(),
        };
    }

    /// <summary>
    /// Builds the unlock state a run started here would be generated against.
    ///
    /// The retail client derives this from the player's save progress. That is not
    /// available for someone else's run, so a model has to be chosen and named, and
    /// the choice has to be visible in the artifact rather than buried in a default.
    /// </summary>
    internal static UnlockState ResolveUnlockState(PlayerProgress progress) => progress switch
    {
        PlayerProgress.AllUnlocked => UnlockState.all,
        PlayerProgress.NoneUnlocked => UnlockState.none,
        PlayerProgress.LocalProfile => LocalProfileUnlockState(),
        _ => throw new EngineException($"Unknown player-progress model '{progress}'."),
    };

    /// <summary>Where a progress model came from, in words a report can print, so a
    /// supplied state is never presented as a reading of somebody's save.</summary>
    public static string OriginOf(PlayerProgress progress) => progress switch
    {
        PlayerProgress.AllUnlocked =>
            "UnlockState.all, supplied by the host in place of the source player's profile",
        PlayerProgress.NoneUnlocked =>
            "UnlockState.none, supplied by the host",
        PlayerProgress.LocalProfile =>
            "the save progress of whichever profile this process has, via " +
            "SaveManager.GenerateUnlockStateFromProgress - inside the retail client that is the player's own, " +
            "and inside this headless host it is the empty sandbox profile, because the player's save is a " +
            "read-only input the host never opens",
        _ => throw new EngineException($"Unknown player-progress model '{progress}'."),
    };

    private static UnlockInventory ReadUnlockInventory(PlayerProgress progress)
    {
        EngineHost.Start();
        var actual = ResolveUnlockState(progress);
        var complete = UnlockState.all;

        var categories = new List<UnlockCategory>();
        foreach (var (name, property) in UnlockCategories)
        {
            var required = ModelIds(complete, property);
            var available = ModelIds(actual, property);
            categories.Add(new UnlockCategory(
                name,
                available.Count,
                required.Count,
                required.Except(available, StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .Take(MissingSampleLimit)
                    .ToList()));
        }

        // Epochs last, and separately, because they are the actionable unit: the game
        // grants an epoch, and everything above is what an epoch makes available. A
        // report that named only the categories would tell a player what they are
        // missing without telling them what to go and unlock.
        var requiredEpochs = EpochIds(complete);
        var availableEpochs = EpochIds(actual);
        categories.Add(new UnlockCategory(
            "epochs",
            availableEpochs.Count,
            requiredEpochs.Count,
            requiredEpochs.Except(availableEpochs, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Take(MissingSampleLimit)
                .ToList()));

        return new UnlockInventory
        {
            Origin = OriginOf(progress),
            FromPlayerProfile = progress == PlayerProgress.LocalProfile,
            Categories = categories,
        };
    }

    /// <summary>
    /// The model ids in one unlock category.
    ///
    /// Reflective because the categories are named in one table above, so that the
    /// set of things checked is a list a reader can see rather than seven near-copies
    /// of the same three lines.
    /// </summary>
    private static IReadOnlyCollection<string> ModelIds(UnlockState state, string property)
    {
        var accessor = typeof(UnlockState).GetProperty(property, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new EngineException(
                $"UnlockState has no '{property}' on this build. The unlock categories this preflight " +
                "compares are named explicitly; refusing to silently check fewer of them.");

        var values = accessor.GetValue(state) as System.Collections.IEnumerable
            ?? throw new EngineException($"UnlockState.{property} did not enumerate on this build.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null) continue;
            var id = value.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)?.GetValue(value)
                ?? throw new EngineException($"An entry of UnlockState.{property} has no model id.");
            ids.Add(id.ToString() ?? string.Empty);
        }

        return ids;
    }

    private static IReadOnlyCollection<string> EpochIds(UnlockState state)
    {
        var field = typeof(UnlockState).GetField("_unlockedEpochIds", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new EngineException("UnlockState has no unlocked-epoch set on this build.");

        var values = field.GetValue(state) as System.Collections.IEnumerable
            ?? throw new EngineException("UnlockState's unlocked-epoch set did not enumerate on this build.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value?.ToString() is { } id) ids.Add(id);
        }

        return ids;
    }

    /// <summary>
    /// The highest ascension this profile records for a character. The game raises it
    /// when a run finishes at the level below, and uses it as the ceiling on what can
    /// be selected, so it is the prerequisite for replaying an ascension-10 run here.
    /// </summary>
    private static int ReadProfileAscensionCeiling(string characterModelId)
    {
        EngineHost.Start();
        var character = ModelDb.AllCharacters.FirstOrDefault(c => c.Id.ToString() == characterModelId);
        if (character is null) return 0;

        var progress = SaveManager.Instance?.Progress;
        var stats = progress?.GetStatsForCharacter(character.Id);
        return stats?.MaxAscension ?? 0;
    }

    private static UnlockState LocalProfileUnlockState()
    {
        var method = typeof(SaveManager).GetMethod(
            "GenerateUnlockStateFromProgress",
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new EngineException("SaveManager.GenerateUnlockStateFromProgress is absent from this build.");

        var target = method.IsStatic ? null : SaveManager.Instance;
        return method.Invoke(target, null) as UnlockState
               ?? throw new EngineException("GenerateUnlockStateFromProgress returned no unlock state.");
    }
}
