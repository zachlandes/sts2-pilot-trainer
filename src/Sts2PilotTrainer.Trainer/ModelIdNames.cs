namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Model ids as a player reads them: <c>ACT.UNDERDOCKS</c> is "Underdocks",
/// <c>CHARACTER.IRONCLAD</c> is "Ironclad".
///
/// Derived from the id rather than looked up in the game's localization on purpose.
/// The screen's approved wording is in one language, and a row that read
/// "Ascension 10 available on Eisenmantel" beside English sentences would be a
/// worse answer than a consistent one.
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
