namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The corruptions are the arbiter's negative controls, so they need controls of
/// their own: each must actually change the history, and must still be a history the
/// validator accepts. A "corruption" that produced an invalid manifest would be
/// rejected by the validator instead of by the engine, which proves nothing about
/// the engine.
/// </summary>
public class CorruptionTests
{
    // Timed to sit after the fixture's run-start evidence (9,000ms) and around its
    // combat-start checkpoint (75,600ms, after seq 1), so the manifest is coherent
    // before any corruption touches it. A corruption has to be rejected by the
    // engine, which means it must get past ingestion first.
    private static readonly int[] PlayableTimes =
        [10_000, 20_000, 76_000, 77_000, 78_000, 79_000, 80_000, 81_000, 82_000, 83_000, 84_000];

    /// <summary>
    /// A history carrying one of every kind of decision a corruption damages, so that
    /// every control has something to bite on. It is never replayed - these tests are
    /// about whether a corruption changes a history and leaves it structurally valid -
    /// so the actions only have to be individually well formed.
    /// </summary>
    private static ReplayManifest Playable(bool native = false)
    {
        var manifest = native ? Fixtures.NativeManifest() : Fixtures.ValidManifest();
        IReadOnlyList<ActionRecord> actions =
        [
            At(0, Fixtures.Action(0, ActionVerb.ChooseNeowBlessing, ("option_index", "2"))),
            At(1, Fixtures.Action(1, ActionVerb.MapMove,
                ("act", "0"), ("row", "1"), ("column", "3"),
                (Corruption.AlternativeColumn, "1"))),
            At(2, Fixtures.Action(2, ActionVerb.PlayCard, ("card_id", "CARD.HELLRAISER"), ("hand_index", "1"))),
            At(3, Fixtures.Action(3, ActionVerb.PlayCard,
                ("card_id", "CARD.DEFEND_IRONCLAD"), ("hand_index", "3"),
                ("negative_control_substitute_card_id", "CARD.STRIKE_IRONCLAD"),
                ("negative_control_substitute_hand_index", "0"))),
            At(4, Fixtures.Action(4, ActionVerb.PlayCard,
                ("card_id", "CARD.BASH"), ("hand_index", "2"), ("target_index", "0"))),
            At(5, Fixtures.Action(5, ActionVerb.EndTurn)),
            At(6, Fixtures.Action(6, ActionVerb.ClaimReward, ("reward_type", "gold"))),
            At(7, Fixtures.Action(7, ActionVerb.TakeCard,
                ("card_id", "CARD.POMMEL_STRIKE"), ("option_index", "0"),
                (Corruption.AlternativeCardId, "CARD.TREMBLE"),
                (Corruption.AlternativeOptionIndex, "1"))),
            At(8, Fixtures.Action(8, ActionVerb.ChooseEventOption,
                ("event_id", "EVENT.WATERLOGGED_SCRIPTORIUM"), ("option_index", "2"))),
            At(9, Fixtures.Action(9, ActionVerb.SelectCardFromScreen,
                ("card_id", "CARD.DEFEND_IRONCLAD"), ("option_index", "5"),
                (Corruption.AlternativeOptionIndex, "4"))),
            At(10, Fixtures.Action(10, ActionVerb.SkipRewards)),
        ];
        if (native)
        {
            actions = actions.Select(action => action with
            {
                Source = FactSource.Captured,
                Evidence = FactEvidence.AtActionOrdinal(
                    action.Seq, runClockMs: PlayableTimes[action.Seq]),
            }).ToList();
        }

        return manifest with { Actions = actions, Checkpoints = Checkpoints(native) };
    }

    /// <summary>
    /// One checkpoint before the nominated play and one after it. The second is what
    /// makes the omission control's renumbering visible: a checkpoint that sits ahead
    /// of the removed action is shifted back, and a native one carries a coordinate
    /// into the history that has to move with it.
    /// </summary>
    private static IReadOnlyList<Checkpoint> Checkpoints(bool native) =>
    [
        new Checkpoint
        {
            Id = "combat-start",
            AfterSeq = 1,
            Kind = "combat_start",
            Expect = Expect(native, 1, "3"),
        },
        new Checkpoint
        {
            Id = "turn-end",
            AfterSeq = 5,
            Kind = "turn_start",
            Expect = Expect(native, 5, "0"),
        },
    ];

    private static IReadOnlyDictionary<string, Fact<string>> Expect(bool native, int afterSeq, string energy) =>
        new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
        {
            ["combat.energy"] = native
                ? Fact<string>.Captured(energy, FactEvidence.AtActionOrdinal(afterSeq))
                : Fact<string>.Observed(energy, FactEvidence.AtVideoTime(PlayableTimes[afterSeq], "energy orb")),
        };

    private static IReadOnlyList<ReplayManifest> PlayableRecordings() => [Playable(), Playable(native: true)];

