using System.Globalization;
using System.Reflection;
using System.Text;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// One way a decision can reach the game on this build, as the ledger names it.
/// </summary>
/// <param name="Kind">One of <see cref="DecisionSurface.LedgerKinds"/>.</param>
/// <param name="Identity">The line's key, as <see cref="DecisionSurface"/> spells it.</param>
/// <param name="Type">The game type the candidate is about: the member's declaring
/// type for a choice, the sender's for a message, the screen's for a screen.</param>
/// <param name="Member">The member's signature, for a choice's syncing member or a
/// message's sender.</param>
/// <param name="Discriminator">A choice's kind, or a message's type name.</param>
public sealed record LedgerCandidate(
    string Kind, string Identity, Type? Type = null, string? Member = null, string? Discriminator = null);

/// <summary>What a row of <see cref="EngineCommands"/> watches, beyond the member it is mapped onto.</summary>
public abstract record Observation
{
    /// <summary>Whether this observation accounts for the candidate.</summary>
    public abstract bool Claims(LedgerCandidate candidate);
}

/// <summary>A choice synced through <c>PlayerChoiceSynchronizer.SyncLocalChoice</c> by
/// a member of <paramref name="DeclaringType"/>: every member of it, or the one named;
/// every kind, or the one named.</summary>
public sealed record PlayerChoiceObservation(Type DeclaringType, string? Member = null, string? Kind = null) : Observation
{
    public override bool Claims(LedgerCandidate candidate) =>
        candidate.Kind == "player-choice" && candidate.Type == DeclaringType &&
        (Member is null || candidate.Member?.StartsWith(Member + "(", StringComparison.Ordinal) == true) &&
        (Kind is null || candidate.Discriminator == Kind);
}

/// <summary>A message the decision's own member sends: every sender of it, or the one named.</summary>
public sealed record MessageObservation(Type MessageType, Type? SenderType = null, string? SenderMember = null) : Observation
{
    public override bool Claims(LedgerCandidate candidate) =>
        candidate.Kind == "message" && candidate.Discriminator == MessageType.Name &&
        (SenderType is null || candidate.Type == SenderType) &&
        (SenderMember is null || candidate.Member?.StartsWith(SenderMember + "(", StringComparison.Ordinal) == true);
}

/// <summary>An overlay screen the decision answers or dismisses.</summary>
public sealed record ScreenObservation(Type Screen) : Observation
{
    public override bool Claims(LedgerCandidate candidate) =>
        candidate.Kind == "overlay-screen" && candidate.Type == Screen;
}

/// <summary>A room the decision is made in or moves the run into, by the ledger's name for it.</summary>
public sealed record RoomObservation(string Room) : Observation
{
    public override bool Claims(LedgerCandidate candidate) =>
        candidate.Kind == "room" && string.Equals(candidate.Identity, Room, StringComparison.Ordinal);
}

/// <summary>
/// The recorder's account of every way a decision can reach the game on this build,
/// candidate by candidate: claimed by a row of the command table, excused in writing,
/// or unclassified, which is the state a game update leaves a new one in and the
/// state <see cref="EngineCommands.Verify"/> refuses.
///
/// This is the standing per-build gate: the walks off the assembly are the
/// candidates, the table's rows and <see cref="Excused"/> are the claims, and the
/// committed record is the ledger a build is held to. A candidate is a way a
/// decision can arrive that the recorder's member patches would never see - a net
/// action, a synced choice, a message, a screen, a room - so the question here is
/// not "does a recording reach it" (that is the coverage number) but "does the
/// recorder know what it is".
/// </summary>
public static class DecisionLedger
{
    /// <summary>Where the ledger on this build is committed, relative to the repository root.</summary>
    public const string RecordPath = "scripts/decision-ledger.txt";

    /// <summary>How a candidate is accounted for.</summary>
    public sealed record Entry(LedgerCandidate Candidate, string Status, string Account)
    {
        public bool IsClassified => Status != Unclassified;

