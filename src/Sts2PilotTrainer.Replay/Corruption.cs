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
            "Energy spent is unchanged (1 + 2 = 2 + 1), the hand still goes from five cards to three, and the " +
            "damage arithmetic is untouched. Nothing measurable in a frame distinguishes the two orders - yet " +
            "order is exactly what the game's run-persistent RNG streams are sensitive to.",
            ReorderPlays),

        new("substitute-same-cost",
            "Replaces the Defend with the Strike beside it. Both cost 1.",
            VideoOnlyVerdict.Undetected,
            "Energy conservation and hand accounting both balance, because the substitute costs the same. The " +
            "damage arithmetic balances too unless the enemy's health is read frame by frame, which the earlier " +
            "video-only pipeline did not do.",
            SubstituteSameCost),

        new("omit-play",
            "Drops the Defend entirely.",
            VideoOnlyVerdict.Detected,
            "Energy would be left at 1 with nothing to account for it, and the hand would end at four cards " +
            "instead of three. Included as a control on the control: an arbiter that rejected only the subtle " +
            "corruptions and let this one through would be broken in an interesting way.",
            OmitPlay),

        new("wrong-opening-choice",
            "Takes a different blessing at the run's opening event.",
            VideoOnlyVerdict.Detected,
            "The chosen blessing changes maximum health on screen within seconds. Included because it corrupts " +
            "the history far from the turn being checked, which tests that a divergence is caught where it " +
            "surfaces rather than where it happened.",
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
        var reordered = new List<ActionRecord>
        {
            second with { Seq = first.Seq, Args = WithArg(second.Args, "hand_index", "4"), Note = "reordered by a negative control" },
            first with { Seq = second.Seq, Args = WithArg(first.Args, "hand_index", "1"), Note = "reordered by a negative control" },
        };

        var index = actions.IndexOf(first);
        actions[index] = reordered[0];
        actions[actions.IndexOf(second)] = reordered[1];
        return manifest with { RunId = manifest.RunId + "+reorder-plays", Actions = actions };
    }

    private static ReplayManifest SubstituteSameCost(ReplayManifest manifest)
    {
        var actions = manifest.Actions.ToList();
        var target = actions.LastOrDefault(a => a.Verb == ActionVerb.PlayCard)
            ?? throw new ManifestException("substitute-same-cost needs a card play.");

        var replaced = target with
        {
            Args = WithArg(WithArg(target.Args, "card_id", "CARD.STRIKE_IRONCLAD"), "hand_index", "0"),
            Note = "substituted by a negative control: a different card of the same energy cost",
        };
        actions[actions.IndexOf(target)] = replaced;
        return manifest with { RunId = manifest.RunId + "+substitute-same-cost", Actions = actions };
    }

    private static ReplayManifest OmitPlay(ReplayManifest manifest)
    {
        var actions = manifest.Actions.ToList();
        var target = actions.LastOrDefault(a => a.Verb == ActionVerb.PlayCard)
            ?? throw new ManifestException("omit-play needs a card play.");

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
