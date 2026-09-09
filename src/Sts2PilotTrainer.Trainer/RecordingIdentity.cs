using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Who a recording is by, as the screens name them.
///
/// One reader, because "NaveGreed" appears in a chip, in a caption, in a subtitle
/// and in the mod list, and four copies of a lookup are four places for a second
/// recording to be half-adopted. Every one of them comes from the manifest's own
/// source record.
///
/// <para>Two recordings are credited, and the second is the reason this returns a
/// <see cref="RecordingCredit"/> rather than a name. A reconstruction from a public
/// video is credited to the channel it came from. A run made inside the player's own
/// game is credited to the player only when the caller knows it is theirs. A received
/// native recording is credited neutrally, because its source says how it was captured,
/// not who is viewing it. A recording holds no author field and never will, because a
/// field naming the person who made it would travel with every copy of it forever.</para>
///
/// <para>It still refuses rather than substituting. A manifest that is neither of those
/// two things and names nobody is a manifest a host cannot honestly attribute, and
/// putting a channel id or a run id on screen in place of a name would be a host
/// inventing an attribution.</para>
/// </summary>
public static class RecordingIdentity
{
    /// <summary>
    /// Whose recording this is, in the forms a sentence needs.
    ///
    /// The one reading. Everything a player sees about whose run they are watching comes
    /// through here, so a recording cannot be credited one way on the transport's tag
    /// and another on its captions.
    /// </summary>
    /// <exception cref="ManifestException">When the manifest is neither a named video
    /// reconstruction nor a native recording.</exception>
    public static RecordingCredit Credit(ReplayManifest recording, bool isPlayersOwn = false) =>
        CreditOrNull(recording, isPlayersOwn)
        ?? throw new ManifestException(
            $"Recording '{recording.RunId}' does not say whose run it is: it names no creator " +
            "(source.video.channel_name is absent) and is not a native recording, so nothing here can " +
            "credit it.");

    /// <summary>
    /// The same, and null rather than a refusal where the manifest credits nobody.
    ///
    /// For the one caller that is not a screen: a generated fixture has no creator
    /// because there is nobody behind it and was not played by anybody either, and a
    /// tool walking one through its own decisions should say so rather than fail.
    /// Everything a player reads goes through <see cref="Credit"/>, which still refuses.
    /// </summary>
    public static RecordingCredit? CreditOrNull(
        ReplayManifest recording, bool isPlayersOwn = false)
    {
        if (CreatorOrNull(recording) is { } named) return RecordingCredit.Named(named);
        if (recording.Source.Native is null) return null;

        return isPlayersOwn ? RecordingCredit.Yours : RecordingCredit.Neutral;
    }

    /// <summary>
    /// The same credit as a plain name, for a slot with nothing after it.
    ///
    /// A projection of <see cref="Credit"/> rather than a second lookup. A caller that
    /// puts the name into a sentence wants the credit itself: "NaveGreed's fight" and
    /// "your fight" are the same slot and different words.
    /// </summary>
    public static string Creator(ReplayManifest recording, bool isPlayersOwn = false) =>
        Credit(recording, isPlayersOwn).Label;

    /// <summary>
    /// The creator the manifest itself names, and null where it names nobody.
    ///
    /// Deliberately not the credit: this answers "does this recording carry an
    /// attribution", which is what the library's own row asks before it draws a
    /// creator line, and what <see cref="Credit"/> asks before it falls through to the
    /// native path. A run of the player's own names nobody and that is the correct
    /// answer here; the row leaves the line out rather than telling them who they are.
    /// </summary>
    public static string? CreatorOrNull(ReplayManifest recording)
    {
        var name = recording.Source.Video?.ChannelName;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>What this recording is, under the screen's title.</summary>
    public static string Subtitle(ReplayManifest recording, bool isPlayersOwn = false) => TrainerCopy.Subtitle(
        Credit(recording, isPlayersOwn),
        recording.Environment.Character.Value,
        recording.Environment.Ascension.Value);
}
