namespace Sts2PilotTrainer.Replay;

/// <summary>
/// One of the recording's decisions before its fight, in the terms a host needs to
/// say what it was: which thing, not which words.
///
/// Here rather than in the wording owner because the values are facts about the run
/// - the relic an opening blessing grants, the kind of node a move enters - and the
/// wording owner must be able to be handed them without knowing how they were read.
/// A host that hardcoded "Leafy Poultice" would be a host that could only ever carry
/// one recording, which is exactly what this type exists to prevent.
///
/// Nothing here is a decision. Every value is read from the run the recording's own
/// action is about to act on, and the action is the manifest's.
/// </summary>
public abstract record PrefightChoice(int Seq)
{
    /// <summary>An opening blessing, named by the relic the chosen option grants.</summary>
    public sealed record Blessing(int Seq, string RelicModelId) : PrefightChoice(Seq);

    /// <summary>
    /// A move to a map node, named by the kind of node and where it sits.
    ///
    /// The column is carried with the map's width rather than as a word, because
    /// where "the middle" is depends on how wide the act is and only the map knows.
    /// </summary>
    public sealed record MapMove(int Seq, string NodeType, int Column, int ColumnCount)
        : PrefightChoice(Seq);
}
