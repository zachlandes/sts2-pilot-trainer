namespace Sts2PilotTrainer.Replay;

/// <summary>
/// The engine-generated fixtures, as this assembly ships them.
///
/// Both are produced by <c>arbiter generate-synthetic-fixture</c> against the real
/// game and committed, so that a machine with no game - continuous integration, most
/// obviously - can still read a history the engine really produced. Neither carries a
/// verification: what a replay of one yields is what a replay is for.
///
/// Regenerating one is two steps, in order, and each fixture has its own journey:
/// <c>arbiter generate-synthetic-fixture --out &lt;fixture&gt; --journey &lt;journey&gt;</c>,
/// then <c>arbiter migrate-manifest &lt;fixture&gt; --derive-boundaries</c>, which
/// replays the written history through the real engine and writes in every boundary
/// it passes. The journeys are <c>first-fight --line reference</c> for
/// synthetic-v0111-pilot-trainer, <c>whole-act</c> for synthetic-v0111-whole-act and
/// <c>screen-at-boundary</c> for synthetic-v0111-screen-at-boundary. Leaving
/// <c>--journey</c> off falls back to first-fight and writes the wrong history at the
/// right path; leaving the second step off writes a fixture with no boundaries at
/// all. Either one is a well-formed file that breaks the tests reading it.
/// docs/headless-fidelity.md carries the same recipe as runnable commands.
/// </summary>
public static class SyntheticReplayFixture
{
    private const string FirstFightResource =
        "Sts2PilotTrainer.Replay.Fixtures.synthetic-v0111-pilot-trainer.replay.json";

    private const string WholeActResource =
        "Sts2PilotTrainer.Replay.Fixtures.synthetic-v0111-whole-act.replay.json";

    /// <summary>Run start to the end of the first fight.</summary>
    public static ReplayManifest Create() => Read(FirstFightResource);

    /// <summary>
    /// Run start to the far side of an act's boss: nine fights, a shop, a chest, rest
    /// sites, an elite and the act transition.
    ///
    /// The one history in this repository that has more than one fight in it and did
    /// not come from anybody's video, which is what makes it the fixture for every
    /// rule about later fights, later floors and turns within them.
    /// </summary>
    public static ReplayManifest CreateWholeAct() => Read(WholeActResource);

    private static ReplayManifest Read(string resource)
    {
        using var stream = typeof(SyntheticReplayFixture).Assembly.GetManifestResourceStream(resource)
            ?? throw new ManifestException($"Embedded synthetic fixture '{resource}' is missing.");
        using var reader = new StreamReader(stream);
        return ManifestJson.Deserialize(reader.ReadToEnd());
    }
}
