using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The run the player is in the middle of, read the way the arbiter reads the run it
/// built.
///
/// The recorder needs three things out of a live game and none of them are new: the
/// canonical state, the digest of it, and the run's identity. All three already have
/// owners - <see cref="CanonicalStateProjection"/>, <see cref="CanonicalState"/> and
/// <see cref="LocalEnvironment"/> - and this is the one place that asks them about
/// the player's own run rather than about a run this process constructed. Keeping it
/// here rather than in the mod is what lets a headless test exercise it, and is where
/// <c>AGENTS.md</c> puts everything that knows how v0.111.0 is put together.
///
/// It reads and never writes, like everything else in this file's neighbourhood. In
/// particular it does not touch the run's save flag: the player's own run saves
/// normally, and a recorder that changed that would be taking the run away from them
/// to describe it.
/// </summary>
public static class LiveRun
{
    /// <summary>The run in progress, or null when there is none.</summary>
    public static RunState? State => LocalEnvironment.StartedRunState();

    /// <summary>The run in progress, or a refusal saying there is none.</summary>
    public static RunState Required() =>
        State ?? throw new EngineException(
            "This game has no run in progress, so there is nothing to read. A recorder attaches to a run and " +
            "there is not one here.");

    /// <summary>The complete canonical state of the run in progress.</summary>
    public static CanonicalState Project() => CanonicalStateProjection.Project(Required());

    /// <summary>
    /// What a trace keeps, and what a boundary is identified by, taken at the same
    /// moment.
    ///
    /// Both from one projection rather than two, because they have to be readings of
    /// the same instant: a digest taken a projection later than the sample beside it
    /// would identify a state the sample does not describe.
    /// </summary>
    public static (IReadOnlyDictionary<string, string> Sample, string Digest) Read()
    {
        var state = Project();
        return (ReplayTrace.Sample(state.Fields), state.Digest());
    }

    /// <summary>Just the sampled fields, for a caller that only needs the trace's half.</summary>
    public static IReadOnlyDictionary<string, string> Sample() => ReplayTrace.Sample(Project().Fields);

    /// <summary>
    /// The game's own run clock, in milliseconds, or null where there is no run.
    ///
    /// Descriptive rather than identifying: action timing is not part of a run's
    /// identity, and this is kept because it is what lets a person find the moment
    /// again in their own recording of the session.
    /// </summary>
    public static int? RunClockMs()
    {
        var manager = RunManager.Instance;
        if (manager is null || !manager.IsInProgress) return null;

        var seconds = manager.RunTime;
        if (seconds < 0) return null;

        // A run clock past twenty-four days is a clock this build cannot render as an
        // int of milliseconds. It is descriptive, so it is dropped rather than made up.
        var milliseconds = seconds * 1000L;
        return milliseconds > int.MaxValue ? null : (int)milliseconds;
    }

    /// <summary>
    /// When this run began, as the game itself recorded it.
    ///
    /// The game keeps the run's start as a Unix timestamp and carries it through a
    /// save, so it is the same value in the session that started the run and in every
    /// session that continues it. That is what makes it half of a recording's name: a
    /// run picked back up tomorrow has to resolve to the journal it was being written
    /// into, and a clock reading taken at attach time would resolve to a new one every
    /// session.
    /// </summary>
    public static DateTimeOffset RunStartedUtc()
    {
        var manager = RunManager.Instance
            ?? throw new EngineException("This game has no run manager, so no run has a start time.");

        var field = typeof(RunManager).GetField(
            "_startTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new EngineException(
                "RunManager has no run start time on this build, so a recording of this run could not be " +
                "named in a way a later session would find again.");

        return DateTimeOffset.FromUnixTimeSeconds(
            (long)(field.GetValue(manager)
                ?? throw new EngineException("RunManager's run start time read as null on this build.")));
    }

    /// <summary>
    /// The run's identity, as the recorder writes it into a manifest.
    ///
    /// Every value is read out of the run itself, including the unlock state: the run
    /// carries the state it was generated against, which is a sharper source than the
    /// profile it came from - the profile can change while the run is being played and
    /// the run's generation cannot.
    /// </summary>
    public static RunIdentityReading ReadIdentity(RunState run)
    {
        var identity = GameIdentity.ReadForCurrentEngine();

        if (run.Players.Count == 0)
        {
            throw new EngineException(
                "This run has no player, so there is no character to record it under.");
        }

        return new RunIdentityReading
        {
            BuildVersion = identity.BuildVersion,
            BuildDateUtc = identity.BuildDateUtc,
            ContentHash = identity.ContentHash,
            GameMode = run.GameMode.ToString().ToLowerInvariant(),
            Seed = run.Rng.StringSeed,
            Ascension = run.AscensionLevel,
            Character = run.Players[0].Character.Id.ToString(),
            Acts = run.Acts.Select(act => act.Id.ToString()).ToList(),
            Unlocks = LocalEnvironment.ReadUnlockStateInventory(run.UnlockState),
            Mods = LoadedMods(),
        };
    }

