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
/// It refuses rather than substituting. A manifest with no creator is a manifest a
/// host cannot honestly attribute, and putting a channel id or a run id on screen in
/// place of a name would be a host inventing an attribution.
/// </summary>
public static class RecordingIdentity
{
    public static string Creator(ReplayManifest recording)
    {
        var name = recording.Source.Video?.ChannelName;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ManifestException(
                $"Recording '{recording.RunId}' does not say whose run it is (source.video.channel_name is " +
                "absent), so nothing here can name them.");
        }

        return name;
    }

    /// <summary>What this recording is, under the screen's title.</summary>
    public static string Subtitle(ReplayManifest recording) => TrainerCopy.Subtitle(
        Creator(recording),
        recording.Environment.Character.Value,
        recording.Environment.Ascension.Value);

    /// <summary>What this recording is, in the game's mod list and on the mode card.</summary>
    public static string Description(ReplayManifest recording) =>
        TrainerCopy.Description(Creator(recording));
}
