using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Playing back a run the player recorded themselves, over the two real recordings
/// committed under <c>manifests/</c>.
///
/// Both defects this pins were shipped behind a green suite, and both for the same
/// reason: every test of the playback surfaces used the one hand-transcribed video
/// reconstruction, which is the only recording in this repository that carries a
/// channel name and the only one whose first fight is its last reachable boundary. So
/// these run against the recorder's own output instead.
///
/// <list type="number">
/// <item>Naming the creator threw on a native recording, and it is the first statement
/// of the journey - so every Play row on every run of the player's own aborted before
/// the run was constructed.</item>
/// <item>The library offered every boundary the recording proved, and the client can
/// only walk to those whose decisions it issues - so a row past the first fight built
/// the run, showed a decision or two and then aborted in front of the player.</item>
/// </list>
///
/// They are asserted against files rather than against a fixture because a fixture is
/// what was missing: the shapes that break are a recording with no video and a run with
/// more than one fight in it, and both are what the recorder actually writes.
/// </summary>
public sealed class NativeRecordingPlaybackTests
{
    /// <summary>The recorder's own output, both runs a person played on this build.</summary>
    public static TheoryData<string> Recordings =>
    [
        "native-3LACFJ5NJ371-20260906-015901.replay.json",
        "native-9F8CY60C5BK7-20260906-005737.replay.json",
    ];

    private static ReplayManifest Load(string fileName) =>
        ManifestJson.Deserialize(File.ReadAllText(Path.Combine(Arbiter.RepoRoot, "manifests", fileName)));

    // ── Crediting the run ──────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Recordings))]
    public void ARunTheRecorderWroteIsCreditedRatherThanRefused(string fileName)
    {
        var recording = Load(fileName);

        Assert.Null(recording.Source.Video);
        var credit = RecordingIdentity.Credit(recording, isPlayersOwn: true);

        Assert.True(credit.IsYours);
        Assert.Equal("Your run", credit.Label);
    }

    /// <summary>
    /// Every surface the journey puts up reads the credit, and each one is built here
    /// for a native recording.
    ///
    /// <c>RecordedFightRun.Start</c> cannot be called without a running client, so what
    /// is exercised is every call it makes that used to throw: the credit itself, the
    /// transport's identity block and its derivation, the post-fight choice, and the
    /// result screen. A single one of them left reading a name would put the refusal
    /// back, one screen further in.
    /// </summary>
    [Theory]
    [MemberData(nameof(Recordings))]
    public void EverySurfaceTheJourneyPutsUpBuildsForIt(string fileName)
    {
        var recording = Load(fileName);
        var credit = RecordingIdentity.Credit(recording, isPlayersOwn: true);
        var plan = RecordedFightPlan.For(recording, fight: 1);

        var identity = new TransportIdentity(
            credit, recording.Source.Video?.Title, recording.Source.Video?.Url, null);
        Assert.Equal("Your run", identity.TooltipTitle);
        Assert.False(identity.IsLink);

        var transport = PlaybackTransport.For(JourneyPhase.Watching, new TransportFacts(
            identity,
            Made: [],
            Next: new PrefightChoice.MapMove(plan.BoundarySeq, "Monster", 3, 7),
            StepsTaken: 0,
            Count: 2,
            AtCombatStart: false,
            Arrived: true,
            Lit: true,
            NextOptionCount: null,
            LookingBackAt: null,
            Playing: false,
            NoteShown: false,
            Speed: PlaybackSpeed.Normal,
            AnythingPlayed: false));
        Assert.NotNull(transport);
        Assert.StartsWith("Your choices are shown as recorded", transport.Note, StringComparison.Ordinal);

        var choice = PostFightChoice.For(credit, new PostFightFacts(
            Won: true, CanWatch: true, CanContinueAsYou: false,
            ComparisonShown: false, FightWatched: false));
        Assert.Contains(choice.Rows, row => row.Row.Label == "Watch your fight");

        // The notice paths, which are the two the result screen can reach without a
        // completed capture. Neither names anybody, and both had to be reachable.
        Assert.False(FightResultScreen.Left().HasComparison);
        Assert.False(FightResultScreen.Refused("no").HasComparison);

        Assert.Equal(
            "Your run · ", RecordingIdentity.Subtitle(recording, isPlayersOwn: true)[..11]);
    }

    /// <summary>The subtitle and the row's own creator line answer different questions,
    /// and the library's is still "this recording names nobody".</summary>
    [Theory]
    [MemberData(nameof(Recordings))]
    public void TheLibraryRowStillCarriesNoCreatorLine(string fileName)
    {
        var recording = Load(fileName);

        Assert.Null(RecordingIdentity.CreatorOrNull(recording));
        Assert.Null(LibraryRun.From(recording, RunOrigin.Mine, RunVerdict.Passed).Creator);
    }

