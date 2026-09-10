using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The table that says how the retail client issues each decision it issues, checked
/// against the build it claims to describe.
///
/// The client half of what <c>EngineCommandTableTests</c> holds for the engine half.
/// Three things can rot it and none is visible by reading it: the game can rename a
/// screen handler or a member the deviation lock hangs on, which turns a lock into
/// nothing rather than into a refusal; this repository can add a verb to
/// <c>RetailPlayback.Verbs</c> without saying how the client issues it, which is a row
/// the library offers and the journey cannot make; and a patch can be hung on a member
/// nobody accounted for. <c>Verify</c> answers all three, and this runs it.
/// </summary>
public sealed class ClientCommandTableTests
{
    [GameFact]
    public void EveryHandlerAndLockExistsOnThisBuildAndEveryVerbIsAccountedFor()
    {
        _ = EngineHost.StartupPhase();

        Assert.Empty(ClientCommands.Verify());
    }

    /// <summary>The table's verbs are exactly the client's verbs, in both directions,
    /// read off the one declaration the driver enforces and the library reads.</summary>
    [GameFact]
    public void TheTableNamesExactlyTheVerbsTheClientIssues()
    {
        _ = EngineHost.StartupPhase();

        Assert.Equal(
            RetailPlayback.Verbs.Order(),
            ClientCommands.All.Select(command => command.Verb).Order());
        Assert.All(RetailPlayback.Verbs, verb => Assert.NotNull(ClientCommands.For(verb)));
        Assert.Null(ClientCommands.For(ActionVerb.PlayCard));
    }

    /// <summary>Every row reaches the engine command the engine table maps the verb
    /// onto, never a second one.</summary>
    [GameFact]
    public void EveryRowReachesTheEngineCommandForItsVerb()
    {
        _ = EngineHost.StartupPhase();

        Assert.All(ClientCommands.All, command =>
            Assert.Same(EngineCommands.For(command.Verb), command.Engine));
    }

    /// <summary>
    /// Every member the recorded-fight journey hangs a patch on is a row's lock or is
    /// excused by name, and every lock is a member of this build. This replaces a
    /// hand-written list of those members: the list is now derived from the rows, so a
    /// verb that gains a lock gains it in the table.
    /// </summary>
    [GameFact]
    public void EveryPatchTheJourneyHangsIsARowsLockOrExcused()
    {
        _ = EngineHost.StartupPhase();

        var locks = ClientCommands.All
            .SelectMany(command => command.Locks)
            .Select(target => $"{target.Type.Name}.{target.Member}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var patched = PatchTargets.Targets(RecordedFightModule.PatchClasses);

        Assert.Equal(
            [
                "EventSynchronizer.ChooseLocalOption",
                "NDeckCardSelectScreen.OnCardClicked",
                "NDeckTransformSelectScreen.OnCardClicked",
                "RunManager.EnterMapCoord",
            ],
            locks);
        Assert.All(locks, target => Assert.Contains(target, patched));
        Assert.Empty(PatchTargets.Unresolvable(RecordedFightModule.PatchClasses));
    }

    /// <summary>The module refuses on the table the way the recorder refuses on a
    /// renamed member: a verified table is a precondition of offering the journey.</summary>
    [GameFact]
    public void TheRecordedFightModuleIsEnabledOnlyWithAVerifiedTable()
    {
        _ = EngineHost.StartupPhase();

        Assert.True(RecordedFightModule.Instance.Enabled, RecordedFightModule.Instance.Refusal);
    }
}
