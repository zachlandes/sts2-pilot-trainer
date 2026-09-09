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
    /// <summary>
    /// An opening blessing, named by the relic the chosen option grants and by the
    /// cards the recording picked off the screen that relic opened.
    ///
    /// The cards are usually none, and they are none wherever the host puts that
    /// screen in front of the player: there the picks are decisions of their own, each
    /// shown being made on the game's own screen, and naming them here as well would
    /// say the same thing twice and say it before it happened. Where the screen is
    /// never drawn - headlessly, where the engine's own seam answers it inside the call
    /// the blessing makes - this line is the only place the card is said, and a caption
    /// that named only the relic would leave a deck that quietly lost a card with
    /// nothing having said which.
    /// </summary>
    public sealed record Blessing(int Seq, string RelicModelId, IReadOnlyList<string> CardsPicked)
        : PrefightChoice(Seq)
    {
        public Blessing(int seq, string relicModelId) : this(seq, relicModelId, []) { }
    }

    /// <summary>
    /// A move to a map node, named by the kind of node and where it sits.
    ///
    /// The column is carried with the map's width rather than as a word, because
    /// where "the middle" is depends on how wide the act is and only the map knows.
    /// </summary>
    public sealed record MapMove(int Seq, string NodeType, int Column, int ColumnCount)
        : PrefightChoice(Seq);

    /// <summary>
    /// A card taken off the selection screen an earlier decision opened, named by the
    /// card.
    ///
    /// Only ever built where the host draws that screen, which is what makes it a
    /// decision a watcher sees rather than an answer the engine took inside the call
    /// that opened it. It says the card and not what became of it: whether the relic
    /// that opened the screen removes, transforms or upgrades is the relic's own name
    /// to carry, and a sentence that guessed the verb would be stating something
    /// nobody read.
    /// </summary>
    public sealed record CardFromScreen(int Seq, string CardModelId) : PrefightChoice(Seq);
}
