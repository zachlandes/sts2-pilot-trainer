using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.AutoSlay.Handlers;
using MegaCrit.Sts2.Core.AutoSlay.Handlers.Rooms;
using MegaCrit.Sts2.Core.AutoSlay.Handlers.Screens;
using MegaCrit.Sts2.Core.AutoSlay.Helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The night: standard singleplayer runs started through the client's own run start
/// and played through what a click issues, with the recorder attached the way it
/// attaches to any run, until the plan's count or its deadline.
///
/// The shape is the game's own autoplayer's - a loop over the current room's type
/// and the overlay stack's top - with three things the autoplayer does replaced,
/// because each makes a run no replay reproduces: its combat handler applies powers
/// through <c>PowerCmd</c> and plays through <c>CardCmd.AutoPlay</c>, its event
/// handler kills an event's enemies through <c>CreatureCmd.Kill</c>, and its card
/// selector answers every prompt engine-side through <c>CardSelectCmd.UseSelector</c>.
/// The fight is played here through <c>CardModel.TryManualPlay</c>, an
/// <c>EndPlayerTurnAction</c> enqueued and <c>PotionModel.EnqueueManualUse</c> - the
/// members the engine table maps a click onto - under <see cref="SurvivalPlayRule"/>'s
/// measured line, the one the whole-act walk plays;
/// the event room presses the game's own option buttons and hands a fight inside an
/// event to the same fight loop; and the hand prompt and the in-fight card grid are
/// pressed the way a person presses them. Everything else is the game's own handler,
/// constructed as its autoplayer constructs it and driven with its non-interactive
/// mode off, which the soak probe measured to record and replay at parity.
///
/// Every decision is paced on <see cref="RunRecorder.Settled"/>: the recorder has read
/// after every decision it announced and, in a fight, its observer is watching. The
/// recorder now refuses a decision made inside that window rather than dropping it,
/// and this driver never makes one.
///
/// The event loop is state-driven. After every option press the overlays are drained
/// and the next state is read and classified - another choice, a fight, a screen,
/// the event over and the map open, or something this driver does not know - and an
/// unknown state, room or screen is logged with the floor it was met on and the run
/// given up through the game's own pause menu, so the recording finishes. Nothing is
/// guessed through.
///
/// The bounds are the game's own: each handler's own <c>Timeout</c>, the room
/// default for a room no handler carries one for, and <c>AutoSlayConfig.runTimeout</c>
/// per run. A bound that runs out gives the run up the same way.
/// </summary>
internal static class RetailSoak
{
    /// <summary>Where the night's summary goes, under the store, once the plan's count
    /// or deadline is reached and before the game is asked to quit. The script waits
    /// for it.</summary>
    internal const string DoneFileName = "soak-done";

    internal const string DoneSchema = "sts2-pilot-trainer/retail-soak-done/v1";

    private const string RootScene = "/root/Game/RootSceneContainer";
    private const string MainMenuPath = RootScene + "/MainMenu";
    private const string RoomContainer = RootScene + "/Run/RoomContainer";
    private const string PauseButtonPath = RootScene + "/Run/GlobalUi/TopBar/RightAlignedStuff/PauseButton";

    private const string StartupDone = "Done";

    /// <summary>How long the recorder is given to settle before a decision, past
    /// which the decision is made anyway and the recorder's own refusal says what
    /// happened; the recorder's own settle budget is fifteen seconds.</summary>
    private const double RecorderWaitSeconds = 15.0;

    private const double PollSeconds = 0.05;

    /// <summary>How long an event may go without becoming one of the states this
    /// driver knows before it is unknown. A page's dialogue, an option's animation and
    /// the map's opening are all inside it.</summary>
    private static readonly TimeSpan EventStateBudget = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ScreenSettleBudget = TimeSpan.FromSeconds(20);

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private static int _armed;

    /// <summary>Whether the night's task has been started in this process.</summary>
    internal static bool Armed => Volatile.Read(ref _armed) == 1;

    /// <summary>
    /// The game members the soak presses that a build could rename, by name, for the
    /// module's refusal; empty on the build this was written for.
    /// </summary>
    internal static IEnumerable<string> MissingGameMembers()
    {
        if (AccessTools.Method(typeof(NGame), nameof(NGame.StartNewSingleplayerRun)) is null)
        {
            yield return $"{nameof(NGame)}.{nameof(NGame.StartNewSingleplayerRun)} is absent from this build, so the soak cannot start a run.";
        }

        if (AccessTools.Method(typeof(NGame), nameof(NGame.Quit)) is null)
        {
            yield return $"{nameof(NGame)}.{nameof(NGame.Quit)} is absent from this build, so the soak cannot end the night.";
        }
    }

    private static void Say(string line) =>
        Log.Info($"[{RunmobileMod.ModId}] soak +{Clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)}ms {line}", 2);

    /// <summary>The main menu has been built. The first time, the night's task starts;
    /// every later time is the menu coming back between runs, which the task itself
    /// waits for.</summary>
    internal static void MainMenuReady()
    {
        if (Interlocked.Exchange(ref _armed, 1) == 1) return;
        TaskHelper.RunSafely(RunTheNightAsync());
    }

    // ── The night ─────────────────────────────────────────────────────────────

