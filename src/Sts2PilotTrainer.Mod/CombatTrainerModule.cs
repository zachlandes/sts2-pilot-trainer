using HarmonyLib;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The Combat Trainer: stand in a recorded fight, play it, and see your line beside
/// the recording's.
///
/// This is the module the proof of concept was, wrapped in the seam the other two
/// features will arrive through. It owns the recording this build ships, the
/// singleplayer card that opens the trainer, and the three patch classes the journey
/// needs; it owns nothing about how the mod is loaded or how module cards are drawn.
///
/// Its recording is read here rather than by the shell because a build whose
/// embedded recording cannot be read is a broken Combat Trainer, not a broken mod.
/// </summary>
internal sealed class CombatTrainerModule : IRunmobileModule
{
    internal static CombatTrainerModule Instance { get; } = new();

    /// <summary>
    /// The patch classes this module owns, listed rather than discovered.
    ///
    /// <c>PatchAll</c> over the assembly would install another module's patches too,
    /// and would install these for a Combat Trainer that had refused to start.
    /// </summary>
    internal static IReadOnlyList<Type> PatchClasses { get; } =
    [
        typeof(RecordedFightRun.TrainerRunTeardown),
        typeof(RecordedFightRun.MainMenuReturn),
        typeof(RecordedFightRun.DeviationLock),
    ];

    private readonly Lock _gate = new();

    private ReplayManifest? _recording;
    private RecordedFights? _recordedFights;
    private string? _refusal;
    private bool _examined;

    private CombatTrainerModule()
    {
    }

    public string Name => TrainerCopy.Name;

    public bool Enabled
    {
        get
        {
            Examine();
            return _refusal is null;
        }
    }

    public string? Refusal
    {
        get
        {
            Examine();
            return _refusal;
        }
    }

    public IReadOnlyList<MenuCard> MenuCards =>
    [
        new MenuCard(
            "CombatTrainerButton",
            TrainerCopy.Name,
            () => RecordingIdentity.Description(Recording),
            TrainerScreen.Open),
    ];

    /// <summary>
    /// The recording this build ships. Established by <see cref="Enabled"/> before
    /// anything downstream reads it, so nothing here carries a null case for a file
    /// that travels inside this assembly.
    /// </summary>
    internal ReplayManifest Recording =>
        Enabled
            ? _recording!
            : throw new InvalidOperationException($"This build ships no readable recording: {_refusal}");

    /// <summary>
    /// The recording's own line of each of its fights, replayed through the real
    /// engine and shipped beside the manifest. Bound to the recording before anything
    /// reads it: a file that is not the replay of exactly this manifest's fights is
    /// refused at mod start rather than compared against.
    /// </summary>
    internal RecordedFights RecordedFights =>
        Enabled
            ? _recordedFights!
            : throw new InvalidOperationException($"This build ships no readable recording: {_refusal}");

    public void Install(Harmony harmony)
    {
        foreach (var patchClass in PatchClasses)
        {
            harmony.CreateClassProcessor(patchClass).Patch();
        }
    }

    private void Examine()
    {
        lock (_gate)
        {
            if (_examined) return;
            _examined = true;
            try
            {
                _recording = ShippedRecording.Read();
                _recordedFights = ShippedRecording.ReadFights(_recording);
            }
            catch (Exception ex)
            {
                _recording = null;
                _recordedFights = null;
                _refusal = $"{ex.GetType().Name}: {ex.Message}";
            }
        }
    }
}

/// <summary>
/// The one recording this mod carries, embedded in the assembly.
///
/// Embedded rather than shipped as a file beside it because the game reads every
/// <c>.json</c> under its mod directory as a mod manifest, and a replay manifest
/// found there would be reported to the player as a broken mod.
/// </summary>
internal static class ShippedRecording
{
    private const string ResourceName = "Sts2PilotTrainer.Mod.recording.json";

    private const string FightResourceName = "Sts2PilotTrainer.Mod.recorded-fights.json";

    internal static ReplayManifest Read()
    {
        // Deserialize refuses a manifest version this build cannot read, rather than
        // interpreting the parts it recognises.
        return ManifestJson.Deserialize(Resource(ResourceName));
    }

    /// <summary>The recording's fights, and the proof they are this recording's.</summary>
    internal static RecordedFights ReadFights(ReplayManifest recording)
    {
        var fights = RecordedFights.Deserialize(Resource(FightResourceName));
        fights.Bind(recording);
        return fights;
    }

    private static string Resource(string name)
    {
        using var stream = typeof(ShippedRecording).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"This build carries no recording ({name} is absent from the assembly).");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
