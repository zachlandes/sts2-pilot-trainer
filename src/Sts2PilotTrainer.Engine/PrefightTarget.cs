using System.Globalization;
using MegaCrit.Sts2.Core.Map;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Where the recording's next decision lands on the game's own screen, so a host can
/// light it without clicking it.
///
/// The companion of <see cref="Sts2PilotTrainer.Replay.PrefightChoice"/> and
/// deliberately not the same type. A choice says what the decision was, in terms a
/// sentence can be built from, and lives in the format owner. A target says which
/// object on which screen it is about to happen to, which is a fact about this
/// build's map and this build's event: <see cref="MapCoord"/> is the game's own type,
/// so this cannot live anywhere but here.
///
/// It authorises nothing. Revealing a target applies the game's own selected state
/// and never its click path; committing is <see cref="RunDriver"/>'s, through the
/// plan, exactly as it was before a transport existed.
/// </summary>
public abstract record PrefightTarget(int Seq)
{
    /// <summary>
    /// What this points at, in one line.
    ///
    /// Here rather than at each caller because the coordinate is the game's own type:
    /// a command line that has no game assembly can print this, and does.
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// An option on the event screen, by its position among the options offered.
    ///
    /// Carries the relic that option grants as well as the index, so a host lighting
    /// it can check it has the right row rather than trusting that the buttons on
    /// screen are in the order the event listed them.
    /// </summary>
    public sealed record EventOption(int Seq, int Index, string RelicModelId) : PrefightTarget(Seq)
    {
        public override string Description =>
            $"event option {Index.ToString(CultureInfo.InvariantCulture)} granting {RelicModelId}";
    }

    /// <summary>A node on the act's map, by the coordinate the run would enter.</summary>
    public sealed record MapNode(int Seq, MapCoord Coord) : PrefightTarget(Seq)
    {
        public override string Description =>
            $"map node (row {Coord.row.ToString(CultureInfo.InvariantCulture)}, column " +
            $"{Coord.col.ToString(CultureInfo.InvariantCulture)})";
    }
}
