namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Deliberate damage to a known-good action history, for proving the arbiter
/// rejects things.
///
/// Each corruption records what a video-only consistency check would conclude about
/// it, because that is the whole argument for owning an engine at all. Checks that
/// can be done from the footage alone - energy spent equals card costs, hand count
/// balances, damage taken equals intent minus block - are cheap and catch a lot.
/// They also pass, silently and completely, on two corruptions that change the run:
/// reordering plays, and substituting a card of the same cost. Those are the ones
/// worth building an arbiter for.
/// </summary>
public static class Corruption
{
    public sealed record Case(
        string Name,
        string What,
        VideoOnlyVerdict VideoOnly,
        string WhyVideoOnly,
        Func<ReplayManifest, ReplayManifest> Apply)
    {
        /// <summary>
        /// What a history must contain for this control to damage anything, in words.
        ///
        /// A control aimed at a decision a history never made is neither a pass nor a
        /// failure - it is a control with nothing to do - and saying which is which
        /// out loud is what stops a history from quietly dodging one. The publication
        /// gate is run against a reconstruction that makes every one of them apply.
        /// </summary>
        public required string Requires { get; init; }

        /// <summary>Whether this history contains the decision the control damages.</summary>
        public required Func<ReplayManifest, bool> AppliesTo { get; init; }
    }

    public enum VideoOnlyVerdict
    {
        /// <summary>Arithmetic on the footage alone catches it.</summary>
        Detected,

        /// <summary>Arithmetic on the footage alone accepts it. The interesting case.</summary>
        Undetected,
    }

