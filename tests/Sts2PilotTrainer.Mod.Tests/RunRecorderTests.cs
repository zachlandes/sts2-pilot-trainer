using HarmonyLib;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What can be established about the recorder on this machine without playing a run.
///
/// Three questions, and all three are about drift rather than about logic - the logic
/// is <see cref="RunCapture"/>'s and is tested without the game at all. Does this build
/// still have every member the recorder attaches to; does the set of decisions it can
/// write down still equal the set the driver can replay; and is the recorder still
/// inert while a trainer run is live. A recorder that quietly stopped seeing one kind
/// of decision would write a history missing decisions, and a history missing
/// decisions replays perfectly into a different run.
/// </summary>
public sealed class RunRecorderTests
{
    [GameFact]
    public void EveryMethodTheRecorderAttachesToExistsOnThisBuild()
    {
        // Asked of the module, because that is the thing the shell asks before it
        // installs anything: a build the recorder cannot watch completely is one it
        // declines to watch at all.
        Assert.Null(RecorderModule.Instance.Refusal);
        Assert.True(RecorderModule.Instance.Enabled);
    }

    [GameFact]
    public void TheRecorderIsOneOfTheModulesTheShellInstallsAndDrawsNothing()
    {
        Assert.Contains(RunmobileMod.Modules, module => ReferenceEquals(module, RecorderModule.Instance));
        Assert.Empty(RecorderModule.Instance.MenuCards);
    }

    /// <summary>
    /// The recorder writes down exactly the decisions the driver can replay.
    ///
    /// The two halves meet at <see cref="EngineCommands"/>: the driver calls those
    /// members to make a recorded decision and the recorder watches the same members
    /// being called. A verb one side has and the other does not is either a recording
    /// nothing can replay or a replay of a decision nothing can record, and both are
    /// silent until somebody tries.
    /// </summary>
    [GameFact]
    public void TheRecordersDecisionsAreTheOnesTheDriverCanReplay()
    {
        var mapped = EngineCommands.All.Select(command => command.Verb).ToHashSet();

        Assert.Equal(mapped.Order(), RunRecorder.RecordedVerbs.Order());
    }

    /// <summary>
    /// And every one of them has something in this build actually watching it.
    ///
    /// Declaring a verb is not recording it. The recorder watched
    /// <see cref="ActionVerb.DiscardPotion"/> only through the fight observer for a
    /// while, so a potion thrown away on the map to make room for a reward left a
    /// history missing a decision - true in every value, and it replays into a
    /// different run. Nothing said so until a <c>gate</c> failed after the run was over.
    ///
    /// What is asked here is the question that catches that: for each verb, is there a
    /// patch on the very member <see cref="EngineCommands"/> names, resolved through
    /// Harmony on this build. The three exceptions are listed with the reason a patch
    /// there would be wrong rather than missing, and a verb that leaves this list
    /// without gaining a patch fails naming itself.
    /// </summary>
    [GameFact]
    public void EveryDecisionTheRecorderDeclaresIsOneSomethingInThisBuildWatches()
    {
        var patched = RunRecorder.PatchClasses
            .SelectMany(patchClass => patchClass
                .GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
                .OfType<HarmonyPatch>()
                .Select(attribute => attribute.info))
            .Select(Watched)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var unwatched = RunRecorder.RecordedVerbs
            .Where(verb => !WatchedWithoutAPatch.Contains(verb))
            .Where(verb => !patched.Contains(Member(verb)))
            .ToList();

        Assert.True(
            unwatched.Count == 0,
            $"Declared and unwatched outside a fight: {string.Join(", ", unwatched)}. Each of these can happen " +
            "with no fight in progress, and the fight observer is attached only while there is one.");

        // The other direction, so an exception cannot outlive its reason: a verb that
        // has grown a patch of its own is no longer one of them.
        Assert.DoesNotContain(WatchedWithoutAPatch, verb => patched.Contains(Member(verb)));
    }

