namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The one-reading shape of <see cref="RunCapture.Record"/>, for a test that writes
/// a run by hand.
///
/// A recorder reads the state a decision begins from where it stands when the
/// decision is announced. A test writing consecutive decisions with nothing between
/// them has the previous after-reading as that state, which is what this supplies -
/// so a test about something else does not have to spell the same reading twice.
/// </summary>
internal static class RecordingExtensions
{
    internal static RunJournalEntry Record(
        this RunCapture capture,
        ActionVerb verb,
        IReadOnlyDictionary<string, string> args,
        IReadOnlyDictionary<string, string> after,
        string digest,
        int? runClockMs = null) =>
        capture.Record(
            verb, args,
            new StateReading(capture.Trace.Steps[^1].After, capture.LastDigest),
            new StateReading(after, digest),
            runClockMs);
}
