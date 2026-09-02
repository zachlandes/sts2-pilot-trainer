using System.Globalization;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Whether one row's requirement is met. Two states, because a row that a player
/// can act on has to say yes or no; anything a gate could not measure is not a row,
/// it is a refusal.
/// </summary>
public enum RequirementState
{
    Met,
    NotMet,
}

/// <summary>
/// One line of the eligibility screen: what is required, whether this game has it,
/// and - when it does not - the engine's own sentence about what to do, verbatim.
/// </summary>
public sealed record EligibilityRow(string Label, RequirementState State, string? Note = null)
{
    public bool Met => State == RequirementState.Met;
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
    /// A separate question from <see cref="Eligible"/>, and the difference is the
    /// point. The rows above report the player's own profile, which is what a player
    /// starting this run by hand would need. Nobody starts it by hand: the trainer
    /// constructs it, and the unlock state it is generated against is the complete
    /// one the recording requires, supplied for that run and written nowhere. So the
    /// offer is governed by whether this game can construct the recording's run,
    /// which <c>Preflight</c> answers under exactly the same rules against exactly
    /// that progress model - see EnvironmentPreflight.EvaluateAscensionCeiling, which
    /// already says a host constructing a run directly never consults the profile
    /// ceiling.
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

    /// <summary>The one unlock field that is about the manifest's request rather than
    /// about a category of content, so it has no "n of m" row to live in.</summary>
    private const string UnlockRequirementField = "unlocks_requirement";

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
            if (field.Field == UnlockRequirementField) continue;
            claimed.Add(field.Field);
            var category = CategoryLabel(field.Field[UnlockCategoryPrefix.Length..]);
            rows.Add(new EligibilityRow(
                $"{category}: {field.Actual} of {field.Expected}",
                field.Matches ? RequirementState.Met : RequirementState.NotMet,
                field.Diagnostic));
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

        // Unmet rows first, in the order the gates asked them; met rows keep their
        // order behind. The screen is read by somebody who wants to know what to go
        // and play, and the thing they have to act on should not be below whatever
        // already passed. Nothing is hidden and no row changes: a row that carries a
        // qualifier still carries it, wherever it lands.
        List<EligibilityRow> ordered = [.. rows.Where(row => !row.Met), .. rows.Where(row => row.Met)];

        return new EligibilityScreen(
            Title: TrainerCopy.Name,
            Subtitle: RecordingIdentity.Subtitle(recording),
            RecordingLine: TrainerCopy.RecordingLine(expected.BuildVersion.Value, expected.BuildDateUtc.Value),
            Headline: preflight.Matches ? TrainerCopy.PassHeadline : TrainerCopy.FailHeadline,
            Eligible: preflight.Matches,
            Rows: ordered,
            Refusals: refusals,
            ProfileNote: TrainerCopy.ProfileNote,
            BackButton: TrainerCopy.BackButton)
        {
            FightOffered = fightOffered,
        };
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
            field.Matches ? RequirementState.Met : RequirementState.NotMet,
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
        foreach (var act in expected.Acts.Value)
        {
            var locked = reading.LockedActs.Contains(act, StringComparer.Ordinal);
            rows.Add(new EligibilityRow(
                $"Act: {ModelIdNames.Display(act)} unlocked",
                locked ? RequirementState.NotMet : RequirementState.Met,
                locked ? field.Diagnostic : null));
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