        public string Describe() => $"{Candidate.Kind,-16} {Candidate.Identity}  {Status} {Account}".TrimEnd();
    }

    public const string Claimed = "claimed";
    public const string ExcusedStatus = "excused";
    public const string Unclassified = "UNCLASSIFIED";

    /// <summary>
    /// Candidates no row claims, each with the class of reason and the reason. In the
    /// shape of <c>EngineCommands.Unmapped</c>: code, so a build is held to it and a
    /// reviewer sees an excusal appear in a diff. The classes are the enumeration
    /// plan's: <c>outcome-of</c> a claimed decision, <c>transport</c> plumbing that
    /// carries a decision another kind accounts for, <c>flavor</c>, <c>lobby</c>,
    /// <c>sync</c>, <c>checksum</c>, <c>multiplayer-only</c>, <c>ending</c>,
    /// <c>engine-driven</c>, <c>non-standard</c>, and <c>no-room</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, (string Class, string Reason)> Excused { get; } =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            // Messages that report what a claimed decision did, sent from inside it
            ["RewardObtainedMessage <- RewardSynchronizer.SyncLocalCardEvent(card, skipped)"] =
                ("outcome-of", "TakeCard: the card a claimed reward gave, announced after the decision"),
            ["RewardObtainedMessage <- RewardSynchronizer.SyncLocalObtainedGold(goldAmount)"] =
                ("outcome-of", "ClaimReward: the gold a claimed reward gave"),
            ["RewardObtainedMessage <- RewardSynchronizer.SyncLocalPotionEvent(potion, skipped)"] =
                ("outcome-of", "ClaimReward: the potion a claimed reward gave"),
            ["RewardObtainedMessage <- RewardSynchronizer.SyncLocalRelicEvent(relic, skipped)"] =
                ("outcome-of", "ClaimReward, TakeChestRelic: the relic a claimed reward or chest gave"),
            ["GoldLostMessage <- RewardSynchronizer.SyncLocalGoldLost(goldLost)"] =
                ("outcome-of", "ShopPurchase, ChooseEventOption: gold a claimed decision cost"),
            ["CardRemovedMessage <- RewardSynchronizer.DoLocalCardRemoval()"] =
                ("outcome-of", "ClaimReward, SelectCardFromScreen: the removal a claimed reward's screen made"),
            ["CrystalSphereRewardsMessage <- OneOffSynchronizer.DoLocalCrystalSphereRewards(owner, rng, revealed)"] =
                ("outcome-of", "RevealCrystalSphereCell: what the last reveal awarded"),
            ["TreasureChestOpenedMessage <- OneOffSynchronizer.DoLocalTreasureRoomRewards()"] =
                ("outcome-of", "MapMove: the chest a treasure room opens on the click after arrival, before anything is decided"),
            ["RewardSetSkippedMessage <- RewardsSetSynchronizer.BeforeLeavingRoom()"] =
                ("outcome-of", "SkipRewards: the room's exit declines what is left through the same SkipRewardsSet funnel the recorder watches"),
            ["RestSiteSkippedMessage <- RestSiteSynchronizer.BeforeLocalRestSiteExited()"] =
                ("outcome-of", "MapMove: leaving a rest site without resting is the map move that leaves it"),
            ["SharedEventOptionChosenMessage <- EventSynchronizer.ChooseSharedEventOption()"] =
                ("multiplayer-only", "a shared event's vote is resolved for the host's peers; a singleplayer run chooses through ChooseLocalOption"),

