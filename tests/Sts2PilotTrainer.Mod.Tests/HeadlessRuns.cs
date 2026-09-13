using System.Globalization;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// A headless run to measure something against, started the way every headless
/// caller starts one and taken away again afterwards.
///
/// Shared by the tests that ask the real engine a question inside a run - a verb's
/// behaviour, a prompt's offered list - so that each of them stands in the same run
/// and none of them leaves one behind for the next.
/// </summary>
internal static class HeadlessRuns
{
    /// <summary>The seed every one of these runs is of.</summary>
    internal const string Seed = "P1L0TTRA1NER";

    /// <summary>
    /// A run of the probe seed, started the way every headless caller starts one.
    ///
    /// Taken away again afterwards, because this assembly's other tests ask this
    /// process what it is holding - and taken away first as well, because a run some
    /// other test left loaded is one this run would be started on top of, and the
    /// engine then hands out an opening event with no options at all.
    /// </summary>
    internal static void WithARun(Action<GameSession> body)
    {
        EndAnyRun();
        var session = new GameSession();
        try
        {
            session.StartRun(
                Seed, "CHARACTER.IRONCLAD", 0, "standard",
                ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"]);
            body(session);
        }
        finally
        {
            EndAnyRun();
            ScreenStandIns.Current = null;
            ScreenStandIns.ForgetMinigame();
            HeadlessEngine.Forget();
        }
    }

    internal static void EndAnyRun()
    {
        if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
    }

    /// <summary>Neow's first option and the map move into the first fight, the way
    /// the first-fight fixture starts.</summary>
    internal static void EnterTheFirstFight(RunDriver driver, GameSession session)
    {
        driver.EnterFirstRoom();
        driver.Apply(Record(0, ActionVerb.ChooseNeowBlessing, ("option_index", "0")));

        var current = Field(session, "run.map_coord");
        var separator = current.IndexOf('c');
        var row = int.Parse(current.AsSpan(1, separator - 1), CultureInfo.InvariantCulture);
        var column = int.Parse(current.AsSpan(separator + 1), CultureInfo.InvariantCulture);
        var edge = session.CurrentMapTopology().Edges
            .Where(candidate => candidate.FromRow == row && candidate.FromColumn == column)
            .OrderBy(candidate => candidate.ToColumn)
            .First();
        driver.Apply(Record(1, ActionVerb.MapMove,
            ("act", Number(session.RunState.CurrentActIndex)),
            ("row", Number(edge.ToRow)),
            ("column", Number(edge.ToColumn))));

        Assert.Equal("true", Field(session, "combat.in_progress"));
    }

    internal static string Field(GameSession session, string field) =>
        CanonicalStateProjection.Project(session.RunState).Fields[field];

    internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    internal static ActionRecord Record(int seq, ActionVerb verb, params (string Key, string Value)[] args) => new()
    {
        Seq = seq,
        Verb = verb,
        Args = new SortedDictionary<string, string>(
            args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal),
        Source = FactSource.Declared,
    };
}
