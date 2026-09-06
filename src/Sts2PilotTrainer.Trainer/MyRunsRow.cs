using System.Globalization;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// What is true about the player's own runs on this disk, at the moment the settings
/// row is drawn.
///
/// Facts, not words. Every one of them is read - the runs and the bytes off the disk,
/// the policy out of the player's settings file, the removal count out of the act that
/// just ran - and none is a state somebody set. <see cref="MyRunsRow.For"/> is the one
/// thing that turns them into a row.
/// </summary>
/// <param name="Runs">How many recorded runs the store holds. Runs rather than files:
/// a run's journal and its manifest are one recording.</param>
/// <param name="Bytes">What those runs occupy, summed over the files each is made
/// of.</param>
/// <param name="Keep">How many of the newest runs the policy in effect keeps. The
/// player's own number where their file could be read, not a control position: a file
/// may say zero, which is a standing purge, and the numeral says so even though the
/// control cannot be moved there. Where it could not be read this is the default shown
/// in its place, and <paramref name="SettingsReadable"/> is what says which of the two
/// a reader is looking at.</param>
/// <param name="RemovedJustNow">How many runs the purge this screen ran removed, or
/// null when it has not run one. Zero is a real answer and is not null: somebody
/// pressed Remove and deserves the receipt whether or not there was anything to
/// take.</param>
/// <param name="SettingsReadable">Whether the policy above is the player's own
/// sentence or the default shown in place of one this build could not read. Carried as
/// a fact rather than left to be inferred from the number, because "the file says
/// fifty" and "nobody could read the file, so no policy is in force at all" are two
/// different things to tell a player, and neither this row nor anything behind it may
/// write into the second one.</param>
public sealed record MyRunsFacts(
    int Runs, long Bytes, int Keep, int? RemovedJustNow = null, bool SettingsReadable = true);