    public static IReadOnlyList<Case> All =>
    [
        new("reorder-plays",
            "Plays the same two cards in the opposite order, adjusting hand indices so both remain valid.",
            VideoOnlyVerdict.Undetected,
            "The same cards are played, aggregate energy and hand counts are unchanged, and the final visible " +
            "damage and block totals agree. The intermediate state and hidden pile order still depend on order.",
            ReorderPlays)
        {
            Requires = "two card plays",
            AppliesTo = manifest => manifest.Actions.Count(a => a.Verb == ActionVerb.PlayCard) >= 2,
        },

        new("substitute-same-cost",
            "Replaces the nominated card play with a different same-cost card selected by the control.",
            VideoOnlyVerdict.Undetected,
            "Energy conservation and hand accounting both balance, because the substitute costs the same. The " +
            "damage arithmetic balances too unless the enemy's health is read frame by frame, which the earlier " +
            "video-only pipeline did not do.",
            SubstituteSameCost)
        {
            Requires = "a card play",
            AppliesTo = manifest => manifest.Actions.Any(a => a.Verb == ActionVerb.PlayCard),
        },

        new("omit-play",
            "Drops the nominated card play entirely.",
            VideoOnlyVerdict.Detected,
            "Energy and hand counts no longer balance against the declared line. Included as a control on the " +
            "control: an arbiter that rejected only the subtle " +
            "corruptions and let this one through would be broken in an interesting way.",
            OmitPlay)
        {
            Requires = "a card play",
            AppliesTo = manifest => manifest.Actions.Any(a => a.Verb == ActionVerb.PlayCard),
        },

        new("wrong-opening-choice",
            "Takes a different blessing at the run's opening event.",
            VideoOnlyVerdict.Detected,
            "The different opening option changes generated setup before combat. Included because it corrupts " +
            "the history far from the turn being checked, which tests that divergence is caught where it surfaces.",
            WrongOpeningChoice)
        {
            Requires = "an opening blessing",
            AppliesTo = manifest => manifest.Actions.Any(a => a.Verb == ActionVerb.ChooseNeowBlessing),
        },

        new("decline-a-claimed-reward",
            "Turns the first reward the player took into a dismissal of the whole loot screen.",
            VideoOnlyVerdict.Detected,
            "The loot screen's entries disappear one at a time as they are taken, and what they gave shows up " +
            "in the coin counter, the potion belt and the deck badge. Included as the control on the reward " +
            "verbs: an arbiter that could not tell taking loot from declining it would let the two states that " +
            "differ most obviously pass for each other.",
            DeclineAClaimedReward)
        {
            Requires = "a claimed reward",
            AppliesTo = manifest => manifest.Actions.Any(a => a.Verb == ActionVerb.ClaimReward),
        },

        new("take-a-different-card",
            "Takes the alternative card the reward offered instead of the one the player took.",
            VideoOnlyVerdict.Undetected,
            "Every counter the loot window moves balances either way: the deck badge goes 11 to 12, the gold is " +
            "untouched, and no energy or hand arithmetic in the fight before it changes. Only the card's face " +
            "distinguishes the two, and the deck it joins is not shown again for two minutes.",
            TakeADifferentCard)
        {
            Requires = "a card reward nominating another card it offered",
            AppliesTo = manifest => manifest.Actions.Any(
                a => a.Verb == ActionVerb.TakeCard && a.Args.ContainsKey(AlternativeCardId)),
        },

        new("enchant-a-different-card",
            "Enchants a different, identical copy of the same card on the event's selection screen.",
            VideoOnlyVerdict.Undetected,
            "The two readings differ by which of three visually identical Defends carries the enchantment. " +
            "Gold, maximum health, deck size and every card face are the same afterwards. What separates them " +
            "is where the marked copy lands two floors later, which is a fact about the shuffle rather than " +
            "anything a frame of the event screen shows.",
            EnchantADifferentCard)
        {
            Requires = "a card picked off a screen nominating another copy of the same card",
            AppliesTo = manifest => manifest.Actions.Any(
                a => a.Verb == ActionVerb.SelectCardFromScreen && a.Args.ContainsKey(AlternativeOptionIndex)),
        },

        new("choose-a-different-event-option",
            "Takes a different option at the event, one the player could afford and did not take.",
            VideoOnlyVerdict.Detected,
            "The options carry their own prices and the coin counter settles on a different number. Included " +
            "because an event's effect reaches the deck rather than the fight in front of it, so it tests that " +
            "a corruption is caught where it surfaces rather than where it was made.",
            ChooseADifferentEventOption)
        {
            Requires = "an event choice",
            AppliesTo = manifest => manifest.Actions.Any(a => a.Verb == ActionVerb.ChooseEventOption),
        },

        new("target-the-other-enemy",
            "Aims a card at the other living enemy.",
            VideoOnlyVerdict.Undetected,
            "Nothing about the player's side changes: the same card leaves the same hand for the same energy, " +
            "and total damage dealt is identical. Only the two enemies' health bars say which one took it, and " +
            "in this recording one of them spends part of the fight behind a chat overlay.",
            TargetTheOtherEnemy)
        {
            Requires = "a play that recorded a target",
            AppliesTo = manifest => manifest.Actions.Any(
                a => a.Verb == ActionVerb.PlayCard && a.Args.ContainsKey("target_index")),
        },

        new("move-to-a-different-node",
            "Walks to a different node the map made reachable from the same one.",
            VideoOnlyVerdict.Detected,
            "The map screen rings the node that was chosen and the room that follows is a different kind of " +
            "room. Included because a map move is the one action that decides what the next several actions " +
            "will even be about.",
            MoveToADifferentNode)
        {
            Requires = "a map move nominating a reachable sibling",
            AppliesTo = manifest => manifest.Actions.Any(
                a => a.Verb == ActionVerb.MapMove && a.Args.ContainsKey(AlternativeColumn)),
        },
    ];

