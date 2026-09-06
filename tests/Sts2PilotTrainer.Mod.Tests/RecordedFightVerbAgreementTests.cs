using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The two hosts agree about what a recording's walk to its first fight contains.
///
/// Written from a defect that had already shipped past a green suite. The headless
/// driver learned to answer the card screen an opening blessing opens - correctly, and
/// with the manifest's own selections - while the verbs the in-game host issues stayed
/// as they were. A recording whose blessing removes, transforms or upgrades a card then
/// replayed, verified and passed the publication gate, and aborted in the retail client
/// at the step after the player had watched the blessing being made. The failure did
/// not go away when the arbiter learned the verb; it moved, from an honest refusal
/// before anybody had invested anything to the worst place a refusal can happen.
///
/// So the claim here is the agreement itself rather than either half of it: every verb
/// the walk to the recording's first fight can contain has to be one the driver will
/// issue inside a running game. Nothing about this needs the game to be installed - it
/// is two declarations held against each other - which is the point, because the run
/// that would catch it otherwise is one only a person with the client can make.
/// </summary>
public sealed class RecordedFightVerbAgreementTests
{
    /// <summary>
    /// The committed history whose opening blessing opens a card screen, which is the
    /// shape the defect needed. Asserted rather than assumed: a fixture regenerated
    /// without that blessing would leave every test below passing over a walk that
    /// never meets one.
    /// </summary>
    private static IReadOnlyList<ActionRecord> WalkToTheFirstFight()
    {
        var manifest = ManifestJson.Deserialize(File.ReadAllText(Arbiter.WholeAct));
        var prefix = RecordedFightPlan.For(manifest).PrefixActions;

        Assert.Contains(prefix, CardScreenAnswers.IsAnAnswer);
        Assert.Equal(ActionVerb.ChooseNeowBlessing, prefix[0].Verb);
        return prefix;
    }

    [Fact]
    public void EveryVerbOnTheWalkToTheFirstFightIsOneTheClientIssues()
    {
        var issued = RunDriver.VerbsIssuedInsideARunningGame;

        Assert.All(
            WalkToTheFirstFight(),
            action => Assert.True(
                issued.Contains(action.Verb),
                $"The walk to the recording's first fight contains a {action.Verb}, which the driver refuses " +
                "inside a running game. A recording like this one verifies headlessly and then aborts in the " +
                "client, in front of a player who has already watched the decisions before it."));
    }

    [Fact]
    public void EveryQueuedScreenAnswerIsOneTheClientConfirms()
    {
        Assert.All(
            CardScreenAnswers.Verbs,
            verb => Assert.Contains(verb, RunDriver.VerbsIssuedInsideARunningGame));
    }

    /// <summary>
    /// A card selection is executed and never shown, so it is not one of the decisions
    /// the transport counts through. The distinction only exists because the engine
    /// answers such a screen inside the call that opens it: there is nothing left on
    /// the game's own screen to point at by the time the step runs.
    /// </summary>
    [Fact]
    public void ACardSelectionIsAnAnswerRatherThanADecisionOfItsOwn()
    {
        var prefix = WalkToTheFirstFight();

        Assert.Equal(
            prefix.Count - 1,
            prefix.Count(action => !CardScreenAnswers.IsAnAnswer(action)));
        Assert.DoesNotContain(
            prefix.Where(CardScreenAnswers.IsAnAnswer),
            action => action.Verb != ActionVerb.SelectCardFromScreen);
    }
}
