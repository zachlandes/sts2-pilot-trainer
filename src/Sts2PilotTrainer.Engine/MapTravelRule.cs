using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Where the run standing on a map node may move next, decided the way the map screen
/// decides which nodes to light.
///
/// One owner, because the driver enforces it on every recorded move and the recorder
/// nominates a negative control's alternative from it, and the two once disagreed with
/// the game: both read the node's own children, and the game reads
/// <see cref="MapTravel.GetTravelablePointsFrom"/>, which is the whole next row while a
/// free-travel hook is live - Winged Boots until it is used up, the Flight modifier
/// throughout - and the children otherwise. A recording of a player flying to a node
/// their path did not lead to was refused as unreachable, on an ordinary singleplayer
/// path, because the driver had a rule of its own.
///
/// The lines before the engine's rule are <c>NMapScreen.RecalculateTravelability</c>'s
/// own, in its order: a run that has visited no node of this act's map - which every
/// act after the first is until its first move, because the act change clears the
/// visited coordinates - is offered the act's starting point and nothing else; the
/// boss's node leads to the second boss where the act has one; and the last row leads
/// to the boss, whose node sits outside the grid the engine's row query reads. A
/// free-travel hook on the last row would otherwise light nothing, and without the
/// first line every recording of a run past its first act was refused at the second
/// act's own opening move, as unreachable from a node the run was not standing on.
/// </summary>
public static class MapTravelRule
{
    /// <param name="currentPoint">The node the run stands on, or null where it has
    /// visited none of this map's nodes yet: the start of every act after the first.</param>
    public static IReadOnlyList<MapPoint> TravelableFrom(IRunState runState, ActMap map, MapPoint? currentPoint)
    {
        if (currentPoint is null)
        {
            return [map.StartingMapPoint];
        }

        if (map.SecondBossMapPoint is { } secondBoss && currentPoint.coord == map.BossMapPoint.coord)
        {
            return [secondBoss];
        }

        if (currentPoint.coord.row == map.GetRowCount() - 1)
        {
            return [map.BossMapPoint];
        }

        return MapTravel.GetTravelablePointsFrom(runState, currentPoint).ToList();
    }
}