    private static async Task RunTheNightAsync()
    {
        // Nothing is read until the game says its own startup is finished and a save
        // profile is chosen: the main menu is built one phase before either
        var ready = await RecordedFightRun.WaitUntil(
            () => EngineHost.StartupPhase() == StartupDone && NGame.Instance is not null &&
                  SaveManager.Instance is { IsProfileInitialized: true },
            RecordedFightRun.LetTheGameRun(60), () => RecordedFightRun.LetTheGameRun(PollSeconds));
        if (!ready) return;

        var settings = RunmobileSettings.Read();
        if (settings.RetailSoak is null && !IsASoakClientWithoutAPlan(settings))
        {
            // Every player's client reaches this line, and the soak is nobody's feature
            return;
        }

        if (RefusalToRun(settings, out var character) is { } refusal)
        {
            Say($"refusing to run: {refusal}");
            WriteDone(settings.RetailSoak, 0, [], refusal);
            Say("the night is over; asking the game to quit");
            NGame.Instance?.Quit();
            return;
        }

        var plan = settings.RetailSoak!;

        var deadline = DateTimeOffset.UtcNow.AddMinutes(plan.StopAfterMinutes);
        using var night = new CancellationTokenSource(TimeSpan.FromMinutes(plan.StopAfterMinutes));
        Say(
            $"armed: {plan.Runs.ToString(CultureInfo.InvariantCulture)} run(s) of {character.Id} at ascension " +
            $"{plan.Ascension.ToString(CultureInfo.InvariantCulture)}, " +
            $"{(plan.Seeds.Count == 0 ? "a fresh seed each" : $"seeds [{string.Join(", ", plan.Seeds)}]")}, " +
            $"until {deadline:O}; display server={DisplayServer.GetName()}");

        var outcomes = new List<RunRecord>();
        var started = 0;
        try
        {
            for (var run = 1; run <= plan.Runs; run++)
            {
                if (night.IsCancellationRequested)
                {
                    Say("the night's deadline passed; no further run is started");
                    break;
                }

                var seed = plan.Seeds.Count == 0
                    ? SeedHelper.GetRandomSeed()
                    : SeedHelper.CanonicalizeSeed(plan.Seeds[(run - 1) % plan.Seeds.Count]);
                started++;
                var outcome = await PlayOneRunAsync(run, character, seed, plan.Ascension, night.Token);
                outcomes.Add(new RunRecord(run, seed, outcome));
                Say($"run {run.ToString(CultureInfo.InvariantCulture)} ({seed}): {outcome}");
                if (string.Equals(outcome, RunOutcome.ClientUnusable, StringComparison.Ordinal)) break;
            }
        }
        finally
        {
            WriteDone(plan, started, outcomes, refusal: null);
            Say("the night is over; asking the game to quit");
            NGame.Instance?.Quit();
        }
    }

    /// <summary>One run of the night, as written to <see cref="DoneFileName"/>.</summary>
    private sealed record RunRecord(int run, string seed, string outcome);

    /// <summary>
    /// Whether a client that read no plan is the soak's own, with a file it could not
    /// read: the settings file is unreadable and the client is headless. A player's
    /// client is never headless - a windowless game is not playable - and the soak
    /// script launches under nothing else, so this is the one case in which a client
    /// without a plan writes a refusal to the store and quits rather than leaving the
    /// player's menu alone; a readable file with no plan is every player's.
    /// </summary>
    private static bool IsASoakClientWithoutAPlan(RunmobileSettings settings) =>
        !settings.Readable && string.Equals(DisplayServer.GetName(), HeadlessDisplayServer, StringComparison.Ordinal);

    private const string HeadlessDisplayServer = "headless";

    /// <summary>
    /// Why this client, with these settings in hand, starts no run - or null, with the
    /// character the plan names, where the night may start. The settings half is
    /// <see cref="RefusalFor"/>; the rest reads the client: a multiplayer session,
    /// another Runmobile run live, a game the shell could not adopt, a character this
    /// build has not got, an ascension the profile has not unlocked.
    /// </summary>
    private static string? RefusalToRun(RunmobileSettings settings, out CharacterModel character)
    {
        character = null!;
        if (RefusalFor(settings) is { } refusal) return refusal;
        var plan = settings.RetailSoak!;
        if (!RunSession.MaySpeakIn(GameSessionWatch.Observed)) return "this client is in a multiplayer session";
        if (!RecordedFightRun.Idle || RunRecorder.Active is not null || ProfileWriteBarrier.IsActive)
        {
            return "another Runmobile run is live in this client";
        }

        if (!RunmobileMod.EnsureAdopted())
        {
            return "the running game could not be adopted, so nothing it plays could be recorded";
        }

        var named = ModelDb.AllCharacters.FirstOrDefault(
            candidate => string.Equals(candidate.Id.ToString(), plan.Character, StringComparison.Ordinal));
        if (named is null) return $"this build has no character '{plan.Character}'";

        var maxAscension = SaveManager.Instance.Progress.CharacterStats.TryGetValue(named.Id, out var stats)
            ? stats.MaxAscension
            : 0;
        if (plan.Ascension > maxAscension)
        {
            return $"the plan asks for ascension {plan.Ascension.ToString(CultureInfo.InvariantCulture)} " +
                   $"and this profile has unlocked {maxAscension.ToString(CultureInfo.InvariantCulture)} for {named.Id}; " +
                   "the game's own lobby would start a lower one, and the soak starts nothing it was not asked for";
        }

        character = named;
        return null;
    }

    /// <summary>
    /// Why the settings a client read give it no night to run, or null where they
    /// carry a plan the night may start. The settings half of the refusals, pure so a
    /// test holds each sentence without a game; a plan absent altogether is not a
    /// refusal but the default, and is answered before this is asked.
    /// </summary>
    internal static string? RefusalFor(RunmobileSettings settings)
    {
        if (!settings.Readable) return $"{RunmobileSettings.FileName} could not be read, so no plan in it can be trusted.";
        if (settings.RetailSoak is not { } plan) return $"{RunmobileSettings.FileName} carries no retail_soak plan.";
        if (!settings.RecordMyRuns) return "record_my_runs is off, and a soak without the recorder measures nothing.";

        var problems = plan.Problems();
        return problems.Count > 0 ? $"the plan cannot be run: {string.Join(" ", problems)}" : null;
    }

    /// <summary>The one-word outcomes a run ends in, as written to <see cref="DoneFileName"/>:
    /// <c>ended</c> and <c>deadline</c> are a night working as planned, and every other
    /// one is a finding the script names for the morning.</summary>
    private static class RunOutcome
    {
        /// <summary>The run ended on its own: the player died, or the run was won.</summary>
        internal const string Ended = "ended";

        /// <summary>The night's deadline passed inside the run and it was given up.</summary>
        internal const string Deadline = "deadline";

        /// <summary>A state this driver does not know, given up at.</summary>
        internal const string Unknown = "unknown-state";
        /// <summary>A bound - a room's, a screen's or the run's - ran out.</summary>
        internal const string TimedOut = "timed-out";

        /// <summary>The driver threw, or the recorder never attached.</summary>
        internal const string Failed = "failed";

        /// <summary>The client is somewhere this driver could not leave, so no further
        /// run is started and the night ends.</summary>
        internal const string ClientUnusable = "client-unusable";
    }

