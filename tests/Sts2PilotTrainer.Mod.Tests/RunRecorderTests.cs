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
    /// The engine's budget measures only the engine's own time, however many screens one
    /// decision puts up.
    ///
    /// A card reward whose hook allows a second card closes its first screen and opens
    /// another. The budget used to be started once, before the wait for screens, so the
    /// gap between those two screens started a thirty-second clock that then ran while
    /// the player was still choosing - and a player who took longer than that over the
    /// second card had the decision refused and the whole recording marked broken, for a
    /// decision they made normally.
    ///
    /// Driven here as that sequence: a screen, a gap, a second screen, then the engine
    /// settling. The budget from the gap is expired by hand while the second screen is
    /// up, which is exactly what a slow decision does to it.
    /// </summary>
    [GameFact]
    public async Task ASecondScreenGetsTheEngineBudgetBackRatherThanTheRemainderOfTheFirst()
    {
        // One screen, then none, then one again, then none for the rest.
        int[] screens = [1, 1, 0, 1, 1, 1, 0, 0, 0, 0];
        var polls = 0;
        var budgets = new List<TaskCompletionSource<bool>>();

        var settled = await RunRecorder.WaitForTheEngine(
            () => screens[Math.Min(polls, screens.Length - 1)],
            () => null,
            engineWork: null,
            () => true,
            () =>
            {
                var budget = new TaskCompletionSource<bool>();
                budgets.Add(budget);
                return budget.Task;
            },
            () =>
            {
                polls++;

                // The budget that started in the gap runs out while the player is back
                // at a screen. Discarded rather than consulted, it cannot end the wait.
                if (budgets.Count == 1 && screens[Math.Min(polls, screens.Length - 1)] > 0)
                {
                    budgets[0].TrySetResult(true);
                }

                return Task.CompletedTask;
            },
            spent => $"the engine did not settle {spent}");

        Assert.Null(settled);

        // Two screen-free stretches, so two budgets: the second one is the engine's whole
        // budget counted from the moment the last screen came down.
        Assert.Equal(2, budgets.Count);
    }

    /// <summary>
    /// A wait ends when there is no recording left to wait for, and says why in the
    /// caller's own words.
    ///
    /// Waiting on a screen has no budget by design - a screen is up for as long as
    /// somebody is looking at it, and a clock there costs a player who stepped away
    /// their recording - so the recorder's own lifetime is the only exit. Without it, a
    /// run left to the main menu with a screen up spins a scene-tree timer every poll
    /// for the rest of the session, outliving the recording it was waiting for.
    /// </summary>
    [GameFact]
    public async Task AWaitEndsWhenTheRecordingDoes()
    {
        var polls = 0;
        var recording = true;

        var stopped = await RunRecorder.WaitForTheEngine(
            () => 1,
            () => recording ? null : "the run went to the main menu.",
            engineWork: null,
            () => true,
            () => throw new InvalidOperationException("A screen was up, so no budget should have started."),
            () =>
            {
                recording = false;
                return ++polls > 1
                    ? throw new InvalidOperationException(
                        "The wait polled again after the recording had ended, so it would never stop.")
                    : Task.CompletedTask;
            },
            spent => $"the engine did not settle {spent}");

        Assert.Equal("the run went to the main menu.", stopped);

        // It waited while there was a recording to wait for, and stopped once there
        // was not.
        Assert.Equal(1, polls);
    }

    /// <summary>
    /// And the engine is given its budget once the screens are down - spent, that is a
    /// decision the recorder could not read.
    ///
    /// The sentence names the card screen only where the wait actually stood down for
    /// one. It is not a log line: it goes into the journal and out as the reason the
    /// manifest gives for a broken recording, so a decision that opened no screen must
    /// not be explained by one closing.
    /// </summary>
    [GameFact]
    public async Task AnEngineThatNeverSettlesSpendsItsBudgetAndSaysSo()
    {
        var open = 2;

        var unsettled = await RunRecorder.WaitForTheEngine(
            () => open,
            () => null,
            engineWork: null,
            () => false,
            () => Task.CompletedTask,
            () =>
            {
                if (open > 0) open--;
                return Task.CompletedTask;
            },
            spent => $"the engine did not settle {spent}");

        Assert.Equal("the engine did not settle within 30 seconds of the last card screen closing", unsettled);
        Assert.Equal(0, open);
    }

    /// <summary>
    /// An engine that has not said its own work is finished is not idle, however empty
    /// its queue reads.
    ///
    /// The gate is the engine's own signal - the task a decision handed back, or the
    /// queue's word that it drained - and the two idle ticks are a debounce on top of
    /// it rather than a replacement for it. Without it, an after-sample taken during a
    /// gap in an action's resolution records a wrong after-state and throws nothing.
    /// </summary>
    [GameFact]
    public async Task AnEngineThatHasNotFinishedIsNotIdleHoweverEmptyItLooks()
    {
        var work = new TaskCompletionSource<bool>();
        var polls = 0;

        var settled = await RunRecorder.WaitForTheEngine(
            () => 0,
            () => null,
            work.Task,
            // Idle from the first poll, which is the shape the gate exists for.
            () => true,
            () => new TaskCompletionSource<bool>().Task,
            () =>
            {
                // Long past the two ticks a settle needs, so a wait that ignored the
                // gate would have returned by now.
                if (++polls == 8) work.SetResult(true);
                return Task.CompletedTask;
            },
            spent => $"the engine did not settle {spent}");

        Assert.Null(settled);

        // The engine spoke during the eighth poll, and it took one more to score the
        // second tick - none of the seven before it counted.
        Assert.Equal(9, polls);
    }

    /// <summary>And a decision that opened no card screen is not told one closed.</summary>
    [GameFact]
    public async Task ASpentBudgetOnADecisionWithNoScreenNamesNoScreen()
    {
        var unsettled = await RunRecorder.WaitForTheEngine(
            () => 0,
            () => null,
            engineWork: null,
            () => false,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            spent => $"the engine did not settle {spent}");

        Assert.Equal("the engine did not settle within 30 seconds", unsettled);
    }

    [GameFact]
    public void RecordingIsOnUntilSomebodySaysOtherwise()
    {
        var settings = RunmobileSettings.Default;

        Assert.True(settings.RecordMyRuns);
        Assert.Equal(RunmobileSettings.Schema, settings.SchemaId);
    }
}