    [Theory]
    [MemberData(nameof(Recordings))]
    public void ARunWithoutItsOwnRecordedLineShowsAPlainNotice(string fileName)
    {
        var recording = Load(fileName);
        var capture = FightCapture.Begin(
            "player",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["combat.outcome"] = "in_progress",
            },
            "sha256:test");
        var unrelated = new RecordedFights
        {
            SchemaId = RecordedFights.Schema,
            RunId = "another-recording",
            Fights = [],
        };

        var screen = RecordedFightRun.ResultAfterFight(
            recording, fight: 1,
            RecordingIdentity.Credit(recording, isPlayersOwn: true), capture, unrelated);

        Assert.False(screen.HasComparison);
        Assert.Equal(TrainerCopy.NoRecordedComparison, screen.Notice);
        Assert.DoesNotContain(unrelated.RunId, screen.Notice, StringComparison.Ordinal);
        Assert.DoesNotContain("digest", screen.Notice, StringComparison.OrdinalIgnoreCase);
    }

    // ── Offering only what the client can reach ────────────────────────────

    /// <summary>
    /// The invariant, stated against the recordings that break it.
    ///
    /// Every place the run view offers is a place whose decisions the driver will
    /// issue. It reads <c>RunDriver</c>'s own list rather than a copy, so a verb added
    /// to the client widens what the library offers in the same commit.
    /// </summary>
    [Theory]
    [MemberData(nameof(Recordings))]
    public void EveryPlaceTheRunViewOffersIsOneTheClientCanWalkTo(string fileName)
    {
        var recording = Load(fileName);
        var issued = RunDriver.VerbsIssuedInsideARunningGame;

        foreach (var position in RunView.PositionsIn(recording).Where(position => position.Playable))
        {
            var refused = recording.Actions
                .Where(action => action.Seq <= position.AfterSeq)
                .OrderBy(action => action.Seq)
                .FirstOrDefault(action => !issued.Contains(action.Verb));

            Assert.True(
                refused is null,
                $"Floor {position.Floor} is offered and the walk to it contains a {refused?.Verb} at action " +
                $"{refused?.Seq}, which the driver refuses inside a running game. Offering it builds the run, " +
                "shows a decision or two and then aborts in front of the player.");
        }
    }

    /// <summary>
    /// And the rows that are refused say why rather than disappearing.
    ///
    /// A recording of a whole run holds several fights, and on this build exactly one
    /// place in each is reachable: the first fight, whose walk is an opening blessing
    /// and a map move. The rest keep their place on the screen with the reason in the
    /// second line, because a row that vanished would teach the player the feature does
    /// not exist rather than that it does not reach here yet.
    /// </summary>
    [Theory]
    [MemberData(nameof(Recordings))]
    public void TheDeeperFloorsAreRefusedByNameRatherThanOffered(string fileName)
    {
        var recording = Load(fileName);
        var positions = RunView.PositionsIn(recording);

        Assert.True(positions.Count > 2, "This recording is too short to have a refused floor in it.");
        Assert.Single(positions, position => position.Playable);

        // The deepest floor refused for this reason rather than the deepest floor: a
        // recording that stops inside its last fight refuses that one for its own
        // reason, and the two are different facts a player is owed apart.
        var deepest = positions.Last(position =>
            !position.IsRunStart && !position.Unfinished && !position.Reachable);
        var view = RunView.For(recording, RunProgress.Empty, selectedFloor: deepest.Floor);
        var row = view.Rows.Single(candidate => candidate.Kind == RunViewRowKind.PlayFrom);

        Assert.False(row.Enabled);
        Assert.Equal(LibraryCopy.EarlierFightNotReplayable, row.Reason);
    }

    /// <summary>
    /// Continue is the affordance that used to walk straight into the wall: after the
    /// first fight it names the second, which no run in this repository can reach.
    /// </summary>
    [Theory]
    [MemberData(nameof(Recordings))]
    public void ContinueRefusesOnceTheOnlyReachableFightHasBeenPlayed(string fileName)
    {
        var recording = Load(fileName);
        var played = RunProgress.Empty.WithFightPlayed(recording.RunId, 1);

        var row = RunView.For(recording, played).Rows
            .Single(candidate => candidate.Kind == RunViewRowKind.Continue);

        Assert.Equal(2, row.Fight);
        Assert.False(row.Enabled);
        Assert.Equal(LibraryCopy.EarlierFightNotReplayable, row.Reason);
    }

    /// <summary>The plate under the game's own run history offers the furthest floor a
    /// player can be stood at, rather than the furthest the run reached.</summary>
    [Theory]
    [MemberData(nameof(Recordings))]
    public void TheRunHistoryPlateOffersTheFurthestReachableFloor(string fileName)
    {
        var recording = Load(fileName);
        var positions = RunView.PositionsIn(recording);

        var offered = positions.LastOrDefault(position => position.Playable);
        Assert.NotNull(offered);
        Assert.NotEqual(positions[^1].Floor, offered.Floor);
    }
}
