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
/// write into the second one. Null where nobody got as far as reading it: a read that
/// never happened establishes nothing about that file, and answering false would be
/// this row reporting a refusal nobody made.</param>
/// <param name="Disk">How the reading went. Its own fact rather than a count of zero:
/// "no runs yet" and "cannot tell yet" are different sentences, and only one of them is
/// safe to offer a removal beside.</param>
/// <param name="ContinuableRunWouldBeLeft">Whether the runs this policy would take
/// include the one the game can currently Continue, which retention always leaves. The
/// row cannot work this out - which run that is comes from the game - and without it the
/// second line predicts one removal more than the next main menu will actually
/// perform.</param>
/// <param name="MainMenuRowShown">Whether Runmobile is currently a row on the game's own
/// main menu. Derived once, by <see cref="Trainer.MainMenuRow.ShownWhen"/>, from the
/// player's stored choice and this profile's run count - both of which the row would
/// have to read for itself to answer, and one of which comes from the game. The control
/// beside it is what changes it, and it is drawn from the same answer the menu patch
/// draws from so the two can never say different things.</param>
public sealed record MyRunsFacts(
    int Runs,
    long Bytes,
    int Keep,
    int? RemovedJustNow = null,
    bool? SettingsReadable = true,
    MyRunsDisk Disk = MyRunsDisk.Read,
    bool ContinuableRunWouldBeLeft = false,
    bool MainMenuRowShown = false);

/// <summary>
/// How the reading of the player's own disk went.
///
/// Three answers rather than two, because only one failure has a cause this row may
/// name. A player who has not chosen a save profile is looking at a state they resolve
/// by choosing one; every other refusal is a fault, and a row that named the profile
/// for it would be telling them something untrue about a state they cannot act on.
/// </summary>
public enum MyRunsDisk
{
    /// <summary>The disk answered.</summary>
    Read,

    /// <summary>The game has not said whose files these would be. The settings screen
    /// hangs off the main menu, which is reachable before a save profile is
    /// chosen.</summary>
    NoSaveProfileYet,

    /// <summary>Something else refused. The row names no cause because it has not
    /// established one; the game's log carries the exception.</summary>
    Refused,
}