    private static ReplayManifest ReorderPlays(ReplayManifest manifest)
    {
        var actions = manifest.Actions.ToList();
        var plays = actions.Where(a => a.Verb == ActionVerb.PlayCard).ToList();
        if (plays.Count < 2) throw new ManifestException("reorder-plays needs at least two card plays.");

        var first = plays[0];
        var second = plays[1];

        // Both cards are re-indexed to where they sit in the *original* hand, so that
        // each play is individually legal and the driver's card-identity check passes.
        // A corruption the driver catches on a bad index would prove nothing about
        // whether the engine notices the reordering.
        var firstIndex = int.Parse(first.Args["hand_index"], System.Globalization.CultureInfo.InvariantCulture);
        var secondIndex = int.Parse(second.Args["hand_index"], System.Globalization.CultureInfo.InvariantCulture);
        var secondInitialIndex = secondIndex + (firstIndex <= secondIndex ? 1 : 0);
        var firstAfterSecondIndex = firstIndex - (secondInitialIndex < firstIndex ? 1 : 0);
        var reordered = new List<ActionRecord>
        {
            MoveToSequence(second, first.Seq, first.Evidence) with
            {
                Args = WithArg(second.Args, "hand_index", secondInitialIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Note = "reordered by a negative control",
            },
            MoveToSequence(first, second.Seq, second.Evidence) with
            {
                Args = WithArg(first.Args, "hand_index", firstAfterSecondIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Note = "reordered by a negative control",
            },
        };

        var index = actions.IndexOf(first);
        actions[index] = reordered[0];
        actions[actions.IndexOf(second)] = reordered[1];
        return manifest with { RunId = manifest.RunId + "+reorder-plays", Actions = actions };
    }

    private static ReplayManifest SubstituteSameCost(ReplayManifest manifest)
    {
        var actions = manifest.Actions.ToList();
        var target = NominatedPlay(actions, "substitute-same-cost");

        var substituteCard = target.Args.GetValueOrDefault(
            "negative_control_substitute_card_id", "CARD.STRIKE_IRONCLAD");
        var substituteIndex = target.Args.GetValueOrDefault("negative_control_substitute_hand_index", "0");

        // A substitution that puts back the card that was already there damages
        // nothing, and an arbiter that accepted it would be reported as having failed
        // to reject a corruption that was never made. Refusing here keeps the control
        // honest about whether it corrupted anything at all.
        if (substituteCard == target.Args.GetValueOrDefault("card_id") &&
            substituteIndex == target.Args.GetValueOrDefault("hand_index"))
        {
            throw new ManifestException(
                $"substitute-same-cost would replace action {target.Seq} with the card it already plays, so " +
                "it would corrupt nothing. The manifest must mark a play with " +
                "'negative_control_substitute_card_id' naming a genuinely different card of the same cost.");
        }

        var args = target.Args
            .Where(pair => !pair.Key.StartsWith("negative_control_", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var replaced = target with
        {
            Args = WithArg(WithArg(args, "card_id", substituteCard), "hand_index", substituteIndex),
            Note = "substituted by a negative control: a different card of the same energy cost",
        };
        actions[actions.IndexOf(target)] = replaced;
        return manifest with { RunId = manifest.RunId + "+substitute-same-cost", Actions = actions };
    }

    private static ReplayManifest OmitPlay(ReplayManifest manifest)
    {
        var actions = manifest.Actions.ToList();
        var target = NominatedPlay(actions, "omit-play");

        actions.Remove(target);
        return manifest with
        {
            RunId = manifest.RunId + "+omit-play",
            Actions = Renumber(actions),
            Checkpoints = ShiftCheckpoints(manifest.Checkpoints, target.Seq),
        };
    }

    private static ReplayManifest WrongOpeningChoice(ReplayManifest manifest)
    {
        var actions = manifest.Actions.ToList();
        var target = actions.FirstOrDefault(a => a.Verb == ActionVerb.ChooseNeowBlessing)
            ?? throw new ManifestException("wrong-opening-choice needs an opening event choice.");

        var current = target.Args.GetValueOrDefault("option_index", "0");
        var different = current == "0" ? "1" : "0";
        actions[actions.IndexOf(target)] = target with
        {
            Args = WithArg(target.Args, "option_index", different),
            Note = "changed by a negative control",
        };
        return manifest with { RunId = manifest.RunId + "+wrong-opening-choice", Actions = actions };
    }

    /// <summary>
    /// The first reward the player took becomes a dismissal of the whole screen.
    ///
    /// It is the reward verbs' counterpart to <c>omit-play</c>: the states either
    /// side of it are as far apart as a loot screen can put them, and an arbiter that
    /// accepted this one would not be able to tell a claimed reward from a declined
    /// one at all.
    /// </summary>
    private static ReplayManifest DeclineAClaimedReward(ReplayManifest manifest)
    {
        var actions = manifest.Actions.ToList();
        var claim = actions.FirstOrDefault(a => a.Verb == ActionVerb.ClaimReward)
            ?? throw new ManifestException("decline-a-claimed-reward needs a claimed reward.");

        actions[actions.IndexOf(claim)] = claim with
        {
            Verb = ActionVerb.SkipRewards,
            Args = new SortedDictionary<string, string>(StringComparer.Ordinal),
            Note = "changed by a negative control: the reward is declined instead of taken",
        };
        return manifest with { RunId = manifest.RunId + "+decline-a-claimed-reward", Actions = actions };
    }

    /// <summary>Takes the alternative the manifest records the card reward as having
    /// also offered.</summary>
    private static ReplayManifest TakeADifferentCard(ReplayManifest manifest)
    {
        var actions = manifest.Actions.ToList();
        var take = actions.FirstOrDefault(a =>
                       a.Verb == ActionVerb.TakeCard &&
                       a.Args.ContainsKey(AlternativeCardId))
            ?? throw new ManifestException(
                "take-a-different-card needs a TakeCard nominating another card the reward offered, through " +
                $"'{AlternativeCardId}' and '{AlternativeOptionIndex}'.");

        var args = WithoutControls(take.Args);
        actions[actions.IndexOf(take)] = take with
        {
            Args = WithArg(
                WithArg(args, "card_id", take.Args[AlternativeCardId]),
                "option_index", take.Args[AlternativeOptionIndex]),
            Note = "changed by a negative control: the other card the reward offered",
        };
        return manifest with { RunId = manifest.RunId + "+take-a-different-card", Actions = actions };
    }

    /// <summary>Enchants a different copy of the same card - the subtlest corruption
    /// this history admits, because the two copies are indistinguishable on screen.</summary>
    private static ReplayManifest EnchantADifferentCard(ReplayManifest manifest)
    {
        var actions = manifest.Actions.ToList();
        var pick = actions.FirstOrDefault(a =>
                       a.Verb == ActionVerb.SelectCardFromScreen &&
                       a.Args.ContainsKey(AlternativeOptionIndex))
            ?? throw new ManifestException(
                "enchant-a-different-card needs a SelectCardFromScreen nominating another copy of the same " +
                $"card through '{AlternativeOptionIndex}'.");

        actions[actions.IndexOf(pick)] = pick with
        {
            Args = WithArg(
                WithoutControls(pick.Args), "option_index", pick.Args[AlternativeOptionIndex]),
            Note = "changed by a negative control: a different copy of the same card",
        };
        return manifest with { RunId = manifest.RunId + "+enchant-a-different-card", Actions = actions };
    }

    private static ReplayManifest ChooseADifferentEventOption(ReplayManifest manifest)
    {
        var actions = manifest.Actions.ToList();
        var choice = actions.FirstOrDefault(a => a.Verb == ActionVerb.ChooseEventOption)
            ?? throw new ManifestException("choose-a-different-event-option needs an event choice.");

        var current = int.Parse(
            choice.Args["option_index"], System.Globalization.CultureInfo.InvariantCulture);
        var different = current == 0 ? 1 : current - 1;
        actions[actions.IndexOf(choice)] = choice with
        {
            Args = WithArg(
                choice.Args, "option_index",
                different.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Note = "changed by a negative control",
        };
        return manifest with { RunId = manifest.RunId + "+choose-a-different-event-option", Actions = actions };
    }

    /// <summary>
    /// Swaps which enemy a card was aimed at.
    ///
    /// Only plays that recorded a target are candidates, and a target is only recorded
    /// when more than one enemy is alive - so the other index is always a living
    /// enemy, and the corruption is a line the player could have taken rather than an
    /// illegal one the driver would reject on its face.
    /// </summary>
    private static ReplayManifest TargetTheOtherEnemy(ReplayManifest manifest)
    {
        var actions = manifest.Actions.ToList();
        var targeted = actions.FirstOrDefault(a =>
                           a.Verb == ActionVerb.PlayCard && a.Args.ContainsKey("target_index"))
            ?? throw new ManifestException("target-the-other-enemy needs a play that recorded a target.");

        var current = int.Parse(
            targeted.Args["target_index"], System.Globalization.CultureInfo.InvariantCulture);
        var other = current == 0 ? 1 : 0;
        actions[actions.IndexOf(targeted)] = targeted with
        {
            Args = WithArg(
                targeted.Args, "target_index",
                other.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Note = "changed by a negative control",
        };
        return manifest with { RunId = manifest.RunId + "+target-the-other-enemy", Actions = actions };
    }

    /// <summary>Walks to the sibling node the manifest records as also reachable.</summary>
    private static ReplayManifest MoveToADifferentNode(ReplayManifest manifest)
    {
        var actions = manifest.Actions.ToList();
        var move = actions.FirstOrDefault(a =>
                       a.Verb == ActionVerb.MapMove && a.Args.ContainsKey(AlternativeColumn))
            ?? throw new ManifestException(
                $"move-to-a-different-node needs a MapMove nominating a reachable sibling through " +
                $"'{AlternativeColumn}'.");

        actions[actions.IndexOf(move)] = move with
        {
            Args = WithArg(WithoutControls(move.Args), "column", move.Args[AlternativeColumn]),
            Note = "changed by a negative control: a different node on the same row",
        };
        return manifest with { RunId = manifest.RunId + "+move-to-a-different-node", Actions = actions };
    }

    /// <summary>Argument names a manifest uses to nominate the alternative a control
    /// should take. Kept here because the controls are the only readers.</summary>
    public const string AlternativeCardId = "negative_control_alternative_card_id";

    public const string AlternativeOptionIndex = "negative_control_alternative_option_index";

    public const string AlternativeColumn = "negative_control_alternative_column";

    private static IReadOnlyDictionary<string, string> WithoutControls(
        IReadOnlyDictionary<string, string> args) =>
        args.Where(pair => !pair.Key.StartsWith("negative_control_", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>
    /// Which play the history controls damage.
    ///
    /// A manifest may nominate one, by carrying the substitution pair the fixture
    /// generator writes onto it, and when it does that nomination wins. The
    /// alternative - always taking the last play - quietly stops being a corruption
    /// the moment a history is extended past the moment it was written for: the last
    /// play of a fight that runs to its end is the killing blow, which no checkpoint
    /// sits on and whose omission simply leaves a shorter, self-consistent history.
    /// Falling back to the last play keeps the controls usable on a manifest that
    /// nominates nothing, which is every reconstructed one.
    /// </summary>
    public static ActionRecord NominatedPlay(IReadOnlyList<ActionRecord> actions, string control = "this control")
    {
        var plays = actions.Where(action => action.Verb == ActionVerb.PlayCard).ToList();
        return plays.LastOrDefault(action => action.Args.ContainsKey("negative_control_substitute_card_id"))
            ?? plays.LastOrDefault()
            ?? throw new ManifestException($"{control} needs a card play.");
    }

    private static IReadOnlyDictionary<string, string> WithArg(
        IReadOnlyDictionary<string, string> args, string key, string value)
    {
        var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in args) copy[k] = v;
        copy[key] = value;
        return copy;
    }

    private static IReadOnlyList<ActionRecord> Renumber(IReadOnlyList<ActionRecord> actions) =>
        actions.Select((action, sequence) => MoveToSequence(action, sequence, action.Evidence)).ToList();

    private static ActionRecord MoveToSequence(
        ActionRecord action, int sequence, FactEvidence? observedEvidence) => action with
        {
            Seq = sequence,
            Evidence = action.Source == FactSource.Captured && action.Evidence is { } capturedEvidence
                ? capturedEvidence with { ActionOrdinal = sequence }
                : observedEvidence,
        };

    /// <summary>
    /// Moves checkpoints back past a removed action so they stay attached to the same
    /// moment. Without this the omission control would fail on a sequence number
    /// rather than on the state, which is a less interesting reason to fail.
    /// </summary>
    private static IReadOnlyList<Checkpoint> ShiftCheckpoints(IReadOnlyList<Checkpoint> checkpoints, int removedSeq) =>
        checkpoints
            .Where(c => c.AfterSeq != removedSeq)
            .Select(c => c.AfterSeq > removedSeq ? MoveCheckpointToSequence(c, c.AfterSeq - 1) : c)
            .ToList();

    // A captured field's action_ordinal is a coordinate into the history, so it moves
    // with the checkpoint. A video timestamp is not, and stays where it was.
    private static Checkpoint MoveCheckpointToSequence(Checkpoint checkpoint, int sequence) => checkpoint with
    {
        AfterSeq = sequence,
        Expect = checkpoint.Expect.ToDictionary(
            entry => entry.Key,
            entry => entry.Value is { Source: FactSource.Captured, Evidence: { } evidence }
                ? entry.Value with { Evidence = evidence with { ActionOrdinal = sequence } }
                : entry.Value,
            StringComparer.Ordinal),
    };
}
