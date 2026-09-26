using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Modding;
using GameMod = MegaCrit.Sts2.Core.Modding.Mod;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Replay.Tests;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Playing from a recording while another mod is active.
///
/// The closest executable layer to the player pressing the play-from row is the
/// engine's own entry, <see cref="RecordedFightEntry"/>, run in this process against
/// the committed video reconstruction with a foreign mod registered in the game's own
/// mod manager - which is exactly what <c>LocalEnvironment.ReadMods</c> reads in the
/// retail client. No client is driven and no screen is taken over.
///
/// <para>Until 2026-09-20 the first test here reproduced a refusal: with any mod but
/// the host loaded, the entry threw before the run was constructed, naming
/// <c>loaded_mod_environment</c> and telling the player to disable every other mod.
/// That refused on "loaded" where what matters is "did something", and the boundary
/// proof at the end of the walk is what measures that.</para>
/// </summary>
public sealed class OtherModsAtPlayFromTests : IDisposable
{
    public OtherModsAtPlayFromTests()
    {
        EngineHost.Start();
        HeadlessRuns.EndAnyRun();
    }

    public void Dispose()
    {
        HeadlessRuns.EndAnyRun();
        HeadlessEngine.Forget();
    }

    private static ReplayManifest Recording() =>
        ManifestJson.Deserialize(File.ReadAllText(Arbiter.Manifest));

    /// <summary>
    /// Another active mod warns and does not block: the run is constructed, the
    /// recording's decisions are walked, and the fight at the end is the recorded one
    /// on the complete digest - with the mod loaded the whole way.
    /// </summary>
    [GameFact]
    public void AnotherActiveModWarnsAndTheRecordedFightIsStillEntered()
    {
        using var _ = ForeignMod.Register("baselib", "BaseLib", affectsGameplay: false);

        var advisory = Preflight.ActiveMods();
        using var entry = RecordedFightEntry.StartHeadless(Recording());
        while (!entry.AtBoundary) entry.AdvanceOneStep();
        var boundary = entry.VerifyBoundary();

        Assert.NotNull(advisory);
        Assert.Equal(["BaseLib"], advisory.Names);
        Assert.True(boundary.Matches, boundary.Refusal);
        Assert.Contains(
            "BaseLib is active alongside Runmobile",
            LibraryCopy.OtherModsActive(advisory.Names),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The warning's promise, held: with another mod active, a fight that opens
    /// differently from the recording's is still refused at the boundary, before
    /// anybody is handed it. The difference here is the project's own negative control
    /// on the opening decision, which is what a mod that changed the run would look
    /// like from where the check stands.
    /// </summary>
    [GameFact]
    public void ABoundaryThatDiffersIsStillRefusedWithAnotherModActive()
    {
        using var _ = ForeignMod.Register("baselib", "BaseLib", affectsGameplay: false);
        var damaged = Corruption.All.Single(control => control.Name == "wrong-opening-choice").Apply(Recording());

        using var entry = RecordedFightEntry.StartHeadless(damaged);
        while (!entry.AtBoundary) entry.AdvanceOneStep();
        var boundary = entry.VerifyBoundary();

        Assert.False(boundary.Matches);
        Assert.Contains("did not open the way the recording's did", boundary.Refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same mod is two facts. As a fact about this machine it is a warning; as a
    /// fact about a recording - captured into <c>environment.mods</c> the way the
    /// recorder captures it - it is still the recording's own rule, and a mod that
    /// declares itself gameplay-affecting still refuses the recording at preflight.
    /// Recording and submission judge the recording, and neither changed.
    /// </summary>
    [GameFact]
    public void TheRecordingSideRulesStillRefuseWhatTheyRefused()
    {
        using var _ = ForeignMod.Register("rebalance", "Rebalance", affectsGameplay: true);

        var recorded = ModEnvironment.AsRecorded(LocalEnvironment.ReadMods(), RecordedPatchRoster.HostOnly());
        var recording = Recording() with
        {
            Environment = Recording().Environment with
            {
                Mods = Fact<ModEnvironment>.Captured(recorded, FactEvidence.AtActionOrdinal(0)),
            },
        };

        var asRecorded = Preflight.Evaluate(recording.Environment, sourceKind: "native");
        var thisMachine = Preflight.Evaluate(Recording().Environment, sourceKind: Recording().Source.Kind);

        Assert.False(asRecorded.Matches);
        Assert.False(asRecorded.Fields.Single(field => field.Field == "mod_environment").Matches);
        Assert.True(thisMachine.Matches, string.Join("\n", thisMachine.Fields.Where(f => !f.Matches).Select(f => f.Diagnostic)));
        Assert.Equal(["Rebalance"], Preflight.ActiveMods()!.Names);
    }
}

/// <summary>
/// A mod the game's own mod manager reports as loaded, put there for one test and
/// taken away after it. The reading under test is <c>ModManager.Mods</c>, so the fake
/// goes into that list and nowhere else.
/// </summary>
internal sealed class ForeignMod : IDisposable
{
    private static readonly FieldInfo ModsField =
        typeof(ModManager).GetField("_mods", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ModManager has no _mods list on this build.");

    private readonly GameMod _mod;

    private ForeignMod(GameMod mod) => _mod = mod;

    internal static ForeignMod Register(
        string id, string name, bool affectsGameplay, ModLoadState state = ModLoadState.Loaded)
    {
        // Mod declares required members, so it is materialised rather than constructed
        var mod = (GameMod)RuntimeHelpers.GetUninitializedObject(typeof(GameMod));
        mod.manifest = new ModManifest
        {
            id = id,
            name = name,
            version = "1.0.0",
            affectsGameplay = affectsGameplay,
        };
        mod.state = state;
        mod.path = string.Empty;
        mod.assemblies = [];
        Mods().Add(mod);
        return new ForeignMod(mod);
    }

    public void Dispose() => Mods().Remove(_mod);

    private static List<GameMod> Mods() =>
        (List<GameMod>)(ModsField.GetValue(null)
                        ?? throw new InvalidOperationException("ModManager._mods is null."));
}
