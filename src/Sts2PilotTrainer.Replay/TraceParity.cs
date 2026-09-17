using System.Globalization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Whether a replay reproduced a recording decision for decision.
///
/// The per-decision oracle behind the recorder's release bar, and the one place it is
/// written: the CLI's <c>parity</c> command holds a player's journal to a fresh replay
/// through it, and the headless recorder tests hold the capture they made to theirs
/// through the same call, so the two cannot drift.
///
/// A recording's side is the trace its journal reads back as (<see cref="RunJournal.Trace"/>,
/// which a live <see cref="RunCapture"/> keeps in step with) and the replay's side is
/// the trace <c>Arbiter.ReplayStartedRun</c> produces. Each step is held on four
/// things in turn: the decision is at the same place with the same verb; the sampled
/// state it began from agrees; the sampled state it settled into agrees; and where both
/// sides carry the complete digest of a reading, that agrees too. The samples are
/// compared through <see cref="ReplayTrace.SameSample"/>, over the fields two hosts
/// have to agree on, and the digest is the whole canonical state - the draw order and
/// every random stream's position - so a step whose samples agree and whose digest does
/// not has diverged in hidden state, and is reported as that rather than as a field.
///
/// Two digests are not held, each for a reason the recorder's own resume already
/// carries, and nothing else is excused. The reading a fight-ending decision settled
/// into: the retail client rolls the rewards after the engine has settled, on its own
/// clock, so that one after-digest is never the replay's and the state the next
/// decision began from is what both hosts read the same way
/// (<see cref="ReplayStep.EndsAFight"/>; the next decision's before-digest is held on
/// its own). And the opening reading: it is not a decision's reading, no decision is
/// held to it, and the retail client's first room does its own work between the
/// recorder's reading of it and the first decision, which only the digest sees. That
/// one is reported beside the verdict rather than folded into it, so a recording is at
/// parity by what its decisions reproduce and the opening's difference stays visible.
///
/// The first divergence is the answer. A replay that has left the recorded history is
/// in a different run from there on, and every later difference is a consequence of
/// the first rather than a finding of its own.
/// </summary>
public static class TraceParity
{
    public static ParityResult Compare(ReplayTrace recorded, ReplayTrace replayed)
    {
        var recordedSteps = recorded.Steps;
        var replayedSteps = replayed.Steps;
        var decisions = recordedSteps.Count(step => step.Seq >= 0);
        var replayedDecisions = replayedSteps.Count(step => step.Seq >= 0);
        string? openingHiddenState = null;

        for (var index = 0; index < recordedSteps.Count; index++)
        {
            var expected = recordedSteps[index];
            if (index >= replayedSteps.Count)
            {
                return Diverged(
                    decisions, replayedDecisions, expected, ParityDivergenceKind.MissingStep,
                    ["the replay has no step here; it ended before this decision"]);
            }

            var actual = replayedSteps[index];
            if (expected.Seq != actual.Seq || !string.Equals(expected.Verb, actual.Verb, StringComparison.Ordinal))
            {
                return Diverged(
                    decisions, replayedDecisions, expected, ParityDivergenceKind.StepDiffers,
                    [$"the replay's step here is {Describe(actual)}"]);
            }

            if (!ReplayTrace.SameSample(expected.Before, actual.Before))
            {
                return Diverged(
                    decisions, replayedDecisions, expected, ParityDivergenceKind.BeforeSampleDiffers,
                    ReplayTrace.Differences(expected.Before, actual.Before));
            }

            if (!ReplayTrace.SameSample(expected.After, actual.After))
            {
                return Diverged(
                    decisions, replayedDecisions, expected, ParityDivergenceKind.AfterSampleDiffers,
                    ReplayTrace.Differences(expected.After, actual.After));
            }

            if (expected.Seq < 0)
            {
                if (DigestsDiffer(expected.AfterDigest, actual.AfterDigest))
                {
                    openingHiddenState = $"{expected.AfterDigest} -> {actual.AfterDigest}";
                }

                continue;
            }

            if (DigestsDiffer(expected.BeforeDigest, actual.BeforeDigest))
            {
                return Diverged(
                    decisions, replayedDecisions, expected, ParityDivergenceKind.HiddenStateDiffersBefore,
                    [$"before: {expected.BeforeDigest} -> {actual.BeforeDigest}"]);
            }

            if (!expected.EndsAFight && DigestsDiffer(expected.AfterDigest, actual.AfterDigest))
            {
                return Diverged(
                    decisions, replayedDecisions, expected, ParityDivergenceKind.HiddenStateDiffersAfter,
                    [$"after: {expected.AfterDigest} -> {actual.AfterDigest}"]);
            }
        }

        if (replayedSteps.Count > recordedSteps.Count)
        {
            var extra = replayedSteps[recordedSteps.Count];
            return Diverged(
                decisions, replayedDecisions, extra, ParityDivergenceKind.ExtraStep,
                ["the recording has no decision here; the replay went on past its last one"]);
        }

        return new ParityResult(decisions, replayedDecisions, Divergence: null, openingHiddenState);
    }

