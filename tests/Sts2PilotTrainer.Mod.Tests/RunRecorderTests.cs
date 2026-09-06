using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
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
            .Where(verb => !(WatchedDeeperThanTheDriverCalls.TryGetValue(verb, out var deeper)
                && patched.Contains(OnTheSameType(verb, deeper))))
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
    /// The five decisions no patch on their engine member watches, and why.
    ///
    /// <see cref="ActionVerb.PlayCard"/>, <see cref="ActionVerb.EndTurn"/> and
    /// <see cref="ActionVerb.UndoEndTurn"/> exist only inside a fight, where the action
    /// executor runs them and <see cref="PlayerFightObserver"/> is attached for the
    /// whole of it; a patch as well would record each of them twice.
    /// <see cref="ActionVerb.SelectCardFromScreen"/> and
    /// <see cref="ActionVerb.TakeCardRewardAlternative"/> are answered rather than
    /// commanded - their engine member is <c>ICardSelector</c>, which is the arbiter's
    /// own seam for the answer a player's client gives - so what the recorder watches
    /// is the two screens that ask.
    /// </summary>
    private static readonly IReadOnlyList<ActionVerb> WatchedWithoutAPatch =
    [
        ActionVerb.PlayCard,
        ActionVerb.EndTurn,
        ActionVerb.UndoEndTurn,
        ActionVerb.SelectCardFromScreen,
        ActionVerb.TakeCardRewardAlternative,
    ];

    /// <summary>
    /// The decisions the recorder watches deeper than the member the driver calls, and
    /// where instead.
    ///
    /// One entry, and it is here because "the driver calls it" and "the client goes
    /// through it" turned out to be different claims.
    /// <see cref="ActionVerb.SkipRewards"/> is replayed by calling
    /// <c>SkipLocalRewardsSet</c>, and the retail client never calls that for a
    /// post-combat loot screen: the screen is terminal, so Skip takes
    /// <c>NRewardsScreen</c>'s proceed branch and the leftovers are declined by
    /// <c>BeforeLeavingRoom</c> on the way out. Both reach the private
    /// <c>SkipRewardsSet</c>, so the recorder watches that and sees the driver's own
    /// call as well.
    ///
    /// Listed rather than left implicit because it is the one place the two halves do
    /// not meet at the same member, and an unexplained divergence is how the next one
    /// gets waved through. A recording that missed a skip did not lose a detail: the
    /// arbiter refuses the following map move, and the run stops reproducing there.
    /// </summary>
    private static readonly IReadOnlyDictionary<ActionVerb, string> WatchedDeeperThanTheDriverCalls =
        new Dictionary<ActionVerb, string>
        {
            [ActionVerb.SkipRewards] = RunRecorder.SkipRewardsSetMember,
        };

    /// <summary>A different member on the same engine type the driver calls into, named
    /// the way <see cref="Watched"/> names one.</summary>
    private static string OnTheSameType(ActionVerb verb, string member) =>
        $"{EngineCommands.All.First(command => command.Verb == verb).Type.FullName}.{member}";

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

    /// <summary>
    /// A prompt the engine answers for itself holds nothing open, so no later answer
    /// can be read as its.
    ///
    /// The bundle screen has no seam of its own, so what the recorder holds between the
    /// prompt and the answer is the prompt itself. It used to hold it whatever the call
    /// did with it - and two of that call's branches answer nobody: a fight that is
    /// ending, and a prompt with no bundles in it. Scroll Boxes offering bundles as a
    /// combat ends left the prompt open, and the next ordinary card screen the player
    /// answered was written down as the bundle they picked. Every value in that
    /// recording is true and the decision it states was never made.
    /// </summary>
    [GameFact]
    public void APromptTheEngineAnswersItselfHoldsNothingOpenForSomebodyElsesAnswer()
    {
        IReadOnlyList<IReadOnlyList<CardModel>> bundles = [[], []];

        RunRecorder.BundleScreen.Opened(bundles, combatIsEnding: true);
        Assert.Null(RunRecorder.BundleScreen.Open);

        RunRecorder.BundleScreen.Opened([], combatIsEnding: false);
        Assert.Null(RunRecorder.BundleScreen.Open);

        RunRecorder.BundleScreen.Opened(bundles, combatIsEnding: false);
        Assert.Same(bundles, RunRecorder.BundleScreen.Open);

        RunRecorder.BundleScreen.Open = null;
        RunRecorder.RelicScreen.Opened([]);
        Assert.Null(RunRecorder.RelicScreen.Open);
    }

    /// <summary>
    /// And a prompt whose call has settled holds nothing open either, whatever that
    /// call settled into: the answer is read while the call is still waiting for it.
    /// </summary>
    [GameFact]
    public void APromptWhoseCallHasSettledIsNoLongerOpen()
    {
        var answering = new TaskCompletionSource<IEnumerable<CardModel>>();
        IReadOnlyList<IReadOnlyList<CardModel>> bundles = [[]];

        RunRecorder.BundleScreen.Opened(bundles, combatIsEnding: false);
        RunRecorder.BundleScreen.After(answering.Task);

        // Still the player's question to answer.
        Assert.Same(bundles, RunRecorder.BundleScreen.Open);

        answering.SetResult([]);

        Assert.Null(RunRecorder.BundleScreen.Open);
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

    /// <summary>
    /// A patch on a base method covers every subclass that overrides it, and none that
    /// shadows it.
    ///
    /// C# binds a shadowing method statically, so a subclass that declares its own
    /// method of the same name - rather than overriding one - takes the call and the
    /// base's patch never fires. Not hypothetical: <c>MerchantCardRemovalEntry</c>
    /// declares a three-argument <c>OnTryPurchaseWrapper</c> beside the base's
    /// non-virtual two-argument one, and until this existed a player could pay gold,
    /// lose a card, and have the recording say nothing about either. The recording
    /// stayed structurally valid and simply would not reproduce.
    ///
    /// Asked of every patch rather than of that one, because the shape is the defect and
    /// naming the instance is how the next one is missed. An override is fine and is
    /// what <see cref="MethodInfo.GetBaseDefinition"/> distinguishes: it reports the
    /// base's method for an override and the method itself for a new slot.
    /// </summary>
    [GameFact]
    public void APatchOnABaseMethodCoversEverySubclassThatShadowsIt()
    {
        // Touched first so the module has installed the game's assembly resolution:
        // reading a Harmony attribute resolves the engine type it names, and this test
        // sorts ahead of every other one that would have done it.
        Assert.Null(RecorderModule.Instance.Refusal);

        var targets = PatchTargets().ToList();
        var patched = targets
            .Select(target => AccessTools.Method(target.Type, target.Method, target.Arguments))
            .Where(method => method is not null)
            .ToHashSet();

        var unwatched = new List<string>();
        foreach (var target in targets)
        {
            foreach (var subclass in Subclasses(target.Type))
            {
                var shadow = subclass.GetMethod(
                    target.Method,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);

                if (shadow is null) continue;
                if (!ReferenceEquals(shadow.GetBaseDefinition(), shadow)) continue;
                if (patched.Contains(shadow)) continue;

                unwatched.Add($"{subclass.Name}.{target.Method}");
            }
        }

        Assert.True(
            unwatched.Count == 0,
            "These shadow a patched method and nothing patches them, so the decisions that go through them " +
            $"are recorded nowhere: {string.Join(", ", unwatched)}.");
    }

    /// <summary>
    /// The skip patch watches the funnel every declined reward set reaches.
    ///
    /// <see cref="WatchedDeeperThanTheDriverCalls"/> says why it is not the member the
    /// driver calls. What is checked here is that the funnel and both of its entry
    /// points still exist on this build, because the whole reason for watching the
    /// deeper member is that those two entry points are different paths into it.
    /// </summary>
    [GameFact]
    public void TheSkipPatchWatchesTheFunnelEveryDeclinedRewardSetReaches()
    {
        Assert.Null(RecorderModule.Instance.Refusal);

        var synchronizer = EngineCommands.All
            .First(command => command.Verb == ActionVerb.SkipRewards).Type;

        Assert.NotNull(AccessTools.Method(synchronizer, RunRecorder.SkipRewardsSetMember));
        Assert.NotNull(AccessTools.Method(synchronizer, "SkipLocalRewardsSet"));
        Assert.NotNull(AccessTools.Method(synchronizer, "BeforeLeavingRoom"));

        Assert.Contains(
            PatchTargets(),
            target => target.Type == synchronizer && target.Method == RunRecorder.SkipRewardsSetMember);
    }

    /// <summary>
    /// One place decides whether a fight is the player's to act in.
    ///
    /// The recorder's settle and the recorded-fight host ask the same question and have
    /// to get the same answer. A room entry completes before the opening hand is dealt,
    /// and a reading taken in between describes a state with no hand and no energy that
    /// no player ever acted from. The host learned that in the client and fixed it
    /// locally; the recorder had it too and anchored every combat-start boundary it
    /// wrote to that instant, so nothing it recorded could reproduce. Two copies is what
    /// allowed that, so there is one.
    /// </summary>
    [GameFact]
    public void OnePlaceDecidesWhetherAFightIsReadyForThePlayer()
    {
        // No run is in progress in a test process, so the ambient question answers false
        // rather than throwing - which is what the settle relies on when a fight has
        // ended under it.
        Assert.False(LiveRun.InCombat);
        Assert.False(LiveRun.ReadyForThePlayer());
    }

    private static IEnumerable<(Type Type, string Method, Type[]? Arguments)> PatchTargets() =>
        RunRecorder.PatchClasses
            .SelectMany(patchClass =>
                patchClass.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).OfType<HarmonyPatch>())
            .Select(attribute => attribute.info)
            .Where(info => info.declaringType is not null && info.methodName is not null)
            .Select(info => (Type: info.declaringType!, Method: info.methodName!,
                Arguments: (Type[]?)info.argumentTypes));

    /// <summary>Every type in the same assembly that derives from this one.</summary>
    private static IEnumerable<Type> Subclasses(Type type)
    {
        Type[] all;
        try
        {
            all = type.Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            all = [.. ex.Types.Where(loaded => loaded is not null).Select(loaded => loaded!)];
        }

        return all.Where(candidate => candidate != type && type.IsAssignableFrom(candidate));
    }
}

