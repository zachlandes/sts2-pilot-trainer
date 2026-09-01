using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// One run, driven through the real game code.
///
/// Deliberately started through the same entry point the retail client uses -
/// <see cref="RunState.CreateForNewRun"/> followed by
/// <see cref="RunManager.SetUpNewSingleplayer"/> - and not through the engine's
/// test-construction path. The test path exists and is easier to drive, but it is
/// documented to skip and alter run setup (tutorial modifications and content
/// discovery order among them), so a run started that way is not the run the
/// player played, and no amount of downstream checking would notice.
///
/// The run is never saved: <c>shouldSave</c> is false, and the host's sandbox
/// refuses filesystem writes outside its own directory besides.
/// </summary>
public sealed class GameSession
{
    private RunState? _runState;

    public RunState RunState => _runState
        ?? throw new EngineException("No run has been started in this session.");

    /// <summary>
    /// Starts a run exactly as the retail client would, at the given identity.
    /// </summary>
    /// <param name="seed">The seed string as the game displays it.</param>
    /// <param name="characterModelId">e.g. <c>CHARACTER.IRONCLAD</c>.</param>
    /// <param name="ascension">Ascension level.</param>
    /// <param name="gameMode">
    /// Standard for an ordinary run. This is a real parameter rather than an
    /// assumption because the game persists it on every run and every save, and it
    /// changes run setup - so replaying a standard run in custom mode would be a
    /// different run wearing the same seed.
    /// </param>
    public void StartRun(
        string seed, string characterModelId, int ascension, string gameMode, IReadOnlyList<string> actModelIds) =>
        StartRun(seed, characterModelId, ascension, gameMode, actModelIds, PlayerProgress.AllUnlocked);

    /// <param name="progress">
    /// Which player-progress model to run under. This is a real input, not a
    /// convenience: the game derives a run's content from the player's unlocks and
    /// from which encounters they have already seen, so two players on the same seed
    /// and build do not necessarily get the same run. Nothing in a video shows it.
    /// </param>
    public void StartRun(
        string seed, string characterModelId, int ascension, string gameMode,
        IReadOnlyList<string> actModelIds, PlayerProgress progress) =>
        StartRun(seed, characterModelId, ascension, gameMode, actModelIds, progress, []);

    public void StartRun(
        string seed, string characterModelId, int ascension, string gameMode,
        IReadOnlyList<string> actModelIds, PlayerProgress progress,
        IReadOnlyList<string> modifierTypeNames)
    {
        EngineHost.Start();

        if (_runState is not null)
        {
            throw new EngineException("This session already has a run. Start a fresh process for a fresh run.");
        }

        var character = FindCharacter(characterModelId);
        var unlockState = LocalEnvironment.ResolveUnlockState(progress);
        var player = Player.CreateForNewRun(character, unlockState, 1uL);

        // The acts, named explicitly rather than taken as the default at each index.
        //
        // Defaulting here would be a quiet mistake: this build ships two acts at index
        // 0, so "the default one" is a different run from the one a video shows, with
        // the same seed and the same map.
        //
        // Cloned before use, because the run mutates its acts and the database's
        // copies are canonical - the engine refuses a canonical model here rather
        // than let one run's changes leak into the next. The retail front end does
        // the same cloning on the caller's behalf; this path does it explicitly.
        var acts = actModelIds.Select(FindAct).Select(a => a.ToMutable()).ToList();

        if (acts.Count == 0)
        {
            throw new EngineException("No acts were named, so no run can be constructed.");
        }

        var modifiers = modifierTypeNames.Select(FindModifier).Select(modifier => modifier.ToMutable()).ToList();
        var runState = RunState.CreateForNewRun(
            players: [player],
            acts: acts,
            modifiers: modifiers,
            gameMode: ParseGameMode(gameMode),
            ascensionLevel: ascension,
            seed: seed);

        // shouldSave: false. The player's save directory is a read-only input.
        // dailyTime: null - a non-null value would enter this run on the daily
        // leaderboard, which is both wrong and outward-facing.
        // Restore a behaviour the headless flag would otherwise switch off.
        //
        // RunManager.ShouldApplyTutorialModifications reads, in order: this override,
        // then TestMode (returning false when it is on), then the game mode - and for
        // a standard run in retail it returns true, always, not only on a first run.
        // The headless flag therefore silently disables it, and GenerateRooms uses it,
        // so a run started this way draws its encounters from a different sequence
        // than the player's did. Forcing it back for standard runs is what makes the
        // generated content the same content.
        //
        // Scoped to standard mode, because that is exactly what retail does: daily and
        // custom runs return false from the same method.
        RunManager.Instance.ForceDiscoveryOrderModifications = ParseGameMode(gameMode) == GameMode.Standard;

        // This order is the retail client's, read from its own start-run path, and it
        // is load-bearing rather than incidental. SetUpNewSingleplayer already calls
        // InitializeNewRun and GenerateRooms, both of which draw from the run's
        // upfront RNG stream - calling either again advances that stream and silently
        // produces a different, entirely valid-looking run.
        RunManager.Instance.SetUpNewSingleplayer(runState, shouldSave: false, dailyTime: null);
        RunManager.Instance.FinalizeStartingRelics().GetAwaiter().GetResult();
        RunManager.Instance.Launch();

        _runState = runState;
    }