            // Plumbing that carries a decision another kind of the ledger accounts for
            ["PlayerChoiceMessage <- PlayerChoiceSynchronizer.SyncLocalChoice(player, choiceId, result)"] =
                ("transport", "carries every synced choice; the player-choice kind accounts for each by its member"),
            ["RequestEnqueueActionMessage <- ActionQueueSynchronizer.RequestEnqueue(action)"] =
                ("transport", "carries a game action to a host; the net-action kind accounts for each by its type"),
            ["ActionEnqueuedMessage <- ActionQueueSynchronizer.EnqueueAction(action, actionOwnerId)"] =
                ("transport", "a host's echo of an enqueued action; the net-action kind accounts for the action"),
            ["HookActionEnqueuedMessage <- ActionQueueSynchronizer.EnqueueHookAction(gameAction)"] =
                ("transport", "the engine's own hook action, which nobody decides"),
            ["RequestEnqueueHookActionMessage <- ActionQueueSynchronizer.RequestEnqueueHookAction(action)"] =
                ("transport", "the engine's own hook action, which nobody decides"),
            ["RequestResumeActionAfterPlayerChoiceMessage <- ActionQueueSynchronizer.RequestResumeActionAfterPlayerChoice(action)"] =
                ("transport", "resumes an action after a choice the player-choice kind accounts for"),
            ["ResumeActionAfterPlayerChoiceMessage <- ActionQueueSynchronizer.ResumeActionAfterPlayerChoice(id)"] =
                ("transport", "resumes an action after a choice the player-choice kind accounts for"),
            ["PeerInputMessage <- PeerInputSynchronizer.SendSyncMessage()"] =
                ("transport", "a peer's cursor and input, drawn and never decided"),
            ["HeartbeatRequestMessage <- NetQualityTracker.Update()"] = ("transport", "connection quality"),
            ["HeartbeatResponseMessage <- NetQualityTracker.HandleHeartbeatRequestMessage(message, senderId)"] =
                ("transport", "connection quality"),

            // What players show each other, which changes no state
            ["EndTurnPingMessage <- FlavorSynchronizer.SendEndTurnPing()"] = ("flavor", "a nudge, not a decision"),
            ["MapPingMessage <- FlavorSynchronizer.SendMapPing(coord)"] = ("flavor", "a ping on the map, not a move"),
            ["ReactionMessage <- ReactionSynchronizer.SendLocalReaction(type, mouseScreenPos)"] = ("flavor", "an emote"),
            ["RestSiteOptionHoveredMessage <- RestSiteSynchronizer.SendHoverMessage()"] = ("flavor", "a hover, not a choice"),
            ["MapDrawingMessage <- NMapDrawings.QueueOrSendEvent(ev)"] = ("flavor", "a drawing on the map"),
            ["MapDrawingMessage <- NMapDrawings.SendSyncMessage()"] = ("flavor", "a drawing on the map"),
            ["MapDrawingModeChangedMessage <- NMapDrawings.SetDrawingModeLocal(drawingMode)"] = ("flavor", "the drawing tool"),
            ["ClearMapDrawingsMessage <- NMapDrawings.ClearDrawnLinesLocal()"] = ("flavor", "clearing drawings"),

