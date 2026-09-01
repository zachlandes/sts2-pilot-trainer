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
        Func<ReplayManifest, ReplayManifest> Apply);

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
            ReorderPlays),

        new("substitute-same-cost",
            "Replaces the nominated card play with a different same-cost card selected by the control.",
            VideoOnlyVerdict.Undetected,
            "Energy conservation and hand accounting both balance, because the substitute costs the same. The " +
            "damage arithmetic balances too unless the enemy's health is read frame by frame, which the earlier " +
            "video-only pipeline did not do.",
            SubstituteSameCost),

        new("omit-play",
            "Drops the nominated card play entirely.",
            VideoOnlyVerdict.Detected,
            "Energy and hand counts no longer balance against the declared line. Included as a control on the " +
            "control: an arbiter that rejected only the subtle " +
            "corruptions and let this one through would be broken in an interesting way.",
            OmitPlay),

        new("wrong-opening-choice",
            "Takes a different blessing at the run's opening event.",
            VideoOnlyVerdict.Detected,
            "The different opening option changes generated setup before combat. Included because it corrupts " +
            "the history far from the turn being checked, which tests that divergence is caught where it surfaces.",
            WrongOpeningChoice),
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
            second with
            {
                Seq = first.Seq,
                Args = WithArg(second.Args, "hand_index", secondInitialIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Evidence = first.Evidence,
                Note = "reordered by a negative control",
            },
            first with
            {
                Seq = second.Seq,
                Args = WithArg(first.Args, "hand_index", firstAfterSecondIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Evidence = second.Evidence,
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
        actions.Select((a, i) => a with { Seq = i }).ToList();

    /// <summary>
    /// Moves checkpoints back past a removed action so they stay attached to the same
    /// moment. Without this the omission control would fail on a sequence number
    /// rather than on the state, which is a less interesting reason to fail.
    /// </summary>
    private static IReadOnlyList<Checkpoint> ShiftCheckpoints(IReadOnlyList<Checkpoint> checkpoints, int removedSeq) =>
        checkpoints
            .Where(c => c.AfterSeq != removedSeq)
            .Select(c => c.AfterSeq > removedSeq ? c with { AfterSeq = c.AfterSeq - 1 } : c)
            .ToList();
}