    /// <summary>
    /// Enters an act far enough to have its map, and no further.
    ///
    /// Map generation is pure run logic driven by the upfront RNG stream; the rest of
    /// entering an act builds the room the player lands in, which drags in the
    /// presentation layer. Keeping them apart means the seed can be verified against a
    /// video's map without the host having to stand up any of that - which is both
    /// simpler and a stronger claim, because fewer of the host's own compromises are
    /// in the way.
    /// </summary>
    public void EnterActForMap(int actIndex)
    {
        // SetActInternal is the step of EnterAct that sets the act and generates its
        // map. The rest of EnterAct builds the room the player lands in and the map
        // screen, which are presentation. Calling this directly keeps the seed check
        // clear of the host's presentation workarounds entirely.
        var setAct = typeof(RunManager).GetMethod(
            "SetActInternal",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            ?? throw new EngineException("RunManager.SetActInternal is absent from this build.");
        setAct.Invoke(RunManager.Instance, [actIndex]);
    }

    /// <summary>
    /// The generated map for an act, as topology only.
    ///
    /// Topology is the part a video can independently confirm: which node types sit
    /// where, and which nodes connect to which. It is generated from the run's
    /// upfront RNG stream, so it is a function of the seed - which makes comparing
    /// it against the map a video shows a genuine, OCR-independent test of whether
    /// the seed we read is the seed that was played.
    /// </summary>
    public MapTopology CurrentMapTopology()
    {
        var map = RunState.Map
            ?? throw new EngineException("The current act has no generated map.");
        var actIndex = RunState.CurrentActIndex;

        var nodes = new List<MapNode>();
        var edges = new List<MapEdge>();

        for (var row = 0; row < map.GetRowCount(); row++)
        {
            for (var column = 0; column < map.GetColumnCount(); column++)
            {
                var point = map.GetPoint(column, row);
                if (point is null || point.PointType == MapPointType.Unassigned) continue;

                nodes.Add(new MapNode(row, column, point.PointType.ToString()));
                foreach (var child in point.Children.OrderBy(c => c.coord.col).ThenBy(c => c.coord.row))
                {
                    edges.Add(new MapEdge(row, column, child.coord.row, child.coord.col));
                }
            }
        }

        return new MapTopology(
            actIndex,
            map.GetRowCount(),
            map.GetColumnCount(),
            nodes.OrderBy(n => n.Row).ThenBy(n => n.Column).ToList(),
            edges.OrderBy(e => e.FromRow).ThenBy(e => e.FromColumn).ThenBy(e => e.ToColumn).ToList());
    }

    /// <summary>
    /// Maps the manifest's game-mode string onto the engine's enum. Unknown values
    /// are refused rather than defaulted: silently treating an unrecognised mode as
    /// standard would produce a confident replay of a different run.
    /// </summary>
    private static GameMode ParseGameMode(string gameMode) => gameMode switch
    {
        "standard" => GameMode.Standard,
        "custom" => GameMode.Custom,
        "daily" => GameMode.Daily,
        _ => throw new EngineException(
            $"Unknown game mode '{gameMode}'. Known modes: standard, custom, daily."),
    };

    private static ActModel FindAct(string modelId) =>
        ModelDb.Acts.FirstOrDefault(a => a.Id.ToString() == modelId)
        ?? throw new EngineException(
            $"No act with model id '{modelId}'. This build ships: " +
            string.Join(", ", ModelDb.Acts.OrderBy(a => a.Index).Select(a => $"{a.Index}:{a.Id}")));

    private static ModifierModel FindModifier(string typeName) =>
        ModelDb.All.OfType<ModifierModel>().FirstOrDefault(modifier =>
            string.Equals(modifier.GetType().FullName, typeName, StringComparison.Ordinal))
        ?? throw new EngineException(
            $"No modifier with type '{typeName}'. Known: " +
            string.Join(", ", ModelDb.All.OfType<ModifierModel>()
                .Select(modifier => modifier.GetType().FullName).Order(StringComparer.Ordinal)));

    private static CharacterModel FindCharacter(string modelId) =>
        ModelDb.AllCharacters.FirstOrDefault(c => c.Id.ToString() == modelId)
        ?? throw new EngineException(
            $"No character with model id '{modelId}'. Known: " +
            string.Join(", ", ModelDb.AllCharacters.Select(c => c.Id.ToString()).Order()));
}

/// <summary>
/// Which player-progress model a run is generated against. Named rather than
/// assumed, because the game's content generation reads it and a video never shows it.
/// </summary>
public enum PlayerProgress
{
    /// <summary>Everything unlocked and every encounter already seen. The right model
    /// for an experienced player, and the only one that is portable between machines.</summary>
    AllUnlocked,

    /// <summary>A brand-new player. Included because the difference between this and
    /// AllUnlocked is exactly the evidence that progress state matters.</summary>
    NoneUnlocked,

    /// <summary>This machine's own save progress. Diagnostic only; not portable.</summary>
    LocalProfile,
}
