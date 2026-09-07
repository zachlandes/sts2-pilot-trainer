using HarmonyLib;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The recorded-fight journey: it carries the recording this build ships, starts a
/// player at one of its proved boundaries, and compares the finished fight.
///
/// Its recording is read here rather than by the shell because a build whose embedded
/// recording cannot be read cannot offer its recorded fights.
/// </summary>
internal sealed class RecordedFightModule : IRunmobileModule
{
    internal static RecordedFightModule Instance { get; } = new();

    /// <summary>
    /// The patch classes this module owns, listed rather than discovered.
    ///
    /// <c>PatchAll</c> over the assembly would install another module's patches too,
    /// and would install these for a recorded-fight journey that had refused to start.
    /// </summary>
    internal static IReadOnlyList<Type> PatchClasses { get; } =
    [
        typeof(RecordedFightRun.TrainerRunTeardown),
        typeof(RecordedFightRun.MainMenuReturn),
        typeof(RecordedFightRun.DeviationLock),
        typeof(RecordedFightRun.LootLock),
    ];

    private readonly Lock _gate = new();

    private ReplayManifest? _recording;
    private RecordedFights? _recordedFights;
    private string? _refusal;
    private bool _examined;

    /// <summary>
    /// The fights whose recorded line has been shown this sitting, by run id.
    ///
    /// A sitting is the game process, from launch to quit. This is never written -
    /// not to progress, not to the store, not to a recording - so a later launch
    /// starts empty and every fight is cold again. It changes what is drawn and gates
    /// nothing: the dot on a post-fight row already taken, and the hollow-eye mark
    /// beside the fight in the run view. Fight it again is offered after a reveal; a
    /// rehearsal of a line just seen is a teaching move of its own, and the mark is
    /// there so the player knows which kind of attempt this is.
    /// </summary>
    private readonly Dictionary<string, HashSet<int>> _shownThisSitting = new(StringComparer.Ordinal);

    private RecordedFightModule()
    {
    }

    public string Name => "Recorded fights";

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

    /// <summary>Records that the recording's line for this fight has been shown: the
    /// comparison drawn, or the attempt finished by name.</summary>
    internal void MarkShownThisSitting(string runId, int fight)
    {
        lock (_gate)
        {
            if (!_shownThisSitting.TryGetValue(runId, out var fights))
            {
                fights = [];
                _shownThisSitting[runId] = fights;
            }

            fights.Add(fight);
        }
    }

    internal bool WasShownThisSitting(string runId, int fight)
    {
        lock (_gate)
        {
            return _shownThisSitting.TryGetValue(runId, out var fights) && fights.Contains(fight);
        }
    }

    /// <summary>Every fight of one run shown this sitting, for the run view's marks.</summary>
    internal IReadOnlyCollection<int> FightsShownThisSitting(string runId)
    {
        lock (_gate)
        {
            return _shownThisSitting.TryGetValue(runId, out var fights) ? [.. fights] : [];
        }
    }

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
