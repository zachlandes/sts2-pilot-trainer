using System.Globalization;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// One line of the eligibility screen: what is required, what this game answered,
/// and - when it did not answer yes - the engine's own sentence, verbatim.
///
/// The state is the gate's own <see cref="PreflightOutcome"/> and not a second
/// vocabulary beside it. A row is a projection of one rule's verdict, so a row that
/// could disagree with the rule it draws is the whole class of defect this screen is
/// written to make impossible - and a third row state invented here, mapped from a
/// bool, would be exactly that.
/// </summary>
public sealed record EligibilityRow(string Label, PreflightOutcome State, string? Note = null)
{
    public bool Met => State == PreflightOutcome.Met;

    /// <summary>
    /// Whether this row is something the player can go and do.
    ///
    /// The distinction the screen is drawn from. A locked act and a missing ascension
    /// are errands; a build that does not ship the content the recording names is a
    /// statement, and drawing the two the same way told somebody to go and unlock
    /// content that does not exist in their game.
    /// </summary>
    public bool Actionable => State == PreflightOutcome.NotMet;
}

/// <summary>
/// Everything the Combat Trainer's one screen says, computed from the selected
/// manifest and a live preflight verdict, with no idea how any of it is drawn.
///
/// The rules here are about presentation only. Nothing recomputes a gate: every
/// row's state comes from a <see cref="PreflightField"/> that
/// <see cref="EnvironmentPreflight"/> produced, and every sentence about a failure
/// is that field's own diagnostic passed through unchanged. A refusal the screen
/// has no row for is still shown - as its sentence - because a gate that failed and
/// said nothing is the failure mode this project exists to prevent.
///
/// A row therefore has three states and not two, because a gate has three. Met, an
/// errand, and a plain statement that this build cannot supply what the recording
/// names: the third is the one a player cannot act on, and collapsing it into the
/// second is how a screen ends up telling somebody to go and unlock content that is
/// not in their game.
///
/// Which reading it is handed decides what every row is about, and the caller owns
/// that choice rather than this file. A host that constructs the run hands it the
/// reading the run will be generated against, so each row states a requirement of
/// the fight being offered; a host asking whether somebody could play the run
/// themselves hands it their profile. Showing one and gating on the other is how a
/// screen ends up warning about something that stops nothing.
/// </summary>
public sealed record EligibilityScreen(
    string Title,
    string Subtitle,
    string RecordingLine,
    string Headline,
    bool Eligible,
    IReadOnlyList<EligibilityRow> Rows,
    IReadOnlyList<string> Refusals,
    string ProfileNote,
    string BackButton)
{
    /// <summary>
    /// Whether the recording's fight is offered, and the note that goes with the
    /// offer.
    ///
    /// Set only when this game can construct the recording's run. The rows and the
    /// offer are both evaluated against the complete unlock state that run will use,
    /// supplied in memory and written nowhere. See
    /// EnvironmentPreflight.EvaluateAscensionCeiling, which records why a host
    /// constructing a run directly never consults the profile ceiling.
    /// </summary>
    public bool FightOffered { get; init; }

    public string EnterButton => TrainerCopy.EnterButton;

    public string NotSavedNote => TrainerCopy.NotSavedNote;

    /// <summary>
    /// Field names this screen turns into a row. Anything failing and not in here
    /// surfaces as a refusal sentence instead, so adding a gate to
    /// <see cref="EnvironmentPreflight"/> can never make the screen quietly show one
    /// requirement fewer.
    /// </summary>
    private const string BuildField = "build_version";
    private const string ContentHashField = "content_hash";
    private const string ActsField = "acts_unlocked";
    private const string AscensionField = "ascension_unlocked";
    private const string UnlockCategoryPrefix = "unlocks_";

    /// <summary>
    /// The two unlock fields that are not a category of content, so neither has an
    /// "n of m" row to live in.
    ///
    /// The requirement names what the manifest asked for. The run count is a value an
    /// exact state is built from and is reported rather than compared - nothing about
    /// this installation has to match it - so a row for it says "Runs: supplied to the
    /// run of 11", which is not a requirement and not a sentence. The report keeps
    /// both; the screen states requirements only.
    /// </summary>
    private const string UnlockRequirementField = "unlocks_requirement";

    /// <inheritdoc cref="UnlockRequirementField"/>
    private const string UnlockRunsField = "unlocks_runs";

    public static EligibilityScreen For(
        ReplayManifest recording, LivePreflight preflight, bool fightOffered = false)
    {
        var expected = recording.Environment;
        var fields = preflight.Fields;
        var rows = new List<EligibilityRow>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        AddRow(rows, claimed, fields, BuildField, field => $"Build {field.Expected}");
        AddRow(rows, claimed, fields, ContentHashField,
            field => $"Content hash {field.Expected}",
            // Shown whether or not the row is green: a matching hash rules out one
            // class of divergence and is not proof of environment parity, and the
            // engine already owns the sentence that says so.
            _ => EnvironmentPreflight.ContentHashScope);

        foreach (var field in fields)
        {
            if (!field.Field.StartsWith(UnlockCategoryPrefix, StringComparison.Ordinal)) continue;
            if (field.Field is UnlockRequirementField or UnlockRunsField) continue;
            claimed.Add(field.Field);
            var category = CategoryLabel(field.Field[UnlockCategoryPrefix.Length..]);
            rows.Add(new EligibilityRow(
                $"{category}: {field.Actual} of {field.Expected}", field.Outcome, field.Diagnostic));
        }

        AddActRows(rows, claimed, fields, expected, preflight.Reading);

        AddRow(rows, claimed, fields, AscensionField,
            _ => $"Ascension {expected.Ascension.Value.ToString(CultureInfo.InvariantCulture)} available on " +
                 ModelIdNames.Display(expected.Character.Value));

        var refusals = fields
            .Where(field => !field.Matches && !claimed.Contains(field.Field))
            .Select(field => field.Diagnostic)
            .OfType<string>()
            .ToList();

        // Three tiers, in the order the gates asked them within each. What this build
        // cannot supply comes first, because it decides the answer and reading a list
        // of errands above it would waste somebody's evening; then the errands; then
        // what already passed. Nothing is hidden and no row changes: a row that
        // carries a qualifier still carries it, wherever it lands.
        List<EligibilityRow> ordered =
        [
            .. rows.Where(row => row.State == PreflightOutcome.Unavailable),
            .. rows.Where(row => row.Actionable),
            .. rows.Where(row => row.Met),
        ];

        return new EligibilityScreen(
            Title: TrainerCopy.Name,
            Subtitle: RecordingIdentity.Subtitle(recording),
            RecordingLine: TrainerCopy.RecordingLine(expected.BuildVersion.Value, expected.BuildDateUtc.Value),
            Headline: HeadlineFor(preflight),
            Eligible: preflight.Matches,
            Rows: ordered,
            Refusals: refusals,
            // Said only where it is true. The note names the profile the rows were
            // measured against, and where the host supplies the state instead there
            // is no profile in the answer - a sentence pointing at one would send a
            // player to import progress that nothing here reads.
            ProfileNote: preflight.Reading.Unlocks.FromPlayerProfile ? TrainerCopy.ProfileNote : string.Empty,
            BackButton: TrainerCopy.BackButton)
        {
            FightOffered = fightOffered,
        };
    }

    /// <summary>
    /// The one sentence above the rows, and which of the three it is.
    ///
    /// A single unavailable field decides it, however many errands sit beside it: the
    /// player could run every one of those errands and this build would still not be
    /// able to play the recording, so the headline that says "yet" would be a promise
    /// nothing can keep. The fields are asked rather than the rows, because a gate
    /// this screen has no row shape for is still a gate that failed.
    /// </summary>
    private static string HeadlineFor(LivePreflight preflight)
    {
        if (preflight.Matches) return TrainerCopy.PassHeadline;

        return preflight.Fields.Any(field => field.Outcome == PreflightOutcome.Unavailable)
            ? TrainerCopy.UnavailableHeadline
            : TrainerCopy.FailHeadline;
    }

    private static void AddRow(
        List<EligibilityRow> rows,
        HashSet<string> claimed,
        IReadOnlyList<PreflightField> fields,
        string fieldName,
        Func<PreflightField, string> label,
        Func<PreflightField, string?>? note = null)
    {
        var field = fields.FirstOrDefault(candidate => candidate.Field == fieldName);
        // A gate this build did not run has no row. Saying "met" about a question
        // nobody asked is the one thing a screen like this must never do; the
        // headline still carries the verdict, and a failure would have a sentence.
        if (field is null) return;

        claimed.Add(fieldName);
        rows.Add(new EligibilityRow(
            label(field),
            field.Outcome,
            note is null ? field.Diagnostic : note(field) ?? field.Diagnostic));
    }

    /// <summary>
    /// One row per act the manifest climbs.
    ///
    /// Per act rather than one row for the list, because the acts are not
    /// interchangeable: this build ships two acts at index 0, and taking the wrong
    /// one generates different content from the same seed behind an identical map.
    /// Which of them this environment is missing comes from the same reading the
    /// gate judged, never from parsing the gate's own sentence back apart.
    ///
    /// Splitting the gate's one verdict into rows is a projection and stays one: the
    /// gate decides whether the acts were asked about at all, and the reading is
    /// consulted only where it says they were. Turning the unasked case into a locked
    /// act - which is what a missing answer read as "locked" amounts to - is a claim
    /// nobody measured, and it is the claim that sent a player off to unlock an act
    /// their build does not ship.
    /// </summary>
    private static void AddActRows(
        List<EligibilityRow> rows,
        HashSet<string> claimed,
        IReadOnlyList<PreflightField> fields,
        EnvironmentIdentity expected,
        LocalPrerequisites reading)
    {
        var field = fields.FirstOrDefault(candidate => candidate.Field == ActsField);
        if (field is null) return;

        claimed.Add(ActsField);
        var asked = field.Outcome != PreflightOutcome.Unavailable ? reading.LockedActs : null;

        // Said once where it is about the state rather than about an act. A locked act
        // is a fact about that act and its row carries the sentence; a question that
        // was never asked is one fact about the whole reading, and repeating it under
        // every act would print the same paragraph three times to say it.
        var explained = false;
        foreach (var act in expected.Acts.Value)
        {
            var state = asked is not { } locked
                ? PreflightOutcome.Unavailable
                : locked.Contains(act, StringComparer.Ordinal)
                    ? PreflightOutcome.NotMet
                    : PreflightOutcome.Met;

            var note = state switch
            {
                PreflightOutcome.Met => null,
                PreflightOutcome.NotMet => field.Diagnostic,
                _ => explained ? null : field.Diagnostic,
            };

            explained |= state == PreflightOutcome.Unavailable;
            rows.Add(new EligibilityRow($"Act: {ModelIdNames.Display(act)} unlocked", state, note));
        }
    }

    /// <summary>
    /// The category name as a row label: <c>card_pools</c> reads <c>Card pools</c>.
    /// The names come from the game's own unlock categories, so this formats them
    /// rather than restating a list that would go stale.
    /// </summary>
    private static string CategoryLabel(string category)
    {
        var words = category.Replace('_', ' ');
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }
}

/// <summary>
/// Model ids as a player reads them: <c>ACT.UNDERDOCKS</c> is "Underdocks",
/// <c>CHARACTER.IRONCLAD</c> is "Ironclad".
///
/// Derived from the id rather than looked up in the game's localization on purpose.
/// The screen's approved wording is in one language, and a row that read
/// "Ascension 10 available on Eisenmantel" beside English sentences would be a
/// worse answer than a consistent one. It also keeps every row testable without the
/// game.
/// </summary>
public static class ModelIdNames
{
    public static string Display(string modelId)
    {
        var last = modelId.LastIndexOf('.');
        var name = last >= 0 ? modelId[(last + 1)..] : modelId;
        return string.Join(' ', name
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }
}