/// <summary>
/// The player's settings row about their own runs: keep, size, remove.
///
/// One derivation for the whole row, and the row is drawn from it without asking a
/// second question. That is the same rule <see cref="PlaybackTransport"/> answers to
/// and for the same reason: three separate things here can disagree - the number the
/// policy keeps, the number on the disk, and what a purge just did - and a surface that
/// let each of them set its own label would eventually show a reading taken before an
/// act and a receipt taken after it.
///
/// <para><b>The row states what is, and the ribbon does what it says.</b> The reading
/// is re-taken from the disk after a removal rather than predicted from it, so a purge
/// that left the continuable run's journal behind - which is
/// <c>RecordingRetention</c>'s rule, not this row's - reads as the one run it actually
/// left rather than as the zero it was asked for.</para>
///
/// <para>Every word here is the accepted wording of the run-library design's settings
/// section, and its vocabulary ruling is what the row is written in: a player has
/// <em>runs</em>. Journal, manifest and recording are this project's internal names and
/// none of them appears on the surface.</para>
/// </summary>
/// <param name="Reading">The row's first line: how many runs, and what they
/// take.</param>
/// <param name="Detail">Its second line: where they are, and - when there is one - what
/// just happened to them or what is about to.</param>
/// <param name="KeepLabel">The standing policy's own label.</param>
/// <param name="KeepNumeral">The number of runs that policy keeps, as the player reads
/// it.</param>
/// <param name="KeepPressable">Whether the policy may be moved. False where this build
/// could not read the settings file: the numeral is then the default standing in for a
/// sentence nobody could read, and a press would write a member this build's own
/// meaning into a document whose schema it refuses.</param>
/// <param name="RemoveLabel">The destructive control's label.</param>
/// <param name="RemovePressable">Whether it may be pressed. False with nothing to
/// remove, and false over a settings file this build could not read: the removal
/// records the request in that file before it takes anything, so an act that cannot be
/// recorded is one that must not be offered. A refused control is drawn rather than
/// hidden, so the row keeps its shape as the number changes, and the second line is
/// what says why.</param>
/// <param name="Confirm">What the game's own popup asks before anything goes.</param>
public sealed record MyRunsRow(
    string Reading,
    string Detail,
    string KeepLabel,
    string KeepNumeral,
    bool KeepPressable,
    string RemoveLabel,
    bool RemovePressable,
    MyRunsConfirm Confirm)
{
    /// <summary>The smallest number of runs the slider may be moved to. A control that
    /// could be dragged to zero would be a standing purge a player set by accident;
    /// asking for every run to go is the ribbon's job and it asks first.</summary>
    public const int MinimumKeep = 1;

    /// <summary>Where the runs are, in the words the row uses for it.</summary>
    private const string Directory = "user://Runmobile/recordings";

    /// <summary>
    /// The row, for what is true right now. Total and pure: every combination of the
    /// facts has an answer, including none of them.
    /// </summary>
    public static MyRunsRow For(MyRunsFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var size = Size(facts.Bytes);
        var runs = Runs(facts.Runs);
        return new MyRunsRow(
            Reading: $"{runs} · {size}",
            Detail: DetailLine(facts),
            KeepLabel: "Keep my runs",
            KeepNumeral: facts.Keep.ToString(CultureInfo.InvariantCulture),
            KeepPressable: facts.SettingsReadable,
            RemoveLabel: "Remove all my runs",
            RemovePressable: facts.Runs > 0 && facts.SettingsReadable,
            Confirm: new MyRunsConfirm(
                Title: "Remove all your runs?",
                Body: $"{runs}, {size}, recorded by Runmobile. Your saves, profile and run history are not " +
                      "touched.",
                Remove: "Remove",
                Keep: "Keep them"));
    }

    /// <summary>
    /// How many runs a standing policy of keeping <paramref name="keep"/> of them would
    /// remove from <paramref name="runs"/>, at the next main menu.
    ///
    /// Private because the row's second line is the only place this number is ever
    /// stated: a caller that worked it out for itself could offer a number the row
    /// disagreed with.
    /// </summary>
    private static int Pending(int runs, int keep) => Math.Max(0, runs - keep);

    /// <summary>
    /// A number of bytes as a player reads it.
    ///
    /// The unit follows the magnitude rather than being fixed, because a fixed one is
    /// wrong at one end or the other: a single run in megabytes rounds to nothing while
    /// the row says there is one, and a full library in kilobytes is a number nobody can
    /// weigh. Nothing at all reads as zero megabytes, which is the design's own word for
    /// an empty library.
    /// </summary>
    public static string Size(long bytes)
    {
        const long kb = 1024L;
        const long mb = kb * 1024L;
        const long gb = mb * 1024L;

        if (bytes <= 0) return "0 MB";
        if (bytes < mb) return $"{Math.Max(1, (bytes + kb - 1) / kb).ToString(CultureInfo.InvariantCulture)} KB";
        if (bytes < gb) return $"{Fraction((double)bytes / mb)} MB";
        return $"{Fraction((double)bytes / gb)} GB";
    }

    /// <summary>
    /// The second line: where the runs are, and what is happening to them.
    ///
    /// One of four, in a stated order. A receipt beats everything because the removal
    /// already happened and the rest is about the next main menu. A settings file this
    /// build could not read beats the warning below it, and says what is actually true
    /// under it: an unreadable file leaves no policy in force at all, so no run is
    /// removed of this mod's own accord until somebody puts the file right. The number
    /// above is the default shown in its place and nothing may be inferred from it - a
    /// warning issued from it would name runs that are not going anywhere. A warning
    /// beats the plain line because a policy that is about to take runs away is the
    /// thing a player on this screen needs to read. The directory is on all four,
    /// because "on this computer" is the claim the whole row exists to make.
    /// </summary>
    private static string DetailLine(MyRunsFacts facts)
    {
        if (facts.RemovedJustNow is { } removed)
        {
            return $"{Runs(removed)} removed just now · {Directory}";
        }

        if (!facts.SettingsReadable)
        {
            return "settings.json could not be read, so no runs are removed automatically until it is · " +
                   Directory;
        }

        var pending = Pending(facts.Runs, facts.Keep);
        return pending > 0
            ? $"{Older(pending)} will be removed at the main menu · {Directory}"
            : $"on this computer, in {Directory}";
    }

    /// <summary>
    /// A count of runs, in words. Singular where it is one: the design writes every
    /// count as a template and a row reading "1 runs" is the template showing through.
    /// </summary>
    private static string Runs(int count) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? "run" : "runs")}";

    private static string Older(int count) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} older {(count == 1 ? "run" : "runs")}";

    /// <summary>One decimal below a hundred and none above it, so the figure carries
    /// about three digits of meaning whatever it is.</summary>
    private static string Fraction(double value) =>
        value < 100
            ? value.ToString("0.#", CultureInfo.InvariantCulture)
            : Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
}

/// <summary>
/// What the game's own popup asks before a player's runs go.
///
/// It names what goes and what does not, because the fear a player brings to a red
/// control on a mod's settings screen is about the save behind it. The sentence is a
/// claim this mod can keep: <c>RunmobileStore</c> refuses every path outside
/// <c>user://Runmobile/</c>, so a save, a profile and run history are not files this
/// removal can name.
/// </summary>
/// <param name="Keep">The way out, and the popup's default: leaving is what a player
/// who opened this by accident wants.</param>
public sealed record MyRunsConfirm(string Title, string Body, string Remove, string Keep);