            // Before a run exists, or between two of them
            ["ClientLoadJoinRequestMessage <- JoinFlow.AttemptLoadJoin()"] = ("lobby", "joining a saved run"),
            ["ClientLoadJoinResponseMessage <- LoadRunLobby.HandleClientLoadJoinRequestMessage(message, senderId)"] = ("lobby", "joining a saved run"),
            ["ClientLobbyJoinRequestMessage <- JoinFlow.AttemptJoin()"] = ("lobby", "joining a lobby"),
            ["ClientLobbyJoinResponseMessage <- StartRunLobby.HandleClientLobbyJoinRequestMessage(message, senderId)"] = ("lobby", "joining a lobby"),
            ["ClientRejoinRequestMessage <- JoinFlow.AttemptRejoin()"] = ("lobby", "rejoining"),
            ["InitialGameInfoMessage <- LoadRunLobby.OnConnectedToClientAsHost(playerId)"] = ("lobby", "the host's game info on connect"),
            ["InitialGameInfoMessage <- RunLobby.OnConnectedToClientAsHost(playerId)"] = ("lobby", "the host's game info on connect"),
            ["InitialGameInfoMessage <- StartRunLobby.OnConnectedToClientAsHost(playerId)"] = ("lobby", "the host's game info on connect"),
            ["LobbyAscensionChangedMessage <- StartRunLobby.SyncAscensionChange(ascension)"] = ("lobby", "run setup, captured as the run's identity"),
            ["LobbyBeginLoadedRunMessage <- LoadRunLobby.TryBeginRunForAllPlayers()"] = ("lobby", "starting a saved run"),
            ["LobbyBeginRunMessage <- StartRunLobby.BeginRunForAllPlayers(seed, modifiers)"] = ("lobby", "starting a run, captured as the run's identity"),
            ["LobbyModifiersChangedMessage <- StartRunLobby.SetModifiers(modifiers)"] = ("lobby", "run setup, captured as the run's identity"),
            ["LobbyPlayerChangedCharacterMessage <- StartRunLobby.SetLocalCharacter(character)"] = ("lobby", "run setup, captured as the run's identity"),
            ["LobbyPlayerSetReadyMessage <- LoadRunLobby.SetReady(ready)"] = ("lobby", "readiness"),
            ["LobbyPlayerSetReadyMessage <- StartRunLobby.SetReady(ready)"] = ("lobby", "readiness"),
            ["LobbySeedChangedMessage <- StartRunLobby.SetSeed(seed)"] = ("lobby", "run setup, captured as the run's identity"),
            ["PlayerJoinedMessage <- StartRunLobby.HandleClientLobbyJoinRequestMessage(message, senderId)"] = ("lobby", "membership"),
            ["PlayerLeftMessage <- LoadRunLobby.OnDisconnectedFromClientAsHost(playerId, info)"] = ("lobby", "membership"),
            ["PlayerLeftMessage <- RunLobby.OnDisconnectedFromClientAsHost(playerId, info)"] = ("lobby", "membership"),
            ["PlayerLeftMessage <- StartRunLobby.OnDisconnectedFromClientAsHost(playerId, info)"] = ("lobby", "membership"),
            ["PlayerReconnectedMessage <- LoadRunLobby.HandleClientLoadJoinRequestMessage(message, senderId)"] = ("lobby", "membership"),

            // State the host pushes to peers, never a decision
            ["SyncPlayerDataMessage <- CombatStateSynchronizer.StartSync()"] = ("sync", "the host's combat state for a peer"),
            ["SyncRngMessage <- CombatStateSynchronizer.StartSync()"] = ("sync", "the host's random streams for a peer"),
            ["ChecksumDataMessage <- ChecksumTracker.GenerateChecksum(context, action)"] = ("checksum", "desync detection"),
            ["StateDivergenceMessage <- ChecksumTracker.CompareChecksums(localChecksum, remoteChecksum, remoteId)"] = ("checksum", "desync detection"),
            ["StateDivergenceMessage <- ChecksumTracker.LogStateDivergence(localChecksum, message, remoteId, checksumIndex)"] = ("checksum", "desync detection"),