/// <summary>
/// Runmobile's settings row: keep, size, remove, whether the run index is fetched, and
/// whether the mod puts a row on the game's main menu.
///
/// It is named for the player's own runs because that is what most of it is about and
/// what the design's settings section is called. The two switches under it are here
/// rather than in sections of their own for the reason the section exists at all: this
/// mod contributes one place a player configures it, and a second one would be a second
/// thing to find.
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
/// meaning into a document whose schema it refuses. False too before the disk can be
/// asked, because there is no file to write into until a save profile is chosen.</param>
/// <param name="RemoveLabel">The destructive control's label.</param>
/// <param name="RemovePressable">Whether it may be pressed. False with nothing to
/// remove, false over a settings file this build could not read - the removal records
/// the request in that file before it takes anything, so an act that cannot be recorded
/// is one that must not be offered - and false before the disk can be asked whose runs
/// these are. A refused control is drawn rather than
/// hidden, so the row keeps its shape as the number changes, and the second line is
/// what says why.</param>
/// <param name="Confirm">What the game's own popup asks before anything goes.</param>
/// <param name="MainMenu">Whether Runmobile is a row on the game's main menu, and the
/// line the control that governs it carries. Its own record rather than two more strings
/// here, because <see cref="Trainer.MainMenuRow"/> is the one owner of that rule and the
/// mod's menu patch reads the same owner.</param>
/// <param name="MainMenuPressable">Whether that control may be moved. The same condition
/// the policy stepper answers to and for the same reason: the choice is a member of the
/// player's settings file, so a press over a file this build refuses would write this
/// build's meaning into a document written by another, and before a save profile is
/// chosen there is no file to write into at all.</param>
public sealed record MyRunsRow(
    string Reading,
    string Detail,
    string KeepLabel,
    string KeepNumeral,
    bool KeepPressable,
    string RemoveLabel,
    bool RemovePressable,
    MyRunsConfirm Confirm,
    MainMenuRow MainMenu,
    bool MainMenuPressable)
{
    /// <summary>The smallest number of runs the slider may be moved to. A control that
    /// could be dragged to zero would be a standing purge a player set by accident;
    /// asking for every run to go is the ribbon's job and it asks first.</summary>
    public const int MinimumKeep = 1;

    /// <summary>Where the runs are, in the words the row uses for it.</summary>
    private const string Directory = "user://Runmobile/recordings";

    /// <summary>What the row reads before the game has said whose runs these are. The
    /// whole row is that one line: a reading, a receipt and a policy all describe a
    /// disk this build cannot yet name, and a second line under it would be describing
    /// it too.</summary>
    private const string NoSaveProfileYet = "Your runs are read once you have chosen a save profile";

    /// <summary>And what it reads when the disk refused for any other reason. It names
    /// no cause: what went wrong is in the game's log, and a row that guessed at one
    /// would be stating a reason nobody read.</summary>
    private const string Refused = "Your runs could not be read; the game's log says why";

    /// <summary>
    /// The row, for what is true right now. Total and pure: every combination of the
    /// facts has an answer, including none of them.
    /// </summary>
    public static MyRunsRow For(MyRunsFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var size = Size(facts.Bytes);
        var runs = Runs(facts.Runs);
        var read = facts.Disk == MyRunsDisk.Read;
        var settingsRead = facts.SettingsReadable == true;
        return new MyRunsRow(
            Reading: facts.Disk switch
            {
                MyRunsDisk.NoSaveProfileYet => NoSaveProfileYet,
                MyRunsDisk.Refused => Refused,
                _ => $"{runs} · {size}",
            },
            Detail: read ? DetailLine(facts) : string.Empty,
            KeepLabel: LibraryCopy.KeepMyRuns,
            KeepNumeral: facts.Keep.ToString(CultureInfo.InvariantCulture),
            KeepPressable: read && settingsRead,
            RemoveLabel: LibraryCopy.RemoveMyRuns,
            RemovePressable: read && settingsRead && facts.Runs > 0,
            Confirm: new MyRunsConfirm(
                Title: "Remove all your runs?",
                Body: $"{runs}, {size}, recorded by Runmobile. Your saves, profile and run history are not " +
                      "touched.",
                Remove: "Remove",
                Keep: "Keep them"),
            MainMenu: new MainMenuRow(facts.MainMenuRowShown),
            MainMenuPressable: read && settingsRead);
    }

    /// <summary>
    /// How many runs the standing policy would remove at the next main menu.
    ///
    /// The arithmetic alone would overstate it. Retention never removes the run the
    /// game can currently Continue, whatever the policy says, so where that run is
    /// among the ones a policy names it is one fewer than the subtraction - which a
    /// player only ever sees under a hand-written policy of zero, and which is a
    /// promise the row would otherwise break every time.
    ///
    /// Private because the row's second line is the only place this number is ever
    /// stated: a caller that worked it out for itself could offer a number the row
    /// disagreed with.
    /// </summary>
    private static int Pending(MyRunsFacts facts) =>
        Math.Max(0, Math.Max(0, facts.Runs - facts.Keep) - (facts.ContinuableRunWouldBeLeft ? 1 : 0));

    /// <summary>
    /// A number of bytes as a player reads it.
    ///
    /// The unit follows the magnitude rather than being fixed, because a fixed one is
    /// wrong at one end or the other: a single run in megabytes rounds to nothing while
    /// the row says there is one, and a full library in kilobytes is a number nobody can
    /// weigh. Nothing at all reads as zero megabytes, which is the design's own word for
    /// an empty library.
    ///
    /// The unit follows the figure after it is rounded rather than the byte count it
    /// came from. Rounding first and choosing the unit second is what stops a library a
    /// few hundred bytes short of a megabyte reading as "1024 KB": the two questions
    /// look independent and are not, because rounding is what can carry a figure into
    /// the next unit.
    /// </summary>
    public static string Size(long bytes)
    {
        const long kb = 1024L;
        const long mb = kb * 1024L;
        const long gb = mb * 1024L;

        if (bytes <= 0) return "0 MB";

        var kilobytes = Math.Max(1, (bytes + kb - 1) / kb);
        if (kilobytes < kb) return $"{kilobytes.ToString(CultureInfo.InvariantCulture)} KB";

        var megabytes = (double)bytes / mb;
        if (bytes < gb && Math.Round(megabytes) < kb) return $"{Fraction(megabytes)} MB";

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

        if (facts.SettingsReadable != true)
        {
            return "settings.json could not be read, so no runs are removed automatically until it is · " +
                   Directory;
        }

        var pending = Pending(facts);
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
