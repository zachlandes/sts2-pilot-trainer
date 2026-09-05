using System.Globalization;
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
    /// this process's profile for callers asking about the player. The rest are
    /// host-supplied states - the complete one, the empty one, and the recorded
    /// player's own where a recording carries it - and every supplied state is
    /// reported as such rather than as a reading of anyone. Omitted, it is the
    /// complete state, which is what a replay of somebody's video is generated
    /// against.
    /// </param>
    public static LocalPrerequisites ReadPrerequisites(
        EnvironmentIdentity expected, PlayerProgress? progress = null)
    {
        progress ??= PlayerProgress.AllUnlocked;
        // Which reading answers "what build is this" depends on how the engine got
        // here. Inside the retail client there is no prepared copy and no bootstrap
        // receipt to consult; the running process is the authority on itself.
        var identity = EngineHost.Origin == EngineOrigin.RunningGame
            ? GameIdentity.ReadFromRunningGame()
            : GameIdentity.Read();

        // A reading, so a state this build cannot build is reported rather than
        // thrown: the screen that exists to say why a recording cannot be replayed
        // would otherwise crash instead of rendering it. The refusal itself stays at
        // run construction, where a state that cannot be built would otherwise become
        // a run nobody asked for.
        var state = UnbuildableState(progress) is null ? ResolveUnlockState(progress) : null;
        var inventory = ReadUnlockInventory(progress, state);

        return new LocalPrerequisites
        {
            BuildVersion = identity.BuildVersion,
            BuildDateUtc = identity.BuildDateUtc,
            ContentHash = identity.ContentHash,
            Mods = ReadMods(),
            Unlocks = inventory,
            LockedActs = state is null ? null : LockedActs(expected.Acts.Value, state),
            ProfileAscensionCeiling = inventory.FromPlayerProfile
                ? ReadProfileAscensionCeiling(expected.Character.Value)
                : null,
        };
    }

    /// <summary>Every mod this game reported, identified by its own manifest.</summary>
    public static IReadOnlyList<LocalMod> ReadMods()
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
        IReadOnlyList<string> actModelIds, UnlockState unlockState)
    {
        EngineHost.Start();
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
    /// The run the game is holding right now, or null when there is none.
    ///
    /// <c>RunManager.State</c> is not public on this build, so it is read reflectively
    /// rather than reconstructed. Reading the run the game actually holds is the whole
    /// point; a second copy assembled from what we passed in would agree with itself
    /// no matter what the engine did with it. One owner of that read, because a second
    /// accessor is a second thing to fix when a build moves it.
    /// </summary>
    public static RunState? StartedRunState()
    {
        var manager = RunManager.Instance;
        if (manager is null || !manager.IsInProgress) return null;

        var accessor = typeof(RunManager).GetProperty(
            "State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new EngineException("RunManager.State is absent from this build.");
        return accessor.GetValue(manager) as RunState;
    }

    /// <summary>
    /// The run in progress, or null when there is none.
    ///
    /// Null is an answer, not a failure: a freshly launched game has no run, and the
    /// preflight refuses on that rather than pretending the question was skipped.
    /// </summary>
    public static LocalRunReading? ReadStartedRun()
    {
        if (StartedRunState() is not { } state) return null;

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
    internal static UnlockState ResolveUnlockState(PlayerProgress progress)
    {
        if (progress.Inventory is { } inventory) return ExactUnlockState(inventory);
        if (progress == PlayerProgress.AllUnlocked) return UnlockState.all;
        if (progress == PlayerProgress.NoneUnlocked) return UnlockState.none;
        if (progress == PlayerProgress.LocalProfile) return LocalProfileUnlockState();
        throw new EngineException($"Unknown player-progress model '{progress}'.");
    }

    /// <summary>
    /// The recorded player's own unlock state, built from the three values the game's
    /// own <c>UnlockState</c> is made of.
    ///
    /// Built rather than approximated, and through the game's own constructor: the
    /// seven categories a preflight reports are derived properties with no setter, so
    /// there is exactly one way in and this is it. An id this build does not ship is
    /// refused rather than dropped, because a state missing one epoch generates a
    /// different run behind an identical map. The run count is passed through and
    /// compared against nothing: no part of this installation has to match it for the
    /// state to be constructible.
    /// </summary>
    private static UnlockState ExactUnlockState(UnlockStateInventory inventory)
    {
        if (UnbuildableState(inventory) is { } refusal) throw new EngineException(refusal);

        var shippedEncounters = ShippedEncounterIds();
        return new UnlockState(
            inventory.Epochs,
            inventory.EncountersSeen.Select(id => shippedEncounters[id]).ToList(),
            inventory.Runs);
    }

    /// <summary>
    /// Why the state this progress model names cannot be built here, or null where it
    /// can.
    ///
    /// A sentence rather than an exception, because the same fact is a refusal on the
    /// way into a run and a reportable shortfall on the way into a preflight, and a
    /// reader that threw would take the report down with it. Only a supplied exact
    /// state can be unbuildable: the complete, empty and profile states are this
    /// build's own.
    /// </summary>
    private static string? UnbuildableState(PlayerProgress progress) =>
        progress.Inventory is { } inventory ? UnbuildableState(inventory) : null;

    /// <summary>
    /// The same answer for an inventory on its own, which is what run construction
    /// holds when it refuses.
    ///
    /// One rule for both id lists, because they fail the same way and for the same
    /// reason: the state cannot be built, so the run generated here would not be the
    /// recording's however closely everything else matched. The remediation is the
    /// game's, as it always is - this build does not ship it, and nothing here will
    /// pretend otherwise.
    /// </summary>
    private static string? UnbuildableState(UnlockStateInventory inventory)
    {
        EngineHost.Start();

        var shortfalls = new[]
        {
            Unshipped(inventory.Epochs, EpochIds(UnlockState.all).Contains, "epoch"),
            Unshipped(inventory.EncountersSeen, ShippedEncounterIds().ContainsKey, "encounter"),
        }.OfType<string>().ToList();

        return shortfalls.Count == 0 ? null : string.Join(" ", shortfalls);
    }

    private static string? Unshipped(
        IReadOnlyList<string> named, Func<string, bool> ships, string what)
    {
        var missing = named.Where(id => !ships(id)).ToList();
        if (missing.Count == 0) return null;

        return
            $"This build does not ship {missing.Count.ToString(CultureInfo.InvariantCulture)} of the " +
            $"{named.Count.ToString(CultureInfo.InvariantCulture)} {what} id(s) the recording was played " +
            "with, so the unlock state it was generated against cannot be built here and the same seed " +
            $"produces a different run. Missing: {string.Join(", ", missing.Take(MissingSampleLimit))}.";
    }

    /// <summary>Every encounter id this build ships, by the string a manifest names it
    /// with. The game's own ids, so a recording that names one this build spells
    /// differently is refused rather than silently dropped from the state.</summary>
    private static IReadOnlyDictionary<string, ModelId> ShippedEncounterIds()
    {
        EngineHost.Start();
        var ids = new Dictionary<string, ModelId>(StringComparer.Ordinal);
        foreach (var encounter in ModelDb.AllEncounters)
        {
            ids[encounter.Id.ToString()] = encounter.Id;
        }

        return ids;
    }

    /// <summary>Where a progress model came from, in words a report can print, so a
    /// supplied state is never presented as a reading of somebody's save.</summary>
    public static string OriginOf(PlayerProgress progress)
    {
        if (progress.Inventory is { } inventory)
        {
            return
                "the recorded player's own unlock state, captured by the recorder and supplied to this run - " +
                $"{inventory.Epochs.Count.ToString(CultureInfo.InvariantCulture)} epoch(s), " +
                $"{inventory.EncountersSeen.Count.ToString(CultureInfo.InvariantCulture)} encounter(s) seen, " +
                $"{inventory.Runs.ToString(CultureInfo.InvariantCulture)} run(s) played. Nobody's profile was " +
                "read to produce it and none is changed by it";
        }

        if (progress == PlayerProgress.AllUnlocked)
        {
            return "UnlockState.all, supplied by the host in place of the source player's profile";
        }

        if (progress == PlayerProgress.NoneUnlocked) return "UnlockState.none, supplied by the host";

        if (progress == PlayerProgress.LocalProfile)
        {
            return
                "the save progress of whichever profile this process has, via " +
                "SaveManager.GenerateUnlockStateFromProgress - inside the retail client that is the player's own, " +
                "and inside this headless host it is the empty sandbox profile, because the player's save is a " +
                "read-only input the host never opens";
        }

        throw new EngineException($"Unknown player-progress model '{progress}'.");
    }

    private static UnlockInventory ReadUnlockInventory(PlayerProgress progress, UnlockState? actual)
    {
        EngineHost.Start();
        var complete = UnlockState.all;

        // No state, no counts. A state this build cannot build has no categories to
        // count, and counting the ids it does ship instead would report a state nobody
        // asked for as though it were the recording's. What this build ships is
        // reported below either way, and that is what an exact requirement is judged
        // against.
        var categories = new List<UnlockCategory>();
        if (actual is not null)
        {
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

            // Epochs last, and separately, because they are the actionable unit: the
            // game grants an epoch, and everything above is what an epoch makes
            // available. A report that named only the categories would tell a player
            // what they are missing without telling them what to go and unlock.
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
        }

        return new UnlockInventory
        {
            Origin = OriginOf(progress),
            FromPlayerProfile = progress == PlayerProgress.LocalProfile,
            Categories = categories,

            // What this build ships, for the two lists an exact requirement names.
            //
            // Read off the build and not off the selected progress model: the question
            // an exact requirement asks is whether a state made of these ids can be
            // constructed here at all, which is a fact about the installation rather
            // than about whichever model this reading was taken under.
            //
            // Read through the same reader the recorder uses, so "the ids a state is
            // made of" has one answer. A second enumeration here would be a second
            // answer, and the one nobody exercises is the one that drifts.
            ShippedIds = ReadShippedUnlockInventory().IdLists()
                .ToDictionary(list => list.Name, list => list.Ids, StringComparer.Ordinal),
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

    /// <summary>
    /// Everything this build ships, as the three values a state is constructed from.
    ///
    /// The complete state read the way the recorder reads a run's own, which is what
    /// makes the reader checkable without somebody playing: hand this back through
    /// <see cref="PlayerProgress.Exact"/> and a run generated against it is the run
    /// the complete state produces, or the reader dropped something.
    /// </summary>
    public static UnlockStateInventory ReadShippedUnlockInventory()
    {
        EngineHost.Start();
        return ReadUnlockStateInventory(UnlockState.all);
    }

    /// <summary>
    /// One unlock state as the three values it was constructed from: the epochs
    /// unlocked, the encounters seen and the runs played.
    ///
    /// The read-back counterpart of <see cref="ExactUnlockState"/>, and here rather
    /// than in the recorder that wants it because where v0.111.0 keeps these is this
    /// class's to know. Read off the state the run was generated against, which is the
    /// exact source: a profile read a moment later would be a reading of a different
    /// moment wearing the same name.
    /// </summary>
    public static UnlockStateInventory ReadUnlockStateInventory(UnlockState state)
    {
        var encounters = typeof(UnlockState).GetField("_encountersSeen", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new EngineException("UnlockState has no seen-encounter set on this build.");

        var seen = encounters.GetValue(state) as System.Collections.IEnumerable
            ?? throw new EngineException("UnlockState's seen-encounter set did not enumerate on this build.");

        var runs = typeof(UnlockState).GetProperty("NumberOfRuns", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new EngineException("UnlockState has no NumberOfRuns on this build.");

        return new UnlockStateInventory
        {
            Epochs = EpochIds(state).Order(StringComparer.Ordinal).ToList(),
            EncountersSeen = seen.Cast<object>()
                .Select(id => id.ToString() ?? string.Empty)
                .Where(id => id.Length > 0)
                .Order(StringComparer.Ordinal)
                .ToList(),
            Runs = (int)(runs.GetValue(state)
                ?? throw new EngineException("UnlockState.NumberOfRuns read as null on this build.")),
        };
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