    /// <summary>
    /// The three decisions no patch on their engine member watches, and why.
    ///
    /// <see cref="ActionVerb.PlayCard"/> and <see cref="ActionVerb.EndTurn"/> exist only
    /// inside a fight, where the action executor runs them and
    /// <see cref="PlayerFightObserver"/> is attached for the whole of it; a patch as
    /// well would record each of them twice.
    /// <see cref="ActionVerb.SelectCardFromScreen"/> is answered rather than commanded -
    /// its engine member is <c>ICardSelector</c>, which is the arbiter's own seam for
    /// the answer a player's client gives - so what the recorder watches is the two
    /// screens that ask.
    /// </summary>
    private static readonly IReadOnlyList<ActionVerb> WatchedWithoutAPatch =
        [ActionVerb.PlayCard, ActionVerb.EndTurn, ActionVerb.SelectCardFromScreen];

    /// <summary>The member <see cref="EngineCommands"/> says this verb goes through.</summary>
    private static string Member(ActionVerb verb)
    {
        var command = EngineCommands.All.First(candidate => candidate.Verb == verb);
        return $"{command.Type.FullName}.{command.Member}";
    }

    /// <summary>The member a patch attaches to, named the same way, or null where this
    /// build has nothing to attach it to - which <c>RecorderModule.Refusal</c> is what
    /// reports.</summary>
    private static string? Watched(HarmonyMethod patch)
    {
        if (patch.declaringType is null) return null;

        if (patch.methodType == MethodType.Constructor || patch.methodName is null)
        {
            return AccessTools.Constructor(patch.declaringType, patch.argumentTypes) is null
                ? null
                : $"{patch.declaringType.FullName}.{EngineCommands.ConstructorMember}";
        }

        return AccessTools.Method(patch.declaringType, patch.methodName, patch.argumentTypes) is null
            ? null
            : $"{patch.declaringType.FullName}.{patch.methodName}";
    }

    [GameFact]
    public void TheRecorderStaysOutOfTheWayOfATrainerRun()
    {
        // The barrier is raised for the whole of a trainer run, which is this mod's own
        // construction rather than the player's: recording it would publish somebody
        // else's recording back as the player's own. Checked by doing it, because the
        // interaction is between two global pieces of state and a comment asserting it
        // is not a test.
        Assert.Null(RunRecorder.Active);
        ProfileWriteBarrier.Raise();
        try
        {
            RunRecorder.NoticeRun();
            Assert.Null(RunRecorder.Active);
        }
        finally
        {
            ProfileWriteBarrier.Lower();
        }
    }

    /// <summary>
    /// A card screen gives back the count it took, however its own task ends.
    ///
    /// The count is what keeps a settle from reading a run in the middle of a decision,
    /// and it is static because the screens are: one game, one person looking at it. So
    /// a screen counted on the way up and not on the way down leaves it above zero for
    /// the rest of the session, and the next run's settle waits for a screen nobody is
    /// looking at. It used to be taken in a Harmony prefix guarded on a recorder being
    /// active and given back in a postfix guarded the same way, which drifts exactly
    /// when a run is torn down between them; it is one try/finally around the screen's
    /// own task now, so the two cannot decide differently.
    ///
    /// Driven through that wrapper rather than asserted about it, because what has to
    /// hold is the count at the end and not the shape of the code that keeps it.
    /// </summary>
    [GameFact]
    public async Task ACardScreenGivesBackTheCountItTookHoweverItEnds()
    {
        var before = RunRecorder.ScreensOpen;

        var answered = new TaskCompletionSource<int?>();
        var watching = RunRecorder.CardRewardAnswer.Observe(answered.Task);
        Assert.Equal(before + 1, RunRecorder.ScreensOpen);

        answered.SetResult(1);
        await watching;
        Assert.Equal(before, RunRecorder.ScreensOpen);

        // A screen whose task faults is still a screen that has come down. This is the
        // reward screen's own tear-down: NCardRewardSelectionScreen._ExitTree faults its
        // completion source when the run goes away under it.
        var faulted = new TaskCompletionSource<int?>();
        var stranded = RunRecorder.CardRewardAnswer.Observe(faulted.Task);
        Assert.Equal(before + 1, RunRecorder.ScreensOpen);

        faulted.SetException(new InvalidOperationException("the run went away under the screen"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => stranded);
        Assert.Equal(before, RunRecorder.ScreensOpen);

        // And so is one that is cancelled, which is what the grid screen's own
        // _ExitTree does. Between them these are every way a screen ends, which is what
        // says the count cannot go stale and why nothing resets it.
        var cancelled = new TaskCompletionSource<int?>();
        var dropped = RunRecorder.WhileOnScreen(cancelled.Task);
        Assert.Equal(before + 1, RunRecorder.ScreensOpen);

        cancelled.SetCanceled();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dropped);
        Assert.Equal(before, RunRecorder.ScreensOpen);
    }