    /// <summary>The night's summary: the plan's count - null where no plan could be
    /// read - how many runs started and how each ended, and <c>refusal</c> - null for
    /// a night that ran, the sentence for one the client refused before starting a run,
    /// which the script reads as a night that measured nothing.</summary>
    private static void WriteDone(RetailSoakPlan? plan, int started, IReadOnlyList<RunRecord> outcomes, string? refusal)
    {
        try
        {
            var text = JsonSerializer.Serialize(new
            {
                schema = DoneSchema,
                finished_at_utc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                runs_planned = plan?.Runs,
                runs_started = started,
                runs = outcomes,
                refusal,
                recorder_version = RunmobileVersion.Recorder,
            }, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            RunmobileStore.Write(DoneFileName, text + "\n");
            Say($"wrote {DoneFileName}");
        }
        catch (Exception ex)
        {
            Log.Error($"[{RunmobileMod.ModId}] soak could not write {DoneFileName}: {ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    // ── One run ───────────────────────────────────────────────────────────────

    private static async Task<string> PlayOneRunAsync(
        int run, CharacterModel character, string seed, int ascension, CancellationToken night)
    {
        var root = ((SceneTree)Godot.Engine.GetMainLoop()).Root;
        var mainMenu = await WaitHelper.ForNode<Control>(root, MainMenuPath, night, TimeSpan.FromSeconds(60));
        await RecordedFightRun.LetTheGameRun(1);

        // A saved run in this profile - one a previous night's deadline cut short - is
        // continued through the menu's own Continue and given up once it stands in its
        // room, so the recorder picks its journal back up and finishes it; abandoned
        // from the menu it would stay a journal with no manifest
        var abandon = mainMenu.GetNodeOrNull<NButton>("MainMenuTextButtons/AbandonRunButton");
        if (abandon is { Visible: true })
        {
            Say("a saved run is in this profile; continuing it through the menu's own Continue to give it up");
            await UiHelper.Click(mainMenu.GetNode<NButton>("MainMenuTextButtons/ContinueButton"));
            await WaitHelper.Until(
                () => RunManager.Instance.IsInProgress && RunRecorder.HasEnteredItsRoom(), night,
                TimeSpan.FromSeconds(60), "the continued run never stood in its room");
            await RecordedFightRun.LetTheGameRun(2);
            await WaitForTheRecorderAsync("giving the continued run up");
            if (!await GiveUpAsync(night)) return RunOutcome.ClientUnusable;
            mainMenu = await WaitHelper.ForNode<Control>(root, MainMenuPath, night, TimeSpan.FromSeconds(60));
            await RecordedFightRun.LetTheGameRun(1);
        }

        // The acts the game's own lobby would build for this seed and this profile's
        // unlocks, through the same members
        var unlockState = new UnlockState(SaveManager.Instance.Progress);
        var acts = ActModel.GetRandomList(
            new Rng(StringHelper.GetDeterministicHashCode(seed), "act_selection"), unlockState, isMultiplayer: false).ToList();
        Say(
            $"run {run.ToString(CultureInfo.InvariantCulture)}: starting {character.Id} seed={seed} " +
            $"acts=[{string.Join(", ", acts.Select(act => act.Id))}] ascension={ascension.ToString(CultureInfo.InvariantCulture)}");

        // The run's own bound, and the night's: either gives the run up
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(night);
        bound.CancelAfter(AutoSlayConfig.runTimeout);
        var ct = bound.Token;

        try
        {
            await NGame.Instance!.StartNewSingleplayerRun(
                character, shouldSave: true, acts, Array.Empty<ModifierModel>(), seed, GameMode.Standard, ascension, null);
            Say($"run {run.ToString(CultureInfo.InvariantCulture)}: started; recorder active={RunRecorder.Active is not null}");
            if (RunRecorder.Active is null)
            {
                Say("the recorder did not attach to this run; giving it up rather than playing unrecorded");
                return await GiveUpAsync(night) ? RunOutcome.Failed : RunOutcome.ClientUnusable;
            }

            return await PlayAsync(ct);
        }
        catch (OperationCanceledException) when (night.IsCancellationRequested)
        {
            Say("the night's deadline passed inside this run; giving it up");
            return await GiveUpAsync(CancellationToken.None) ? RunOutcome.Deadline : RunOutcome.ClientUnusable;
        }
        catch (OperationCanceledException)
        {
            Say($"the run's own bound of {AutoSlayConfig.runTimeout.TotalMinutes.ToString(CultureInfo.InvariantCulture)} minutes ran out; giving it up");
            return await GiveUpAsync(night) ? RunOutcome.TimedOut : RunOutcome.ClientUnusable;
        }
        catch (UnknownStateException ex)
        {
            Say($"UNKNOWN STATE {DescribeWhere()}: {ex.Message}; giving the run up so the recording finishes");
            return await GiveUpAsync(night) ? RunOutcome.Unknown : RunOutcome.ClientUnusable;
        }
        catch (AutoSlayTimeoutException ex)
        {
            Say($"a bound ran out {DescribeWhere()}: {ex.Message}; giving the run up");
            return await GiveUpAsync(night) ? RunOutcome.TimedOut : RunOutcome.ClientUnusable;
        }
        catch (Exception ex) when (RunManager.Instance is { IsInProgress: true })
        {
            Say($"FAILED inside the run {DescribeWhere()}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return await GiveUpAsync(night) ? RunOutcome.Failed : RunOutcome.ClientUnusable;
        }
        catch (Exception ex)
        {
            Say($"FAILED with no run in progress: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return RunOutcome.Failed;
        }
    }

    /// <summary>Where the run stands, for a log line: floor, act, room and coordinate.</summary>
    private static string DescribeWhere()
    {
        try
        {
            var run = LiveRun.State;
            if (run is null) return "with no run";
            var me = LocalContext.GetMe(run);
            return $"at floor {run.TotalFloor.ToString(CultureInfo.InvariantCulture)} (act " +
                   $"{(run.CurrentActIndex + 1).ToString(CultureInfo.InvariantCulture)}, act floor " +
                   $"{run.ActFloor.ToString(CultureInfo.InvariantCulture)}, room {run.CurrentRoom?.RoomType.ToString() ?? "none"}, " +
                   $"coord {run.CurrentMapCoord}, hp {me?.Creature.CurrentHp.ToString(CultureInfo.InvariantCulture) ?? "?"}/" +
                   $"{me?.Creature.MaxHp.ToString(CultureInfo.InvariantCulture) ?? "?"}, " +
                   $"in combat {CombatManager.Instance?.IsInProgress ?? false}, " +
                   $"screen {NOverlayStack.Instance?.Peek()?.GetType().Name ?? "none"})";
        }
        catch (Exception ex)
        {
            return $"somewhere this driver could not describe ({ex.GetType().Name})";
        }
    }

    /// <summary>A room, screen or transition this driver has no handler for.</summary>
    private sealed class UnknownStateException(string message) : Exception(message);

    // ── The run loop, the shape of AutoSlayer.PlayRunAsync ───────────────────

    private static IReadOnlyDictionary<Type, IScreenHandler> ScreenHandlers() => new Dictionary<Type, IScreenHandler>
    {
        [typeof(NRewardsScreen)] = new RewardsScreenHandler(),
        [typeof(NCardRewardSelectionScreen)] = new CardRewardScreenHandler(),
        [typeof(NDeckUpgradeSelectScreen)] = new DeckUpgradeScreenHandler(),
        [typeof(NDeckTransformSelectScreen)] = new DeckTransformScreenHandler(),
        [typeof(NDeckEnchantSelectScreen)] = new DeckEnchantScreenHandler(),
        [typeof(NDeckCardSelectScreen)] = new DeckCardSelectScreenHandler(),
        [typeof(NSimpleCardSelectScreen)] = new SimpleCardSelectScreenHandler(),
        [typeof(NChooseACardSelectionScreen)] = new ChooseACardScreenHandler(),
        [typeof(NChooseABundleSelectionScreen)] = new ChooseABundleScreenHandler(),
        [typeof(NChooseARelicSelection)] = new ChooseARelicScreenHandler(),
        [typeof(NGameOverScreen)] = new GameOverScreenHandler(),
        [typeof(NCrystalSphereScreen)] = new CrystalSphereScreenHandler(),
    };

    /// <summary>The game's own screen handlers, one instance per process, keyed by the
    /// screen type each answers; the map and room handlers are constructed per run.</summary>
    private static readonly Lazy<IReadOnlyDictionary<Type, IScreenHandler>> Screens = new(ScreenHandlers);

    /// <summary>The game's own handler types the soak reuses unchanged, for the guard
    /// that reads their IL beside this driver's.</summary>
    internal static IReadOnlyList<Type> ReusedGameHandlers =>
    [
        typeof(MapScreenHandler), typeof(RestSiteRoomHandler), typeof(ShopRoomHandler), typeof(TreasureRoomHandler),
        .. Screens.Value.Values.Select(handler => handler.GetType()),
    ];

    private static async Task<string> PlayAsync(CancellationToken ct)
    {
        var random = new Rng(StringHelper.GetDeterministicHashCode(LiveRun.Required().Rng.StringSeed));
        var map = new MapScreenHandler();
        var rest = new RestSiteRoomHandler();
        var shop = new ShopRoomHandler(DrainOverlayScreensUntilAsync);
        var treasure = new TreasureRoomHandler();
        var floors = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await WaitHelper.Until(
                () => LiveRun.State?.CurrentRoom is { RoomType: not RoomType.Unassigned } || !RunManager.Instance.IsInProgress,
                ct, AutoSlayConfig.runStateTimeout, "the room's type was never assigned");
            if (!RunManager.Instance.IsInProgress)
            {
                await DrainOverlayScreensAsync(ct);
                return RunOutcome.Ended;
            }

            var roomType = LiveRun.Required().CurrentRoom!.RoomType;
            floors++;
            Say($"FLOOR {floors.ToString(CultureInfo.InvariantCulture)}: {roomType} {DescribeWhere()} recorder={RecorderState()}");

            await WaitForTheRecorderAsync($"the {roomType} room");
            switch (roomType)
            {
                case RoomType.Monster or RoomType.Elite or RoomType.Boss:
                    await WaitHelper.WithTimeout(token => FightRoomAsync(roomType, token), CombatRoomBound, ct);
                    break;
                case RoomType.Event:
                    await WaitHelper.WithTimeout(EventRoomAsync, EventRoomBound, ct);
                    break;
                case RoomType.Shop:
                    await RunTheGamesHandlerAsync(shop, random, ct);
                    break;
                case RoomType.Treasure:
                    await RunTheGamesHandlerAsync(treasure, random, ct);
                    break;
                case RoomType.RestSite:
                    await RunTheGamesHandlerAsync(rest, random, ct);
                    await DrainOverlayScreensAsync(ct);
                    await PressTheRestSiteProceedAsync(ct);
                    break;
                default:
                    throw new UnknownStateException($"no handler for room type {roomType}");
            }

            if (!RunManager.Instance.IsInProgress)
            {
                Say("the run ended");
                await DrainOverlayScreensAsync(ct);
                return RunOutcome.Ended;
            }

            await DrainOverlayScreensAsync(ct);
            if (!RunManager.Instance.IsInProgress)
            {
                await DrainOverlayScreensAsync(ct);
                return RunOutcome.Ended;
            }

            // The map, through the game's own handler: the first child of the node
            // the run stands on
            await WaitForTheRecorderAsync("the map move");
            await RunTheGamesHandlerAsync(map, random, ct);
        }
    }

    /// <summary>The game's own bound for its combat handler, which is what its
    /// autoplayer runs a fight under; the room default is two minutes and a boss
    /// fight at the player's speed runs past it.</summary>
    private static TimeSpan CombatRoomBound => new CombatRoomHandler().Timeout;

    /// <summary>The game's own bound for its event handler, for the same reason.</summary>
    private static TimeSpan EventRoomBound => new EventRoomHandler().Timeout;

    private static async Task RunTheGamesHandlerAsync(IHandler handler, Rng random, CancellationToken ct)
    {
        var name = handler.GetType().Name;
        Say($"game {name}: begin");
        await WaitHelper.WithTimeout(token => handler.HandleAsync(random, token), handler.Timeout, ct);
        Say($"game {name}: end; recorder={RecorderState()}");
        await RecordedFightRun.LetTheGameRun(0.5);
    }

    // ── Pacing on the recorder ────────────────────────────────────────────────

    /// <summary>
    /// Waits for the recorder to be ready for the next decision, and says in the log
    /// when it was not within the budget. The decision is then made anyway: the
    /// recorder's own rule refuses it rather than misrecording it, and a soak that
    /// stalled for ever on a recorder that never settled would measure nothing.
    /// </summary>
    private static async Task WaitForTheRecorderAsync(string before)
    {
        var started = Clock.ElapsedMilliseconds;
        var settled = await RecordedFightRun.WaitUntil(
            () => RunRecorder.Settled, RecordedFightRun.LetTheGameRun(RecorderWaitSeconds),
            () => RecordedFightRun.LetTheGameRun(PollSeconds));
        var waited = Clock.ElapsedMilliseconds - started;
        if (!settled)
        {
            Say($"the recorder was still not settled after {waited.ToString(CultureInfo.InvariantCulture)}ms before {before}");
        }
        else if (waited > 200)
        {
            Say($"waited {waited.ToString(CultureInfo.InvariantCulture)}ms for the recorder before {before}");
        }
    }

    private static string RecorderState()
    {
        var recorder = RunRecorder.Active;
        if (recorder is null) return "none";
        return $"{recorder.Capture.State}/{recorder.Capture.Integrity}/{recorder.Capture.Continuity}";
    }

    // ── The fight, the mod's own ──────────────────────────────────────────────

    private static async Task FightRoomAsync(RoomType roomType, CancellationToken ct)
    {
        await FightAsync(roomType, ct);
        await WaitHelper.Until(
            () => NOverlayStack.Instance?.Peek() is NRewardsScreen || (NMapScreen.Instance?.IsOpen ?? false) ||
                  !RunManager.Instance.IsInProgress,
            ct, ScreenSettleBudget, "neither the rewards nor the map came up after the fight");
    }

    private static async Task FightAsync(RoomType roomType, CancellationToken ct)
    {
        var fightStart = Clock.ElapsedMilliseconds;
        await WaitHelper.Until(() => CombatManager.Instance.IsInProgress, ct, AutoSlayConfig.nodeWaitTimeout, "combat not started");
        var me = LocalContext.GetMe(LiveRun.Required()) ?? throw new InvalidOperationException("The run has no local player.");
        Say($"fight ({roomType}) started; enemies=[{string.Join(", ", Enemies().Select(enemy => enemy.ModelId))}]");

        var turns = 0;
        var lastPlayed = 0;
        while (CombatManager.Instance.IsInProgress && turns < 99)
        {
            ct.ThrowIfCancellationRequested();
            await WaitHelper.Until(
                () => IsPlayableTurnAfter(me, lastPlayed) || !CombatManager.Instance.IsInProgress, ct,
                TimeSpan.FromSeconds(60), "no playable turn");
            if (!CombatManager.Instance.IsInProgress) break;
            turns++;
            var turn = me.PlayerCombatState!.TurnNumber;
            lastPlayed = turn;
            Say(
                $"turn {turn.ToString(CultureInfo.InvariantCulture)}: hand=[{string.Join(", ", me.PlayerCombatState.Hand.Cards.Select(card => card.Id))}] " +
                $"energy={me.PlayerCombatState.Energy.ToString(CultureInfo.InvariantCulture)} hp={me.Creature.CurrentHp.ToString(CultureInfo.InvariantCulture)}");

            await WaitForTheRecorderAsync("the turn's first action");
            if (turn == 1) await DrinkPotionsAsync(me, roomType, ct);

            var plays = 0;
            while (plays < 40 && CombatManager.Instance.IsInProgress &&
                   me.PlayerCombatState is { Phase: PlayerTurnPhase.Play } state && state.TurnNumber == turn)
            {
                ct.ThrowIfCancellationRequested();
                if (await AnswerTheHandPromptAsync(ct)) continue;
                if (NOverlayStack.Instance is { ScreenCount: > 0 })
                {
                    Say("a screen is up inside the fight; draining it");
                    await DrainOverlayScreensAsync(ct);
                    await WaitQuietAsync(ScreenSettleBudget);
                    continue;
                }

                await WaitForTheRecorderAsync("a play");
                if (!NextPlay(me, out var index, out var target)) break;
                var card = state.Hand.Cards[index];
                plays++;
                var history = CombatManager.Instance.History;
                var playsStartedBefore = history.CardPlaysStarted.Count();
                var ok = card.TryManualPlay(target);
                Say($"play {card.Id} (hand index {index.ToString(CultureInfo.InvariantCulture)}) at {target?.ModelId.ToString() ?? "nothing"} -> {ok}");
                if (!ok) break;

                await WaitHelper.Until(
                    () => PlayStarted(history, playsStartedBefore, card) || HandPromptOpen() || !CombatManager.Instance.IsInProgress ||
                          NOverlayStack.Instance is { ScreenCount: > 0 },
                    ct, TimeSpan.FromSeconds(15), "the engine's combat history never held the play");
                if (NOverlayStack.Instance is { ScreenCount: > 0 })
                {
                    Say("the play opened a screen; draining it");
                    await DrainOverlayScreensAsync(ct);
                }

                await AnswerTheHandPromptAsync(ct);
                await WaitQuietAsync(ScreenSettleBudget);
                await RecordedFightRun.LetTheGameRun(0.15);
            }

            if (!CombatManager.Instance.IsInProgress) break;
            if (me.PlayerCombatState is { Phase: PlayerTurnPhase.Play } still && still.TurnNumber == turn)
            {
                await WaitForTheRecorderAsync("the end of the turn");
                Say($"turn {turn.ToString(CultureInfo.InvariantCulture)}: end turn after {plays.ToString(CultureInfo.InvariantCulture)} play(s)");
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(me, turn));
            }
            else
            {
                Say($"turn {turn.ToString(CultureInfo.InvariantCulture)}: ended by the game");
            }
        }

        Say(
            $"fight finished after {(Clock.ElapsedMilliseconds - fightStart).ToString(CultureInfo.InvariantCulture)}ms, " +
            $"{turns.ToString(CultureInfo.InvariantCulture)} turn(s); in progress={CombatManager.Instance.IsInProgress} " +
            $"hp={me.Creature.CurrentHp.ToString(CultureInfo.InvariantCulture)} recorder={RecorderState()}");
    }

    /// <summary>Whether the engine began this card's play: a <c>CardPlayStartedEntry</c>
    /// for it past the ones the history held before the request, which the engine
    /// writes as the play begins and never for a play it declined. Read off the
    /// history and never off where the card ended up, because Particle Wall and a
    /// 0-cost attack under Feral go back to the hand after a play executed in full.</summary>
    private static bool PlayStarted(CombatHistory history, int playsStartedBefore, CardModel card) =>
        history.CardPlaysStarted.Skip(playsStartedBefore).Any(entry => ReferenceEquals(entry.CardPlay.Card, card));

    private static bool IsPlayableTurnAfter(Player player, int lastPlayed) =>
        player.PlayerCombatState is { Phase: PlayerTurnPhase.Play } state && state.TurnNumber > lastPlayed;

    /// <summary>The next play under the survival rule, in game-typed parts, so the
    /// async fight loop holds nothing of a sibling assembly's type across an await.</summary>
    private static bool NextPlay(Player me, out int index, out Creature? target)
    {
        var play = SurvivalPlayRule.Next(SurvivalRule.BlockWhenThreatened, me, Enemies());
        index = play?.HandIndex ?? -1;
        target = play?.Target;
        return play is not null;
    }

    private static IReadOnlyList<Creature> Enemies() =>
        CombatManager.Instance.DebugOnlyGetState()?.Enemies ?? [];

    /// <summary>The walk's potion rule: everything on an elite or boss, one when hurt.</summary>
    private static async Task DrinkPotionsAsync(Player me, RoomType roomType, CancellationToken ct)
    {
        var everything = roomType is RoomType.Elite or RoomType.Boss;
        var hurt = me.Creature.CurrentHp < me.Creature.MaxHp;
        if (!everything && !hurt) return;

        foreach (var potion in me.Potions.ToList())
        {
            if (me.PlayerCombatState is not { Phase: PlayerTurnPhase.Play } || !CombatManager.Instance.IsInProgress) return;
            await WaitForTheRecorderAsync("a potion");
            var target = SurvivalPlayRule.TargetFor(SurvivalRule.BlockWhenThreatened, potion.TargetType, Enemies());
            Say($"potion {potion.Id} at {target?.ModelId.ToString() ?? "nothing"}");
            potion.EnqueueManualUse(target);
            await WaitHelper.Until(
                () => Quiet() || NOverlayStack.Instance is { ScreenCount: > 0 } || HandPromptOpen(), ct,
                ScreenSettleBudget, "the potion never settled");
            if (NOverlayStack.Instance is { ScreenCount: > 0 })
            {
                Say("the potion opened a screen; draining it");
                await DrainOverlayScreensAsync(ct);
            }

            await AnswerTheHandPromptAsync(ct);
            await WaitQuietAsync(ScreenSettleBudget);
            await RecordedFightRun.LetTheGameRun(0.3);
            if (!everything) return;
        }
    }

    private static bool HandPromptOpen() => NPlayerHand.Instance is { IsInCardSelection: true };

    /// <summary>
    /// Presses the hand's own card holder and, where the prompt is a range, its
    /// confirm button: the presser the game's autoplayer has none for, because its
    /// selector answered the prompt engine-side.
    /// </summary>
    private static async Task<bool> AnswerTheHandPromptAsync(CancellationToken ct)
    {
        var hand = NPlayerHand.Instance;
        if (hand is not { IsInCardSelection: true }) return false;

        await WaitForTheRecorderAsync("the hand prompt");
        var holders = hand.CardHolderContainer.GetChildren().OfType<NHandCardHolder>()
            .Where(holder => holder.Visible && holder.CardNode?.Model is not null).ToList();
        Say($"HAND PROMPT open: mode={hand.CurrentMode} holders=[{string.Join(", ", holders.Select(holder => holder.CardNode!.Model!.Id))}]");
        if (holders.Count == 0) throw new UnknownStateException("the hand prompt is open with no visible holder to press");

        var pick = holders[0];
        pick.EmitSignal(NCardHolder.SignalName.Pressed, pick);
        Say($"HAND PROMPT: pressed {pick.CardNode!.Model!.Id}");
        await RecordedFightRun.LetTheGameRun(0.3);

        if (hand.IsInCardSelection)
        {
            var confirm = hand.GetNodeOrNull<NConfirmButton>("%SelectModeConfirmButton");
            if (confirm is { IsEnabled: true })
            {
                confirm.ForceClick();
                Say("HAND PROMPT: confirm pressed");
            }
        }

        await WaitHelper.Until(() => !HandPromptOpen(), ct, AutoSlayConfig.nodeWaitTimeout, "the hand prompt did not close");
        await WaitQuietAsync(ScreenSettleBudget);
        Say($"HAND PROMPT: answered; recorder={RecorderState()}");
        return true;
    }

    private static bool Quiet()
    {
        var manager = RunManager.Instance;
        if (manager.ActionExecutor is { IsRunning: true }) return false;
        if (!manager.ActionQueueSet.IsEmpty) return false;
        if (!CombatManager.Instance.IsInProgress) return true;
        return LiveRun.ReadyForThePlayer() || HandPromptOpen();
    }

    private static async Task WaitQuietAsync(TimeSpan budget)
    {
        // Asked twice with a tick between, the way the recorder asks it
        var started = Clock.ElapsedMilliseconds;
        while (Clock.ElapsedMilliseconds - started < budget.TotalMilliseconds)
        {
            if (Quiet())
            {
                await RecordedFightRun.LetTheGameRun(PollSeconds);
                if (Quiet()) return;
            }

            if (NOverlayStack.Instance is { ScreenCount: > 0 }) return;
            await RecordedFightRun.LetTheGameRun(PollSeconds);
        }

        Say(
            $"the engine was not quiet after {budget.TotalSeconds.ToString(CultureInfo.InvariantCulture)}s " +
            $"(executor running={RunManager.Instance.ActionExecutor?.IsRunning}, queue empty={RunManager.Instance.ActionQueueSet.IsEmpty})");
    }

    // ── The event room, the mod's own, state by state ────────────────────────

    /// <summary>What an event is doing after a press, read off the scene.</summary>
    private enum EventState
    {
        /// <summary>Nothing this driver knows is true yet.</summary>
        Nothing,

        /// <summary>An ancient's dialogue is up with no option to press yet.</summary>
        Dialogue,

        /// <summary>Another option can be pressed.</summary>
        Choice,

        /// <summary>The option opened a fight.</summary>
        Fight,

        /// <summary>The option opened a screen.</summary>
        Screen,

        /// <summary>The event is over: the map is open, or the run has ended.</summary>
        Ended,
    }

    private static async Task EventRoomAsync(CancellationToken ct)
    {
        var root = ((SceneTree)Godot.Engine.GetMainLoop()).Root;
        var eventRoom = await WaitHelper.ForNode<Node>(root, $"{RoomContainer}/EventRoom", ct, AutoSlayConfig.nodeWaitTimeout);
        Say($"event room: {LiveRun.State?.CurrentRoom?.GetType().Name}");

        for (var step = 0; step < 40; step++)
        {
            ct.ThrowIfCancellationRequested();
            var state = await ClassifyTheEventAsync(eventRoom, ct);
            Say($"event state: {state}");
            switch (state)
            {
                case EventState.Ended:
                    return;
                case EventState.Fight:
                    await FightAsync(RoomType.Event, ct);
                    await WaitHelper.Until(
                        () => NOverlayStack.Instance?.Peek() is NRewardsScreen || EnabledOptions(eventRoom).Count > 0 ||
                              (NMapScreen.Instance?.IsOpen ?? false) || !RunManager.Instance.IsInProgress,
                        ct, ScreenSettleBudget, "nothing followed the event's fight");
                    await DrainOverlayScreensAsync(ct);
                    continue;
                case EventState.Screen:
                    await DrainOverlayScreensAsync(ct);
                    continue;
                case EventState.Dialogue:
                    await PressTheDialogueAsync(eventRoom, ct);
                    continue;
                case EventState.Choice:
                    await PressAnOptionAsync(eventRoom, ct);
                    await DrainOverlayScreensAsync(ct);
                    continue;
                default:
                    throw new UnknownStateException(
                        $"the event {EventName(eventRoom)} reached no state this driver knows within " +
                        $"{EventStateBudget.TotalSeconds.ToString(CultureInfo.InvariantCulture)}s of the last press " +
                        $"(options {DescribeOptions(eventRoom)})");
            }
        }

        throw new UnknownStateException($"the event {EventName(eventRoom)} did not end after 40 steps");
    }

    private static string EventName(Node eventRoom) =>
        UiHelper.FindAll<NEventOptionButton>(eventRoom).FirstOrDefault()?.Event.Id.ToString() ??
        LiveRun.State?.CurrentRoom?.GetType().Name ?? "unknown";

    private static string DescribeOptions(Node eventRoom) =>
        "[" + string.Join(" | ", UiHelper.FindAll<NEventOptionButton>(eventRoom).Select(option =>
            $"{option.Option.TextKey}{(option.IsEnabled ? "" : " disabled")}{(option.Option.IsLocked ? " locked" : "")}")) + "]";

    private static List<NEventOptionButton> EnabledOptions(Node eventRoom) =>
        GodotObject.IsInstanceValid(eventRoom)
            ? UiHelper.FindAll<NEventOptionButton>(eventRoom).Where(option => option.IsEnabled && !option.Option.IsLocked).ToList()
            : [];

    /// <summary>
    /// Reads the event's state, waiting up to the budget for it to become one this
    /// driver knows; <see cref="EventState.Nothing"/> is what comes back when it never
    /// does, and is the unknown state the caller refuses on.
    /// </summary>
    private static async Task<EventState> ClassifyTheEventAsync(Node eventRoom, CancellationToken ct)
    {
        var deadline = RecordedFightRun.LetTheGameRun(EventStateBudget.TotalSeconds);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var state = ReadTheEvent(eventRoom);
            if (state != EventState.Nothing) return state;
            if (deadline.IsCompleted) return EventState.Nothing;
            await RecordedFightRun.LetTheGameRun(PollSeconds);
        }
    }

    private static EventState ReadTheEvent(Node eventRoom)
    {
        if (!RunManager.Instance.IsInProgress) return EventState.Ended;
        if (CombatManager.Instance.IsInProgress) return EventState.Fight;
        if (NOverlayStack.Instance is { ScreenCount: > 0 }) return EventState.Screen;
        if (NMapScreen.Instance?.IsOpen ?? false) return EventState.Ended;
        if (!GodotObject.IsInstanceValid(eventRoom)) return EventState.Nothing;
        if (EnabledOptions(eventRoom).Count > 0) return EventState.Choice;

        var ancient = UiHelper.FindFirst<NAncientEventLayout>(eventRoom);
        if (ancient?.GetNodeOrNull<NButton>("%DialogueHitbox") is { } hitbox && hitbox.IsVisibleInTree()) return EventState.Dialogue;

        return EventState.Nothing;
    }

    private static async Task PressTheDialogueAsync(Node eventRoom, CancellationToken ct)
    {
        var ancient = UiHelper.FindFirst<NAncientEventLayout>(eventRoom);
        var hitbox = ancient?.GetNodeOrNull<NButton>("%DialogueHitbox");
        if (hitbox is null) return;
        Say("event: advancing the ancient's dialogue");
        await UiHelper.Click(hitbox, 300);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Presses the first enabled, unlocked option that does not kill the player,
    /// after the recorder has settled, and waits for the event to respond: the
    /// options changed, the map opened, a fight or a screen came up. What it responded
    /// with is the next state, read by the caller.
    /// </summary>
    private static async Task PressAnOptionAsync(Node eventRoom, CancellationToken ct)
    {
        var options = EnabledOptions(eventRoom);
        var owner = options[0].Event.Owner;
        var safe = options.Where(option =>
            option.Option.WillKillPlayer is null || owner is null || !option.Option.WillKillPlayer(owner)).ToList();
        var choice = (safe.Count > 0 ? safe : options)[0];
        var before = new HashSet<NEventOptionButton>(options);

        await WaitForTheRecorderAsync("the event option");
        Say(
            $"event {choice.Event.Id}: options {DescribeOptions(eventRoom)}; pressing '{choice.Option.TextKey}' " +
            $"(proceed={choice.Option.IsProceed})");
        await UiHelper.Click(choice);
        await WaitHelper.Until(
            () =>
            {
                if (ReadTheEvent(eventRoom) is EventState.Ended or EventState.Fight or EventState.Screen) return true;
                var now = GodotObject.IsInstanceValid(eventRoom)
                    ? UiHelper.FindAll<NEventOptionButton>(eventRoom).Where(option => !option.Option.IsLocked).ToList()
                    : [];
                return now.Count == 0 || !before.SetEquals(now);
            }, ct, TimeSpan.FromSeconds(15), "the event option did not respond");
        await RecordedFightRun.LetTheGameRun(0.4);
    }

    private static async Task PressTheRestSiteProceedAsync(CancellationToken ct)
    {
        var root = ((SceneTree)Godot.Engine.GetMainLoop()).Root;
        var button = root.GetNodeOrNull<NProceedButton>($"{RoomContainer}/RestSiteRoom/ProceedButton");
        if (button is not { IsEnabled: true }) return;
        await WaitForTheRecorderAsync("the rest site's proceed");
        Say("rest site: pressing proceed");
        await UiHelper.Click(button);
        ct.ThrowIfCancellationRequested();
    }

    // ── Overlay screens, through the game's own handlers ─────────────────────

    /// <summary>The shop handler's drain callback: screens the purchase opens are
    /// answered while its own task waits.</summary>
    private static async Task DrainOverlayScreensUntilAsync(Task pending, CancellationToken ct)
    {
        while (!pending.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            var stack = NOverlayStack.Instance;
            if (stack is { ScreenCount: > 0 } && stack.Peek() is { } screen) await HandleScreenAsync(screen, ct);
            await RecordedFightRun.LetTheGameRun(PollSeconds);
        }

        await pending;
    }

    private static async Task DrainOverlayScreensAsync(CancellationToken ct)
    {
        var stalls = 0;
        IOverlayScreen? last = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var stack = NOverlayStack.Instance;
            if (stack is null || stack.ScreenCount == 0) return;
            var screen = stack.Peek();
            if (screen is null) return;
            // A rewards screen left up under an open map is the loot the map move declines
            if (screen is NRewardsScreen && (NMapScreen.Instance?.IsOpen ?? false)) return;

            var countBefore = stack.ScreenCount;
            await HandleScreenAsync(screen, ct);
            var after = NOverlayStack.Instance;
            if (after is not null && after.ScreenCount == countBefore && ReferenceEquals(after.Peek(), screen) &&
                ReferenceEquals(last, screen))
            {
                stalls++;
                if (stalls >= 3)
                {
                    throw new UnknownStateException($"the {screen.GetType().Name} screen did not close after three attempts");
                }
            }
            else
            {
                stalls = 0;
            }

            last = screen;
            await RecordedFightRun.LetTheGameRun(0.1);
        }
    }

    private static async Task HandleScreenAsync(IOverlayScreen screen, CancellationToken ct)
    {
        var type = ((object)screen).GetType();
        if (!Screens.Value.TryGetValue(type, out var handler))
        {
            // The game's autoplayer has no handler for the grid prompts a card opens
            // mid-fight; its selector answered those engine-side
            if (screen is NCardGridSelectionScreen grid)
            {
                await PressTheGridAsync(grid, ct);
                return;
            }

            throw new UnknownStateException($"no handler for the {type.Name} screen, and it is not a card grid");
        }

        await WaitForTheRecorderAsync($"the {type.Name} screen");
        Say($"game {handler.GetType().Name}: begin ({type.Name})");
        await WaitHelper.WithTimeout(
            token => handler.HandleAsync(new Rng(StringHelper.GetDeterministicHashCode(LiveRun.Required().Rng.StringSeed)), token),
            handler.Timeout, ct);
        Say($"game {handler.GetType().Name}: end; stack={NOverlayStack.Instance?.ScreenCount.ToString(CultureInfo.InvariantCulture) ?? "-"} recorder={RecorderState()}");
    }

    /// <summary>
    /// Presses the first card holder of a grid prompt and, where the prompt wants a
    /// confirmation, its confirm button: the mod's own presser for the in-fight prompts
    /// the game's autoplayer answered engine-side.
    /// </summary>
    private static async Task PressTheGridAsync(NCardGridSelectionScreen grid, CancellationToken ct)
    {
        await RecordedFightRun.LetTheGameRun(0.4);
        await WaitForTheRecorderAsync("the grid prompt");
        var holders = UiHelper.FindAll<NCardHolder>(grid).Where(holder => holder.CardNode?.Model is not null && holder.IsVisibleInTree()).ToList();
        Say($"GRID PROMPT {grid.GetType().Name}: holders=[{string.Join(", ", holders.Select(holder => holder.CardNode!.Model!.Id))}]");
        if (holders.Count == 0) throw new UnknownStateException($"the {grid.GetType().Name} grid is up with nothing to press");

        var pick = holders[0];
        pick.EmitSignal(NCardHolder.SignalName.Pressed, pick);
        Say($"GRID PROMPT: pressed {pick.CardNode!.Model!.Id}");
        await RecordedFightRun.LetTheGameRun(0.3);
        if (GodotObject.IsInstanceValid(grid) && ReferenceEquals(NOverlayStack.Instance?.Peek(), grid))
        {
            var confirm = UiHelper.FindFirst<NConfirmButton>(grid);
            if (confirm is { IsEnabled: true })
            {
                confirm.ForceClick();
                Say("GRID PROMPT: confirm pressed");
            }
        }

        await WaitHelper.Until(
            () => !GodotObject.IsInstanceValid(grid) || !ReferenceEquals(NOverlayStack.Instance?.Peek(), grid), ct,
            AutoSlayConfig.nodeWaitTimeout, "the grid prompt did not close");
        Say($"GRID PROMPT: answered; recorder={RecorderState()}");
    }

    // ── Giving up through the pause menu, so the recorder finishes the run ────

    /// <summary>
    /// Gives the run up the way a person does - the top bar's pause button, the pause
    /// menu's Give Up, the confirmation's Yes - and takes the game-over screen through
    /// its own handler back to the main menu. Answers whether it got there; a client it
    /// could not get out of a run in is one the night stops on.
    /// </summary>
    private static async Task<bool> GiveUpAsync(CancellationToken ct)
    {
        try
        {
            var root = ((SceneTree)Godot.Engine.GetMainLoop()).Root;
            if (!RunManager.Instance.IsInProgress)
            {
                await DrainOverlayScreensAsync(ct);
            }
            else
            {
                await RecordedFightRun.LetTheGameRun(1);
                var pause = await WaitHelper.ForNode<NTopBarPauseButton>(root, PauseButtonPath, ct, AutoSlayConfig.nodeWaitTimeout);
                await UiHelper.Click(pause);
                NPauseMenu? menu = null;
                await WaitHelper.Until(
                    () => (menu = UiHelper.FindFirst<NPauseMenu>(root)) is not null && menu.IsVisibleInTree(), ct,
                    AutoSlayConfig.nodeWaitTimeout, "the pause menu did not open");
                var giveUp = menu!.GetNode<Control>("%ButtonContainer").GetNode<NPauseMenuButton>("GiveUp");
                Say("give up: pressing Give Up");
                await UiHelper.Click(giveUp);
                NAbandonRunConfirmPopup? popup = null;
                await WaitHelper.Until(
                    () => (popup = UiHelper.FindFirst<NAbandonRunConfirmPopup>(root)) is not null, ct,
                    AutoSlayConfig.nodeWaitTimeout, "the confirmation did not appear");
                await UiHelper.Click(popup!.GetNode<NVerticalPopup>("VerticalPopup").YesButton);
                await WaitHelper.Until(
                    () => NOverlayStack.Instance?.Peek() is NGameOverScreen || !RunManager.Instance.IsInProgress, ct,
                    AutoSlayConfig.runStateTimeout, "the game-over screen did not appear");
                await RecordedFightRun.LetTheGameRun(1);
                Say($"give up: confirmed; recorder={RecorderState()}");
                await DrainOverlayScreensAsync(ct);
            }

            await WaitHelper.Until(
                () => root.GetNodeOrNull(MainMenuPath) is not null, ct, TimeSpan.FromSeconds(60), "the main menu did not come back");
            Say("give up: back at the main menu");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] soak could not give the run up {DescribeWhere()}: {ex.GetType().Name}: {ex.Message}", 2);
            return false;
        }
    }
}
