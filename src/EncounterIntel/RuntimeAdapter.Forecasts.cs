using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;

namespace EncounterIntel;

public static partial class RuntimeAdapter
{
    public static LookupResult<UnknownRoomForecast> ReadUnknownRoomForecast(MapPoint candidate) => Lookup(() =>
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var run = LookupRun();
        var hooks = RequireForecastSemantics(run);
        if (candidate.PointType != MapPointType.Unknown)
            throw new IntelUnavailable(LookupState.Refused, "This node is not a question mark.");
        if (!NextVisitPoints(run).Any(point => ReferenceEquals(point, candidate)))
            throw new IntelUnavailable(LookupState.Refused, "This question mark is not an available next visit; intervening decisions need another forecast.");
        var history = VisibleHistory(run);
        var priorUnknown = history.SelectMany(act => act).Count(point => point.MapPointType == MapPointType.Unknown);
        var previous = history.LastOrDefault()?.LastOrDefault();
        // The candidate's children, not the children of the node the player stands on
        var blacklist = RunManager.BuildRoomTypeBlacklist(previous, candidate.Children);
        var shopBlacklisted = blacklist.Contains(RoomType.Shop);
        var allowed = Enum.GetValues<ForecastRoomType>().ToHashSet();
        if (shopBlacklisted) allowed.Remove(ForecastRoomType.Shop);
        if (hooks.Contains(typeof(JuzuBracelet).FullName!)) allowed.Remove(ForecastRoomType.Monster);
        var live = run.Odds.UnknownMapPoint;
        var raw = new RawRoomOdds(live.MonsterOdds, live.EliteOdds, live.TreasureOdds, live.ShopOdds);
        var firstRun = run.UnlockState.NumberOfRuns == 0;
        return new UnknownRoomForecast(SupportedBuild.Require(), run.Act.Id.ToString(), run.CurrentActIndex,
            candidate.coord.col, candidate.coord.row, raw, priorUnknown, firstRun, shopBlacklisted, hooks,
            UnknownRoomRules.Project(raw, allowed, firstRun, priorUnknown));
    });

    public static LookupResult<EncounterPoolForecast> ReadEncounterPoolForecast() => Lookup(() =>
    {
        var run = LookupRun();
        RequireForecastSemantics(run);
        var act = run.Act;
        var history = VisibleHistory(run);
        var current = history.ElementAtOrDefault(run.CurrentActIndex) ?? [];
        // Only the base room consumes the normal deal; an event may hold later combat rooms
        var normals = current.Count(point => point.Rooms[0].RoomType == RoomType.Monster);
        var elites = current.Count(point => point.Rooms[0].RoomType == RoomType.Elite);
        var weak = typeof(ActModel).GetProperty("NumberOfWeakEncounters", InstanceMembers)?.GetValue(act) as int?
            ?? throw new IntelRefusal("The inspected weak encounter count is unavailable.");
        var phase = normals < weak ? NormalEncounterPhase.Weak : NormalEncounterPhase.Regular;
        IEnumerable<EncounterModel> normalPool = phase == NormalEncounterPhase.Weak ? act.AllWeakEncounters : act.AllRegularEncounters;
        IEnumerable<EncounterModel> elitePool = act.AllEliteEncounters;
        var normalBasis = PoolBasis.PhaseDefinitions;
        var eliteBasis = PoolBasis.PhaseDefinitions;
        if (act.Id.ToString() == "ACT.OVERGROWTH" && run.UnlockState.NumberOfRuns == 0 && RunManager.Instance.ShouldApplyTutorialModifications())
        {
            // Public tutorial script, not an inspection of its already-dealt queue
            string[] normalScript = ["NIBBITS_WEAK", "SLIMES_WEAK", "SHRINKER_BEETLE_WEAK", "INKLETS_NORMAL", "MAWLER_NORMAL", "RUBY_RAIDERS_NORMAL", "NIBBITS_NORMAL"];
            string[] eliteScript = ["BYRDONIS_ELITE", "PHROG_PARASITE_ELITE"];
            if (normals < normalScript.Length)
            {
                normalPool = [ScriptedEncounter(normalScript[normals])];
                normalBasis = PoolBasis.FirstRunScript;
            }
            if (elites < eliteScript.Length)
            {
                elitePool = [ScriptedEncounter(eliteScript[elites])];
                eliteBasis = PoolBasis.FirstRunScript;
            }
        }
        var inputs = ReferenceInputs.Read();
        return new EncounterPoolForecast(inputs.Patterns.Build, run.AscensionLevel, inputs.Provenance,
            act.Id.ToString(), run.CurrentActIndex, normals, weak, phase, normalBasis,
            normalPool.Select(model => EncounterFor(model, inputs, run.AscensionLevel)).ToArray(), eliteBasis,
            elitePool.Select(model => EncounterFor(model, inputs, run.AscensionLevel)).ToArray());
    });

    private static EncounterModel ScriptedEncounter(string entry) => ModelDb.All.OfType<EncounterModel>()
        .SingleOrDefault(model => model.Id.ToString() == "ENCOUNTER." + entry)
        ?? throw new IntelRefusal($"The inspected tutorial encounter {entry} is absent.");

    private static IReadOnlyList<IReadOnlyList<MapPointHistoryEntry>> VisibleHistory(RunState run)
    {
        var history = run.MapPointHistory;
        if (history.Count > run.CurrentActIndex + 1 || history.SelectMany(act => act).Any(point => point.Rooms.Count == 0))
            throw new IntelUnavailable(LookupState.NotReady, "The visible room history is not ready for a forecast.");
        return history;
    }

    private static IEnumerable<MapPoint> NextVisitPoints(RunState run)
    {
        var map = run.Map;
        if (map is NullActMap)
            throw new IntelUnavailable(LookupState.NotReady, "The current act's map is not ready for a next-visit forecast.");
        if (map.GetType().Assembly != typeof(ActModel).Assembly)
            throw new IntelRefusal("The current map has no inspected travel rules.");
        var current = run.CurrentMapPoint;
        if (current is null) return [map.StartingMapPoint];
        if (map.SecondBossMapPoint is { } second && current.coord == map.BossMapPoint.coord) return [second];
        if (current.coord.row == map.GetRowCount() - 1) return [map.BossMapPoint];
        // This is the native map's read-only travel rule, including native free travel
        return MapTravel.GetTravelablePointsFrom(run, current);
    }

    private static IReadOnlyList<string> RequireForecastSemantics(RunState run)
    {
        if (SupportedBuild.Require().AssemblySha256 != SupportedBuild.AssemblySha256)
            throw new IntelRefusal("This build has no inspected next-visit forecast rules.");
        string[] hooks = [nameof(AbstractModel.ModifyUnknownMapPointRoomTypes), nameof(AbstractModel.ModifyOddsIncreaseForUnrolledRoomType)];
        var patched = Harmony.GetAllPatchedMethods().FirstOrDefault(method =>
            Harmony.GetPatchInfo(method)?.Owners.Count > 0 && method.DeclaringType?.Assembly == typeof(ActModel).Assembly &&
            (method.DeclaringType?.Namespace is "MegaCrit.Sts2.Core.Odds" or "MegaCrit.Sts2.Core.Map" || method.DeclaringType == typeof(ModHelper) ||
             method.DeclaringType == typeof(MapPointHistoryEntry) || method.DeclaringType == typeof(MapPointRoomHistoryEntry) ||
             method.DeclaringType?.Namespace == "MegaCrit.Sts2.Core.Unlocks" ||
             (method.DeclaringType?.Namespace == "MegaCrit.Sts2.Core.Entities.Players" && method.Name.StartsWith("get_", StringComparison.Ordinal)) ||
             (method.DeclaringType == typeof(RunManager) && method.Name is nameof(RunManager.BuildRoomTypeBlacklist) or nameof(RunManager.ShouldApplyTutorialModifications)) ||
             (method.DeclaringType == typeof(RunState) && method.Name == nameof(RunState.IterateHookListeners)) ||
             (method.DeclaringType == typeof(ActModel) && method.Name == nameof(ActModel.MarkRoomVisited)) ||
             hooks.Contains(method.Name) || method.Name == nameof(AbstractModel.ShouldAllowFreeTravel)));
        if (patched is not null)
            throw new IntelRefusal($"Forecast semantics are unsupported: {patched.DeclaringType?.Name}.{patched.Name} has Harmony patches.");
        // A subscription delegate is arbitrary mod code; refuse before the native iterator invokes it
        var subscribers = typeof(ModHelper).GetField("_runHookSubscribers", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as System.Collections.ICollection
            ?? throw new IntelRefusal("The run-hook subscription registry is unavailable on this build.");
        if (subscribers.Count != 0)
            throw new IntelRefusal("A mod run-hook subscription has no inspected forecast rule.");
        var recognized = new List<string>();
        foreach (var model in run.IterateHookListeners(null))
        {
            RequireNative(model);
            foreach (var hook in hooks)
            {
                var owner = model.GetType().GetMethod(hook)!.DeclaringType;
                if (owner == typeof(AbstractModel)) continue;
                if ((hook == nameof(AbstractModel.ModifyUnknownMapPointRoomTypes) && owner == typeof(JuzuBracelet)) ||
                    (hook == nameof(AbstractModel.ModifyOddsIncreaseForUnrolledRoomType) && owner == typeof(DeadlyEvents)))
                    recognized.Add(owner.FullName!);
                else throw new IntelRefusal($"Forecast hook {owner?.Name}.{hook} has no inspected rule.");
            }
        }
        return recognized.Distinct(StringComparer.Ordinal).ToArray();
    }
}