    /// <summary>A digest is held only where both sides carry one: a trace written
    /// before the digests were kept says nothing about hidden state rather than
    /// disagreeing about it.</summary>
    private static bool DigestsDiffer(string? expected, string? actual) =>
        expected is not null && actual is not null && !string.Equals(expected, actual, StringComparison.Ordinal);

    private static ParityResult Diverged(
        int decisions, int replayedDecisions, ReplayStep at, ParityDivergenceKind kind,
        IReadOnlyList<string> differences) =>
        new(decisions, replayedDecisions, new ParityDivergence(at.Seq, at.Verb, kind, differences), null);

    private static string Describe(ReplayStep step) =>
        $"decision {step.Seq.ToString(CultureInfo.InvariantCulture)} ({step.Verb})";
}

/// <summary>What <see cref="TraceParity.Compare"/> found.</summary>
/// <param name="Decisions">How many decisions the recording holds, the opening reading aside.</param>
/// <param name="ReplayedDecisions">How many the replay took, the opening reading aside.</param>
/// <param name="Divergence">The first decision at which the replay left the recording, or null at parity.</param>
/// <param name="OpeningHiddenState">The two opening digests where they differed with the
/// opening samples equal, as <c>recorded -> replayed</c>; null where they agreed or
/// where a side carried none. Reported, never counted: see <see cref="TraceParity"/>.</param>
public sealed record ParityResult(
    int Decisions, int ReplayedDecisions, ParityDivergence? Divergence, string? OpeningHiddenState)
{
    public bool AtParity => Divergence is null;

    /// <summary>The opening's difference as a line for a person, or null where there
    /// is none to report.</summary>
    public string? OpeningNote => OpeningHiddenState is null
        ? null
        : "opening reading: hidden state differs before any decision; every decision is held from its own " +
          $"reading ({OpeningHiddenState})";

    /// <summary>The verdict as lines for a person: <c>PARITY</c>, or the first
    /// divergence as a decision and what differed there; the opening's note after
    /// either, where there is one.</summary>
    public string Describe()
    {
        var verdict = Divergence is null ? "PARITY" : Divergence.Describe();
        return OpeningNote is null ? verdict : $"{verdict}\n{OpeningNote}";
    }
}

/// <summary>The first decision at which a replay left the recording, and how.</summary>
public sealed record ParityDivergence(
    int Seq, string Verb, ParityDivergenceKind Kind, IReadOnlyList<string> Differences)
{
    /// <summary>Whether every sampled field agreed and only the complete digest
    /// differed: a divergence in draw order or a random stream's position, which
    /// no sampled field shows.</summary>
    public bool HiddenStateOnly =>
        Kind is ParityDivergenceKind.HiddenStateDiffersBefore or ParityDivergenceKind.HiddenStateDiffersAfter;

    public string Describe()
    {
        var at = $"decision {Seq.ToString(CultureInfo.InvariantCulture)} ({Verb})";
        return Kind switch
        {
            ParityDivergenceKind.MissingStep or ParityDivergenceKind.ExtraStep or ParityDivergenceKind.StepDiffers =>
                $"{at}: {Differences[0]}",
            ParityDivergenceKind.BeforeSampleDiffers =>
                string.Join("\n", Differences.Select(line => $"{at} before: {line}")),
            ParityDivergenceKind.AfterSampleDiffers =>
                string.Join("\n", Differences.Select(line => $"{at} after: {line}")),
            ParityDivergenceKind.HiddenStateDiffersBefore or ParityDivergenceKind.HiddenStateDiffersAfter =>
                $"hidden state differs at {at}; every sampled field agrees ({Differences[0]})",
            _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "unknown divergence kind"),
        };
    }
}

public enum ParityDivergenceKind
{
    /// <summary>The replay ended before this decision.</summary>
    MissingStep,

    /// <summary>The replay went on past the recording's last decision.</summary>
    ExtraStep,

    /// <summary>The replay's step at this position is another decision or another verb.</summary>
    StepDiffers,

    /// <summary>A sampled field of the state the decision began from differs.</summary>
    BeforeSampleDiffers,

    /// <summary>A sampled field of the state the decision settled into differs.</summary>
    AfterSampleDiffers,

    /// <summary>Every sampled field agrees and the complete digest before the decision does not.</summary>
    HiddenStateDiffersBefore,

    /// <summary>Every sampled field agrees and the complete digest after the decision does not.</summary>
    HiddenStateDiffersAfter,
}