            // Screens and rooms that are not a decision
            ["NGameOverScreen"] = ("ending", "the run is over; the recorder finishes on RunManager.OnEnded before it is drawn"),
            ["RoomType.Unassigned"] = ("no-room", "the map's placeholder type for a point no room has been dealt to"),
        };

    /// <summary>Every candidate on this build with how it is accounted for, in the ledger's order.</summary>
    public static IReadOnlyList<Entry> Entries()
    {
        var entries = new List<Entry>();
        foreach (var kind in DecisionSurface.LedgerKinds)
        {
            foreach (var candidate in Candidates(kind)) entries.Add(Classify(candidate));
        }

        return entries;
    }

    /// <summary>The candidates of one kind, structured for the observations to match.</summary>
    public static IReadOnlyList<LedgerCandidate> Candidates(string kind) => kind switch
    {
        "net-action" => DecisionSurface.NetActions()
            .Select(name => new LedgerCandidate(kind, name))
            .ToList(),
        "player-choice" => DecisionSurface.PlayerChoiceSites()
            .Select(site => new LedgerCandidate(
                kind, DecisionSurface.PlayerChoiceIdentity(site.Kind, site.Member),
                site.Member.DeclaringType, EntryPointSignature.Of(site.Member), site.Kind))
            .ToList(),
        "message" => DecisionSurface.MessageSites()
            .Select(site => new LedgerCandidate(
                kind, DecisionSurface.MessageIdentity(site.Message, site.Sender),
                site.Sender.DeclaringType, EntryPointSignature.Of(site.Sender), site.Message.Name))
            .ToList(),
        "overlay-screen" => DecisionSurface.OverlayScreenTypes()
            .Select(type => new LedgerCandidate(kind, type.Name, type))
            .ToList(),
        "room" => DecisionSurface.Rooms()
            .Select(name => new LedgerCandidate(kind, name))
            .ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a ledger kind"),
    };

    /// <summary>How one candidate is accounted for: by the rows whose observations
    /// claim it, by the net-action table, by an excusal, or not at all.</summary>
    public static Entry Classify(LedgerCandidate candidate)
    {
        if (candidate.Kind == "net-action")
        {
            var claim = NetActionClaims.All.FirstOrDefault(pair => pair.Key.Name == candidate.Identity).Value;
            return claim is null
                ? new Entry(candidate, Unclassified, "")
                : claim.Disposition == NetActionClaims.Disposition.Claimed
                    ? new Entry(candidate, Claimed, claim.WatchedBy)
                    : new Entry(candidate, ExcusedStatus, $"{Kebab(claim.Disposition)}: {claim.WatchedBy}");
        }

        var rows = EngineCommands.All
            .Where(command => command.Observes.Any(observation => observation.Claims(candidate)))
            .Select(command => command.Verb.ToString())
            .ToList();
        if (rows.Count > 0) return new Entry(candidate, Claimed, string.Join(", ", rows));

        return Excused.TryGetValue(candidate.Identity, out var excuse)
            ? new Entry(candidate, ExcusedStatus, $"{excuse.Class}: {excuse.Reason}")
            : new Entry(candidate, Unclassified, "");
    }

    /// <summary>Every excusal that names no candidate this build offers: a sentence
    /// about nothing, which comes out.</summary>
    public static IReadOnlyList<string> StaleExcusals()
    {
        var offered = DecisionSurface.LedgerKinds
            .SelectMany(Candidates)
            .Select(candidate => candidate.Identity)
            .ToHashSet(StringComparer.Ordinal);
        return Excused.Keys.Where(identity => !offered.Contains(identity)).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>The committed record: one line per candidate under its kind.</summary>
    public static string Record()
    {
        var text = new StringBuilder();
        text.AppendLine("# Every way a decision can reach the game on this build - each game action a net");
        text.AppendLine("# action becomes, each choice the client syncs and where, each message it sends and");
        text.AppendLine("# who sends it, each overlay screen, each room - walked off the game assembly by");
        text.AppendLine("# DecisionSurface, with the recorder's account of it: claimed by the rows of the");
        text.AppendLine("# engine command table that observe it, or excused in DecisionLedger with a class");
        text.AppendLine("# and a reason. A candidate accounted for neither way is UNCLASSIFIED and fails");
        text.AppendLine("# engine-commands, which is what a game update that adds one meets first.");
        text.AppendLine("# Regenerate with");
        text.AppendLine("#   ./scripts/arbiter engine-commands --update");
        var entries = Entries();
        foreach (var kind in DecisionSurface.LedgerKinds)
        {
            var ofKind = entries.Where(entry => entry.Candidate.Kind == kind).ToList();
            text.AppendLine();
            text.AppendLine($"# {kind} ({ofKind.Count.ToString(CultureInfo.InvariantCulture)})");
            foreach (var entry in ofKind) text.AppendLine(entry.Describe());
        }

        return text.ToString();
    }

    private static string Kebab(NetActionClaims.Disposition disposition) => disposition switch
    {
        NetActionClaims.Disposition.EngineDriven => "engine-driven",
        NetActionClaims.Disposition.NonStandard => "non-standard",
        _ => disposition.ToString().ToLowerInvariant(),
    };
}
