using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
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
/// So the claim here is the agreement for the committed fixture rather than either
/// half of it, in two parts: every verb on that walk to the first fight has to be one
/// the driver will issue inside a running game, and every screen answer on it has to be
/// one the client draws a screen for - because the client answers that screen by
/// lighting the recording's card on it and pressing, and a verb it issues without
/// drawing would be committed with nothing to point at. This is a sentinel for the
/// known card-screen shape, not proof that every possible prefix is supported; bundle
/// and relic prompts remain headless-only. Nothing about this needs the game to be
/// installed - it is declarations held against each other - which is the point, because
/// the run that would catch this otherwise is one only a person with the client can
/// make.
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

    /// <summary>
    /// Every screen answer on that walk is one the client draws the screen for.
    ///
    /// The second half of the same agreement, and the half A2 added. Issuing a verb and
    /// being able to show it being issued are two different claims: an answer the client
    /// issues but draws no screen for is a decision the journey would have to commit
    /// with nothing on screen to point at, which is the one thing consider, reveal and
    /// commit exists to prevent. The card screen is the only screen answer that clears
    /// both bars, and this is what would fail if a later walk contained a bundle or a
    /// relic answer instead.
    /// </summary>
    [Fact]
    public void EveryScreenAnswerOnThatWalkIsOneTheClientDrawsAScreenFor()
    {
        var shown = RunDriver.AnswersShownOnTheGamesOwnScreen;

        Assert.All(
            WalkToTheFirstFight().Where(CardScreenAnswers.IsAnAnswer),
            action => Assert.True(
                shown.Contains(action.Verb),
                $"The walk to the recording's first fight answers a screen with a {action.Verb}, which no " +
                "host draws. The journey would have to commit that answer with nothing on screen to point " +
                "at, or refuse it in front of a player who has already watched the decisions before it."));
    }

    /// <summary>
    /// Every screen answer on that walk follows a decision of its own.
    ///
    /// The premise the whole card-screen path rests on, asserted rather than assumed:
    /// an answer answers a screen that the step before it opened. The driver reads its
    /// picks off exactly that window, the transport's caption for a blessing names the
    /// cards from it, and the journey refuses to press the game's own proceed while an
    /// answer is still outstanding - all three are wrong if an answer can lead a walk
    /// or follow another kind of gap.
    /// </summary>
    [Fact]
    public void EveryScreenAnswerOnThatWalkFollowsTheDecisionThatOpenedItsScreen()
    {
        var prefix = WalkToTheFirstFight();

        for (var step = 0; step < prefix.Count; step++)
        {
            if (!CardScreenAnswers.IsAnAnswer(prefix[step])) continue;

            Assert.True(
                step > 0,
                $"Action {prefix[step].Seq} answers a screen and is the first step of the walk, so no " +
                "decision opened that screen.");
        }
    }

    /// <summary>
    /// A screen answer the client draws is one it also issues.
    ///
    /// Two declarations that have to agree, held against each other rather than derived
    /// from one another - because they answer different questions and only happen to
    /// have the same answer today. A verb drawn and not issued would light a card on a
    /// screen and then refuse to press it.
    /// </summary>
    [Fact]
    public void AnAnswerTheClientDrawsIsAnAnswerItIssues()
    {
        var issued = RunDriver.VerbsIssuedInsideARunningGame;

        Assert.All(
            RunDriver.AnswersShownOnTheGamesOwnScreen,
            verb => Assert.True(
                issued.Contains(verb),
                $"The client draws the screen a {verb} is given on and does not issue the verb, so the " +
                "recording's answer would be lit and never pressed."));
    }

    /// <summary>
    /// Every member the recorded-fight journey hangs a patch on is in this build.
    ///
    /// The library module asks this of its own patches because a surface that silently
    /// failed to appear is a feature nobody can find; this module has the same exposure
    /// for a different reason. Its deviation locks are what keep the decisions before
    /// the fight the recording's, and a lock hung on a member a build renamed does not
    /// refuse loudly - it is simply not there, and a player can take a decision the
    /// recording owns. The card screen's own handler is the one that made this worth
    /// asserting: it is named as a string because the method is protected.
    /// </summary>
    [GameFact]
    public void EveryMemberTheRecordedFightJourneyHangsOnIsInThisBuild()
    {
        _ = EngineHost.StartupPhase();

        // Named rather than counted, because an empty refusal list is also what a walk
        // that found nothing to check returns.
        Assert.Equal(
            [
                "EventSynchronizer.ChooseLocalOption",
                "NDeckCardSelectScreen.OnCardClicked",
                "NDeckTransformSelectScreen.OnCardClicked",
                "NGame.ReturnToMainMenu",
                "NRewardButton.OnRelease",
                "NRewardsScreen.OnProceedButtonPressed",
                "RunManager.CleanUp",
                "RunManager.EnterMapCoord",
            ],
            PatchTargets.Targets(RecordedFightModule.PatchClasses).Order(StringComparer.Ordinal));
        Assert.Empty(PatchTargets.Unresolvable(RecordedFightModule.PatchClasses));
    }
}
