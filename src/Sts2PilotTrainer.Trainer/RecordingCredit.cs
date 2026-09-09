namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Whose recording this is, in the grammatical forms the screens need.
///
/// A name was enough while every recording came from somebody else's video. A run the
/// player recorded themselves is credited to them, while a received native run is
/// credited neutrally. English does not let one noun stand in every slot a name stood
/// in: "NaveGreed took Burning Blood" becomes "You
/// took Burning Blood", and "Watch NaveGreed's fight" becomes "Watch your fight". A
/// single string forced into both would read "Watch You's fight", which is why this
/// carries the forms rather than a name.
///
/// Three forms are authored and one is derived from another, so there is nothing here
/// for a fourth caller to invent. Every authored form lives in
/// <see cref="TrainerCopy"/> with the rest of the fixed words;
/// <see cref="RecordingIdentity"/> is the one thing that decides which credit a
/// recording gets.
/// </summary>
/// <param name="Subject">The credit as the subject of a sentence, mid sentence:
/// "NaveGreed", "you". Lower case where the word is lower case; a use that opens a
/// sentence takes <see cref="RecordingCredit.OpeningSubject"/>.</param>
/// <param name="Possessive">The credit before the thing it owns: "NaveGreed's",
/// "your". Lower case where the word is lower case, so it reads correctly mid
/// sentence.</param>
/// <param name="Label">The credit as a name in a list, a column or the transport's own
/// tag: "NaveGreed", "Your run". A slot where nothing follows it, so it takes the
/// fuller phrase the possessive cannot.</param>
/// <param name="IsYours">Whether this recording is the player's own. Two sentences
/// change shape rather than word for a recording of one's own and ask this; nothing
/// else reads it, and no surface draws a row or a control differently for it.</param>
public sealed record RecordingCredit(
    string Subject, string Possessive, string Label, bool IsYours)
{
    /// <summary>The subject opening a sentence: "NaveGreed took Burning Blood", "You
    /// took Burning Blood".</summary>
    public string OpeningSubject => Opening(Subject);

    /// <summary>The possessive opening a sentence: "NaveGreed's choices are shown as
    /// recorded", "Your choices are shown as recorded".</summary>
    public string OpeningPossessive => Opening(Possessive);

    /// <summary>
    /// A form capitalized because it opens a sentence.
    ///
    /// Derived rather than authored: capitalizing the first letter is a rule of the
    /// language rather than a wording decision, and two more authored strings would be
    /// two more things to keep in step. A name is already capitalized, so this changes
    /// nothing for a recording credited to somebody else.
    /// </summary>
    private static string Opening(string form) =>
        form.Length == 0 ? form : char.ToUpperInvariant(form[0]) + form[1..];

    /// <summary>The credit for a recording somebody else made, named by the manifest.</summary>
    public static RecordingCredit Named(string name) => new(name, $"{name}'s", name, IsYours: false);

    /// <summary>The credit for a native run whose player is not known to the viewer.</summary>
    public static RecordingCredit Neutral { get; } = new(
        TrainerCopy.ThisRunSubject, TrainerCopy.ThisRunPossessive, TrainerCopy.ThisRunLabel, IsYours: false);

    /// <summary>The credit for a run this player recorded themselves.</summary>
    public static RecordingCredit Yours { get; } = new(
        TrainerCopy.YouSubject, TrainerCopy.YourPossessive, TrainerCopy.YourRunLabel, IsYours: true);
}