    /// <summary>
    /// Every mod this game reported loaded, as the manifest's mod environment.
    ///
    /// A reading rather than an audit, and it says so. A video only ever showed a
    /// count, so a VOD manifest's list carries a human's assessment of each mod; a
    /// recorder reads each mod's own manifest instead, which is what lets
    /// <c>EnvironmentPreflight</c> judge a native recording by a rule rather than
    /// against a fixed list of audited names. What is written into the risk line is
    /// therefore the declaration itself, never a judgement this code is not in a
    /// position to make.
    ///
    /// The patch roster goes on the same reading, because it is the half a manifest
    /// cannot state: what the loaded mods actually did to this process. See
    /// <see cref="HarmonyRoster"/> and <c>EnvironmentPreflight</c>, which judges it.
    /// </summary>
    public static ModEnvironment LoadedMods() =>
        ModEnvironment.AsRecorded(LocalEnvironment.ReadMods(), HarmonyRoster.Read());

    /// <summary>Whether a fight is open at all, however far into opening it is.</summary>
    public static bool InCombat => CombatManager.Instance is { IsInProgress: true };

    /// <summary>
    /// What kind of game this client is playing right now, read out of the game's own
    /// networking and player list.
    ///
    /// The one reader of that question on v0.111.0, and it is read rather than
    /// inferred from which setup member the game called: the recorder watches
    /// <c>SetUpNewSingleplayer</c> and <c>SetUpSavedSingleplayer</c>, and a member
    /// whose name says "singleplayer" is a claim about what that member is for, not a
    /// reading of what this session became. The two facts read here are the ones the
    /// engine dispatches on itself - <c>NetService.Type</c>, which
    /// <c>RunManager.IsSingleplayerOrFakeMultiplayer</c> reads, and the run's player
    /// count, which is what tells that property's two halves apart.
    ///
    /// It never throws. The callers are a menu being built and a run starting, and a
    /// reading that could not be taken is <see cref="RunSessionKind.Unreadable"/>,
    /// which is refused exactly like a multiplayer answer: a run nothing established
    /// anything about is not a singleplayer run.
    /// </summary>
    public static RunSessionKind ReadSession()
    {
        try
        {
            if (RunManager.Instance is not { } manager) return RunSessionKind.NoRunInProgress;
            if (!manager.IsInProgress) return RunSessionKind.NoRunInProgress;
            if (manager.NetService is not { } net) return RunSessionKind.Unreadable;

            return net.Type switch
            {
                NetGameType.Host or NetGameType.Client => RunSessionKind.NetworkedMultiplayer,
                NetGameType.Replay => RunSessionKind.Spectated,

                // The game's own "fake multiplayer": no network game, and more than
                // one player sharing this client. Its history holds decisions this
                // client's player did not make.
                NetGameType.Singleplayer => LocalEnvironment.StartedRunState()?.Players.Count switch
                {
                    1 => RunSessionKind.Singleplayer,
                    > 1 => RunSessionKind.LocalMultiplayer,
                    _ => RunSessionKind.Unreadable,
                },

                _ => RunSessionKind.Unreadable,
            };
        }
        catch (Exception)
        {
            // A run the game is still building answers by throwing, and that is a
            // "could not read" like any other rather than a singleplayer run.
            return RunSessionKind.Unreadable;
        }
    }

    /// <summary>
    /// Whether the fight has finished opening and is the player's to act in.
    ///
    /// The one owner of that question, asked by everything that has to read a state a
    /// player could actually have acted from. Entering a room is not the same moment
    /// as the fight being ready: the room is built as soon as the map move's task
    /// completes, and the opening hand is dealt over the frames after it. A reading
    /// taken in between has an empty hand and no energy - a real state, and one no
    /// player ever saw. The engine's own turn phase is the signal, and it is the same
    /// signal whether a host is standing somebody in a recorded fight or a recorder is
    /// writing down the fight its player just walked into.
    ///
    /// It cost a whole retail evidence run to learn that twice. The playback path
    /// found it first and fixed it locally; the recorder had the same defect and
    /// anchored every combat-start boundary it ever wrote to that half-opened instant,
    /// so no recording it made could reproduce. Hence one predicate rather than two.
    /// </summary>
    public static bool ReadyForThePlayer(RunState run) =>
        InCombat && run.Players[0].PlayerCombatState is { Phase: PlayerTurnPhase.Play };

    /// <summary>The same question about the run this game is in the middle of.</summary>
    public static bool ReadyForThePlayer() => State is { } run && ReadyForThePlayer(run);
}
