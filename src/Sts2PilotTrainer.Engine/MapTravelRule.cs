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
/// The two lines before the engine's rule are <c>NMapScreen.RecalculateTravelability</c>'s
/// own, in its order: the boss's node leads to the second boss where the act has one,
/// and the last row leads to the boss, whose node sits outside the grid the engine's
/// row query reads. A free-travel hook on the last row would otherwise light nothing.
/// </summary>
public static class MapTravelRule
{
    public static IReadOnlyList<MapPoint> TravelableFrom(IRunState runState, ActMap map, MapPoint currentPoint)
    {
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