    /// <summary>
    /// The count never dips below where it started, however the screens interleave.
    ///
    /// It cannot: <c>WhileOnScreen</c> is the only thing that touches the counter and it
    /// takes and gives back in one try/finally, so there is no bare decrement for a
    /// caller to reach. That is the point of the shape - a reset and a stray decrement
    /// once left it at -1, and a negative count silently satisfies the wait that keeps a
    /// reading off a half-made decision.
    /// </summary>
    [GameFact]
    public async Task TheScreenCountNeverGoesBelowWhereItStarted()
    {
        var before = RunRecorder.ScreensOpen;
        var floor = before;

        var first = new TaskCompletionSource<int?>();
        var second = new TaskCompletionSource<int?>();
        var outer = RunRecorder.WhileOnScreen(first.Task);
        var inner = RunRecorder.WhileOnScreen(second.Task);
        floor = Math.Min(floor, RunRecorder.ScreensOpen);

        second.SetResult(0);
        await inner;
        floor = Math.Min(floor, RunRecorder.ScreensOpen);

        first.SetResult(1);
        await outer;
        floor = Math.Min(floor, RunRecorder.ScreensOpen);

        Assert.Equal(before, floor);
        Assert.Equal(before, RunRecorder.ScreensOpen);
    }

    /// <summary>
    /// A wait on a screen ends when there is no recording left to wait for.
    ///
    /// It has no budget by design - a screen is up for as long as somebody is looking at
    /// it, and a clock there costs a player who stepped away their recording - so the
    /// recorder's own lifetime is the only exit. Without it, a run left to the main menu
    /// with a screen up spins a scene-tree timer every poll for the rest of the session,
    /// outliving the recording it was waiting for.
    /// </summary>
    [GameFact]
    public async Task AScreenWaitEndsWhenTheRecordingDoes()
    {
        var polls = 0;
        var recording = true;

        var refusal = await RunRecorder.WaitForScreens(
            () => 1,
            () => !recording,
            () =>
            {
                recording = false;
                return ++polls > 1
                    ? throw new InvalidOperationException(
                        "The wait polled again after the recording had ended, so it would never stop.")
                    : Task.CompletedTask;
            });

        Assert.NotNull(refusal);
        Assert.Contains("1 card screen(s) still open", refusal, StringComparison.Ordinal);

        // It waited while there was a recording to wait for, and stopped once there
        // was not.
        Assert.Equal(1, polls);
    }

    /// <summary>And a screen that comes down lets the settle carry on.</summary>
    [GameFact]
    public async Task AScreenThatComesDownLetsTheWaitFinish()
    {
        var open = 2;

        Assert.Null(await RunRecorder.WaitForScreens(
            () => open,
            () => false,
            () =>
            {
                open--;
                return Task.CompletedTask;
            }));
        Assert.Equal(0, open);
    }

    [GameFact]
    public void RecordingIsOnUntilSomebodySaysOtherwise()
    {
        var settings = RunmobileSettings.Default;

        Assert.True(settings.RecordMyRuns);
        Assert.Equal(RunmobileSettings.Schema, settings.SchemaId);
    }
}
