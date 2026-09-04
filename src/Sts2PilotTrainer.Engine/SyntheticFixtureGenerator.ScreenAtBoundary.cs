using System.Globalization;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The journey that stops at the first turn a decision begins.
///
/// Every other history here answers its card screens well inside a fight, so nothing
/// exercises the case where the screen belongs to the action a boundary is named
/// after. A turn boundary is named after the end of the turn before it, and a power
/// that acts at the start of a turn opens its screen inside that very call - so the
/// recorded answer sits after the action a prefix replay stops at, and after the last
/// step of a plan that walks to it. Both have to hand that action its own selections,
/// and neither can be shown to against a history that never reaches the case.
///
/// It is a narrow shape on v0.111.0. What opens a card screen at the start of a
/// player's turn is Tools of the Trade, Tyranny, Entropy or Foregone Conclusion - each
/// a rare power card - or one of three relics, two of which are in the event pool this
/// route will not enter and the third of which asks for a selection with no upper
/// bound, which no recorded history can answer. So the journey does not name a card at
/// all: it walks the act by the act journey's own rules and stops at the first fight
/// whose turn began with a decision, and refuses to emit anything if no turn did.
/// </summary>
public static partial class SyntheticFixtureGenerator
{
    /// <summary>
    /// The seed this journey walks, chosen the same way the act seed was: by generating
    /// runs through the real engine and reading what they produced, rather than by
    /// assuming anything about it.
    ///
    /// On this one the run's own decisions put a power that acts at the start of a turn
    /// into the deck, and the fight after that lasts long enough to end a turn holding
    /// it. Nothing here depends on that being true - the journey refuses below if the
    /// act went by without a turn that began with a decision.
    /// </summary>
    private const string ScreenAtBoundarySeed = "S00126";

    /// <summary>The Regent's, whose pool holds two of the four cards that open a screen
    /// at the start of a turn.</summary>
    private const string ScreenAtBoundaryCharacter = "CHARACTER.REGENT";

    private static ReplayManifest GenerateScreenAtBoundary()
    {
        var identity = RequireSupportedBuild();
        string[] acts = ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"];
        var session = new GameSession();
        session.StartRun(ScreenAtBoundarySeed, ScreenAtBoundaryCharacter, 0, "standard", acts);
        using var driver = new RunDriver(session);
        driver.ImproviseUnrecordedCardSelections();
        driver.EnterFirstRoom();

        var actions = new List<ActionRecord>();
        var checkpoints = new List<Checkpoint>();

        Apply(driver, actions, ActionVerb.ChooseNeowBlessing, ("option_index", "0"));

        foreach (var next in PlanRoute(session))
        {
            Apply(driver, actions, ActionVerb.MapMove,
                ("act", session.RunState.CurrentActIndex.ToString(CultureInfo.InvariantCulture)),
                ("row", next.coord.row.ToString(CultureInfo.InvariantCulture)),
                ("column", next.coord.col.ToString(CultureInfo.InvariantCulture)));

            checkpoints.Add(Capture(
                $"floor-{Field(session, "run.total_floor")}-entry", actions[^1].Seq, session,
                "run.total_floor", "run.map_coord", "player.hp", "player.gold"));

            HandleRoom(driver, session, actions, checkpoints, next.PointType);

            // The whole fight is kept: the comparison contract is defined over one that
            // finished, and a history cut off inside the turn that opened the screen
            // would prove the boundary and nothing either side of it.
            if (ATurnBeganWithADecision(actions)) break;
        }

        if (!ATurnBeganWithADecision(actions))
        {
            throw new EngineException(
                "This journey walked its act without a turn that began with a decision. A power that acts at " +
                "the start of a turn is what produces one, so either this run never obtained one or no fight " +
                "after it lasted a second turn. Refusing to emit a fixture that does not contain the case it " +
                "exists for.");
        }

        return new ReplayManifest
        {
            RunId = "synthetic-v0111-screen-at-boundary",
            Environment = new EnvironmentIdentity
            {
                BuildVersion = Fact<string>.Declared(identity.BuildVersion),
                BuildDateUtc = Fact<string>.Declared(identity.BuildDateUtc),
                GameMode = Fact<string>.Declared("standard"),
                Seed = Fact<string>.Declared(ScreenAtBoundarySeed),
                ContentHash = Fact<string>.Declared(identity.ContentHash),
                Ascension = Fact<int>.Declared(0),
                Unlocks = Fact<UnlockRequirement>.Declared(UnlockRequirement.Complete(
                    "Generated by this arbiter against UnlockState.all, so the requirement is a property of " +
                    "how the fixture was produced rather than a claim about any player.")),
                Character = Fact<string>.Declared(ScreenAtBoundaryCharacter),
                Acts = Fact<IReadOnlyList<string>>.Declared(acts),
                Mods = Fact<ModEnvironment>.Declared(new ModEnvironment
                {
                    Name = "vanilla-headless-v0.111.0",
                    ReportedCount = 0,
                    Mods = [],
                }),
            },
            Source = new SourceProvenance
            {
                Kind = "synthetic-engine",
                Synthetic = new SyntheticSource
                {
                    FixtureId = "v0111-screen-at-boundary",
                    FixtureVersion = FixtureVersion,
                    Generator = "sts2-pilot-trainer",
                    GeneratedBuild = identity.BuildVersion,
                },
                ExtractionMethod = "engine-generated",
                Coverage =
                    "Mechanically generated act route, stopped after the first fight in which a turn began " +
                    "with a card-selection screen the recording had to answer.",
            },
            Actions = actions,
            Checkpoints = checkpoints,
        };
    }

    /// <summary>Whether some turn of this history began with a decision: an end of turn
    /// whose own screen the next action answers.</summary>
    private static bool ATurnBeganWithADecision(IReadOnlyList<ActionRecord> actions) =>
        Enumerable.Range(0, Math.Max(actions.Count - 1, 0)).Any(index =>
            actions[index].Verb == ActionVerb.EndTurn &&
            actions[index + 1].Verb == ActionVerb.SelectCardFromScreen);
}