    private static ActionRecord At(int index, ActionRecord action) => action with
    {
        Evidence = FactEvidence.AtVideoTime(PlayableTimes[index], "test fixture"),
    };

    [Fact]
    public void EveryCorruptionChangesTheActionHistory()
    {
        foreach (var recording in PlayableRecordings())
        {
            var original = SnapshotCacheKey.HashActions(recording.Actions);
            foreach (var corruption in Corruption.All)
            {
                var corrupted = corruption.Apply(recording);
                Assert.True(
                    SnapshotCacheKey.HashActions(corrupted.Actions) != original,
                    $"corruption '{corruption.Name}' left the {recording.Source.Kind} action history unchanged");
            }
        }
    }

    [Fact]
    public void EveryCorruptionStillProducesAStructurallyValidManifest()
    {
        foreach (var recording in PlayableRecordings())
        {
            foreach (var corruption in Corruption.All)
            {
                var result = ManifestValidator.Validate(corruption.Apply(recording));
                Assert.True(
                    result.IsValid,
                    $"corruption '{corruption.Name}' produced an invalid {recording.Source.Kind} manifest:\n" +
                    result.Describe());
            }
        }
    }

    /// <summary>
    /// A boundary after the omitted play names the same action it named before, so a
    /// host entering the corrupted recording is refused by the engine replaying it
    /// rather than by a coordinate the control moved out from under.
    /// </summary>
    [Fact]
    public void OmitPlayCarriesALaterBoundaryBackWithTheActionItNames()
    {
        var recording = Playable(native: true) with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.CombatStart(
                    2, 5, Fact<string>.Captured(Fixtures.Digest, FactEvidence.AtActionOrdinal(5))),
            ],
        };

        var corrupted = Corruption.All.Single(c => c.Name == "omit-play").Apply(recording);

        var later = corrupted.Boundaries.Single(boundary => boundary.Fight == 2);
        Assert.Equal(4, later.AfterSeq);
        Assert.Equal(4, later.Digest.Evidence!.ActionOrdinal);
        Assert.Equal(ActionVerb.EndTurn, corrupted.Actions.Single(action => action.Seq == 4).Verb);
        Assert.Equal(1, corrupted.Boundaries.Single(boundary => boundary.Fight == 1).AfterSeq);

        var result = ManifestValidator.Validate(corrupted);
        Assert.True(result.IsValid, result.Describe());
    }

    /// <summary>The place a boundary named is gone with the action it stood on, so it
    /// goes too rather than silently naming whatever slid into the slot.</summary>
    [Fact]
    public void OmitPlayDropsABoundaryStandingOnTheRemovedAction()
    {
        var recording = Playable(native: true) with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
                ReplayBoundary.CombatStart(2, 3, Fact<string>.Engine(Fixtures.Digest)),
            ],
        };

        var corrupted = Corruption.All.Single(c => c.Name == "omit-play").Apply(recording);

        Assert.Equal([1], corrupted.Boundaries.Select(boundary => boundary.Fight));
    }

    [Fact]
    public void CoversBothCorruptionsVideoOnlyChecksCannotSee()
    {
        // These are the reason for owning an engine at all. If the set ever stops
        // including them, the negative controls only demonstrate what arithmetic on
        // the footage already caught.
        var undetectable = Corruption.All
            .Where(c => c.VideoOnly == Corruption.VideoOnlyVerdict.Undetected)
            .Select(c => c.Name)
            .ToList();

        Assert.Contains("reorder-plays", undetectable);
        Assert.Contains("substitute-same-cost", undetectable);
        Assert.Contains("enchant-a-different-card", undetectable);
        Assert.Contains("target-the-other-enemy", undetectable);
    }

    /// <summary>
    /// Every kind of decision the driver can apply has a control aimed at it. A verb
    /// that nothing corrupts is a verb whose rejection has never been demonstrated,
    /// and this is what stops the next verb from arriving without one.
    /// </summary>
    [Theory]
    [InlineData(ActionVerb.ChooseNeowBlessing, "wrong-opening-choice")]
    [InlineData(ActionVerb.MapMove, "move-to-a-different-node")]
    [InlineData(ActionVerb.PlayCard, "substitute-same-cost")]
    [InlineData(ActionVerb.PlayCard, "target-the-other-enemy")]
    [InlineData(ActionVerb.ClaimReward, "decline-a-claimed-reward")]
    [InlineData(ActionVerb.TakeCard, "take-a-different-card")]
    [InlineData(ActionVerb.ChooseEventOption, "choose-a-different-event-option")]
    [InlineData(ActionVerb.SelectCardFromScreen, "enchant-a-different-card")]
    public void EveryKindOfDecisionHasAControlAimedAtIt(ActionVerb verb, string control)
    {
        var corruption = Corruption.All.Single(c => c.Name == control);
        var before = Playable();
        var after = corruption.Apply(before);

        var changed = before.Actions
            .Zip(after.Actions, (a, b) => (a, b))
            .Where(pair => pair.a.Args.Count != pair.b.Args.Count ||
                           pair.a.Verb != pair.b.Verb ||
                           pair.a.Args.Any(arg => pair.b.Args.GetValueOrDefault(arg.Key) != arg.Value))
            .ToList();

        Assert.NotEmpty(changed);
        Assert.Contains(changed, pair => pair.a.Verb == verb);
    }

    [Fact]
    public void KeepsAtLeastOneCorruptionVideoOnlyChecksWouldCatch()
    {
        // A control on the controls: an arbiter that caught only the subtle
        // corruptions would be broken in a way the subtle ones cannot reveal.
        Assert.Contains(Corruption.All, c => c.VideoOnly == Corruption.VideoOnlyVerdict.Detected);
    }

    /// <summary>
    /// A corrupted history is a hypothesis about a run nobody played, and the whole
    /// point of writing one to disk is to watch it fail. It has to be impossible to
    /// mistake for the reconstruction, in the file and in every artifact keyed off it.
    /// </summary>
    [Fact]
    public void EveryCorruptionMarksItselfInTheRunIdAndOnTheActionItDamaged()
    {
        var original = Playable();

        foreach (var corruption in Corruption.All)
        {
            var corrupted = corruption.Apply(original);

            Assert.StartsWith(original.RunId, corrupted.RunId, StringComparison.Ordinal);
            Assert.EndsWith("+" + corruption.Name, corrupted.RunId, StringComparison.Ordinal);

            // Omission has no action left to annotate; everything else does.
            if (corrupted.Actions.Count == original.Actions.Count)
            {
                Assert.Contains(
                    corrupted.Actions,
                    action => action.Note?.Contains("negative control", StringComparison.Ordinal) == true);
            }

            // And a corruption never invents provenance: it damages a decision, it does
            // not turn a hypothesis into an observation.
            Assert.Equal(
                original.Actions.Count(a => a.Source == FactSource.Observed) -
                (original.Actions.Count - corrupted.Actions.Count),
                corrupted.Actions.Count(a => a.Source == FactSource.Observed));
        }
    }

    [Fact]
    public void ReorderingKeepsEvidenceTimestampsInSequenceOrder()
    {
        var reordered = Corruption.All.Single(c => c.Name == "reorder-plays").Apply(Playable());
        var plays = reordered.Actions.Where(action => action.Verb == ActionVerb.PlayCard).ToList();

        Assert.Equal(PlayableTimes[2], plays[0].Evidence!.VideoTimeMs);
        Assert.Equal(PlayableTimes[3], plays[1].Evidence!.VideoTimeMs);
        Assert.True(ManifestValidator.Validate(reordered).IsValid);
    }

    /// <summary>
    /// A history that nominated no substitute is one this control has nothing to do to.
    ///
    /// Its three nomination-driven siblings all say so in <c>AppliesTo</c>, so a history
    /// that never nominated is skipped by name and
    /// <c>negative-controls --require-all-controls</c> refuses. This one used to fall
    /// back to a hardcoded Strike at hand index 0 and run anyway: the driver then
    /// refused on card identity against whatever card actually sat there, the gate
    /// counted the control as rejected, and nothing about a same-cost substitution had
    /// been demonstrated. A control that cannot fail for the right reason is worse than
    /// one that is honestly skipped.
    /// </summary>
    [Fact]
    public void SubstitutingAppliesOnlyToAHistoryThatNominatedASubstitute()
    {
        var control = Corruption.All.Single(c => c.Name == "substitute-same-cost");
        var manifest = Playable();
        var unnominated = manifest with
        {
            Actions = [.. manifest.Actions.Select(action => action with
            {
                Args = action.Args
                    .Where(pair => pair.Key != Corruption.SubstituteCardId &&
                                   pair.Key != Corruption.SubstituteHandIndex)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            })],
        };

        Assert.True(control.AppliesTo(manifest));
        Assert.False(control.AppliesTo(unnominated));

        // And reaching it anyway is a refusal rather than a guess.
        var refusal = Assert.Throws<ManifestException>(() => control.Apply(unnominated));
        Assert.Contains(Corruption.SubstituteCardId, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReorderingKeepsBothPlaysLegalAtTheirOriginalHandPositions()
    {
        // The reorder has to be caught by state, not by the driver noticing a bad hand
        // index - otherwise it would prove nothing about whether order matters.
        var reordered = Corruption.All.Single(c => c.Name == "reorder-plays").Apply(Playable());
        var plays = reordered.Actions.Where(a => a.Verb == ActionVerb.PlayCard).ToList();

        Assert.Equal("CARD.DEFEND_IRONCLAD", plays[0].Args["card_id"]);
        Assert.Equal("4", plays[0].Args["hand_index"]);
        Assert.Equal("CARD.HELLRAISER", plays[1].Args["card_id"]);
        Assert.Equal("1", plays[1].Args["hand_index"]);
    }
}
