using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The saves the game asks for while the headless host is replaying, offered to a
/// collector instead of being written.
///
/// The retail client takes its run save inside <c>RunManager.EnterMapPointInternal</c>,
/// after the map coordinate has been set and <em>before</em> the room type is rolled.
/// The game's own save is therefore a floor-entry snapshot by construction, and it is
/// the only way to get one: a save assembled here would be a save of what this project
/// believes a run is made of, which is the belief under test.
///
/// This is the same headless patch <c>SaveManager.SaveRun</c> has always had - the
/// player's save directory is a read-only input, and nothing is written where the game
/// would write - with one addition. When a collector is installed, the prefix asks the
/// game for the object it was about to save, through the game's own
/// <c>RunManager.ToSave(preFinishedRoom)</c> at the game's own call site with the game's
/// own argument, and hands it over. With no collector installed it is exactly the
/// neutralize it replaces, and <c>ToSave</c> is not called at all.
///
/// One thing it deliberately does not reproduce. The retail client reaches
/// <c>SaveManager.SaveRun</c> through <c>RunSaveManager</c>, which consults
/// <c>RunManager.Instance.ShouldSave</c> first; this host's runs are created with
/// <c>shouldSave: false</c>, so what is collected here is what the game <em>asked</em>
/// to save rather than what a player's client would have written to disk. For a
/// snapshot produced from a replay they are the same object. Reading a save file a
/// player's own client wrote is a different path and is not this one.
/// </summary>
public static class RunSaveInterception
{
    private static Action<InterceptedRunSave>? _collector;

    /// <summary>Collects every save the game asks for until the returned scope is
    /// disposed. Nested arming is refused rather than stacked: two collectors would
    /// each see a prefix of the run and neither would know it.</summary>
    public static IDisposable Collect(Action<InterceptedRunSave> collector)
    {
        if (_collector is not null)
        {
            throw new EngineException(
                "A run-save collector is already installed in this process. Two collectors would each see " +
                "part of one replay and neither would say so.");
        }

        _collector = collector;
        return new Scope();
    }

    /// <summary>Whether anything is collecting. Read by the patch so the default path
    /// stays byte-for-byte the neutralize it replaces.</summary>
    internal static bool Armed => _collector is not null;

    /// <summary>
    /// Called from the <c>SaveManager.SaveRun</c> prefix, with the argument the game
    /// passed.
    /// </summary>
    internal static void Offer(object? preFinishedRoom)
    {
        var collector = _collector;
        if (collector is null) return;

        // Nothing to serialize means nothing to collect. A save asked for with no run
        // behind it is not a moment of a run, and skipping it cannot make a wrong
        // snapshot pass: the state a skip would leave last is a state the restore then
        // fails to reproduce, and the digest comparison is what catches it.
        var run = RunManager.Instance;
        var state = run?.DebugOnlyGetState();
        if (run is null || state is null) return;

        var room = preFinishedRoom as AbstractRoom;
        var save = run.ToSave(room);
        collector(new InterceptedRunSave(
            Json: JsonSerializationUtility.ToJson(save),
            SchemaVersion: save.SchemaVersion,
            PreFinishedRoom: room?.RoomType.ToString() ?? "none",
            TotalFloor: state.TotalFloor,
            ActFloor: state.ActFloor,
            MapCoord: state.CurrentMapCoord is { } coord ? $"r{coord.row}c{coord.col}" : "none"));
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => _collector = null;
    }
}

/// <summary>One save the game asked to write, as the game's own serializer rendered
/// it, with the little that says which moment of the run it is.</summary>
public sealed record InterceptedRunSave(
    string Json,
    int SchemaVersion,
    string PreFinishedRoom,
    int TotalFloor,
    int ActFloor,
    string MapCoord)
{
    /// <summary>What a save taken at a floor arrival carries in place of a room: the
    /// room does not exist yet, which is what makes the moment the one worth
    /// storing.</summary>
    public const string NoPreFinishedRoom = "none";

    public bool IsFloorEntry => string.Equals(PreFinishedRoom, NoPreFinishedRoom, StringComparison.Ordinal);

    public string Describe() =>
        $"floor {TotalFloor.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
        $"(act floor {ActFloor.ToString(System.Globalization.CultureInfo.InvariantCulture)}) at {MapCoord}, " +
        $"pre-finished room {PreFinishedRoom}, " +
        $"{System.Text.Encoding.UTF8.GetByteCount(Json).ToString(System.Globalization.CultureInfo.InvariantCulture)} bytes";
}
