using System.Text;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Lays the screen's content out as the game's rich-text markup.
///
/// Colour is the whole visual language here, and that is deliberate: a row's state
/// is drawn, never written. Adding a tick, a cross or the word "missing" would put a
/// string on screen that nobody approved, and a row already says what it needs to.
/// </summary>
internal static class ScreenMarkup
{
    /// <summary>Requirement met. The game's own affirmative green.</summary>
    private const string MetColor = "#8fc972";

    /// <summary>Requirement not met. Warm rather than alarming: a missing unlock is
    /// something to go and play, not an error.</summary>
    private const string NotMetColor = "#e0755a";

    /// <summary>Supporting text - the subtitle, the engine's sentences, the profile
    /// note. Dimmer than a row so the rows read first.</summary>
    private const string SupportingColor = "#b6a892";

    internal static string Body(EligibilityScreen screen)
    {
        var body = new StringBuilder();

        Line(body, Dim(screen.Subtitle));
        if (screen.RecordingLine.Length > 0) Line(body, Dim(screen.RecordingLine));
        Blank(body);
        Line(body, screen.Headline);

        if (screen.Rows.Count > 0) Blank(body);
        foreach (var row in screen.Rows)
        {
            Line(body, Colored(row.Met ? MetColor : NotMetColor, row.Label));
            // A note under a green row is scope, under a red one it is what to do.
            // Both are the engine's own sentence and neither is rewritten here.
            if (row.Note is { Length: > 0 } note) Line(body, Dim(note));
        }

        foreach (var refusal in screen.Refusals)
        {
            Blank(body);
            Line(body, Colored(NotMetColor, refusal));
        }

        Blank(body);
        Line(body, Dim(screen.ProfileNote));

        return body.ToString();
    }

    private static void Line(StringBuilder body, string text) => body.Append(text).Append('\n');

    private static void Blank(StringBuilder body) => body.Append('\n');

    private static string Dim(string text) => Colored(SupportingColor, text);

    private static string Colored(string color, string text) => $"[color={color}]{Escape(text)}[/color]";

    /// <summary>
    /// Keeps the engine's sentences out of the markup parser.
    ///
    /// Nothing in a diagnostic is meant as markup, and a stray bracket in a model id
    /// would otherwise swallow the rest of the sentence.
    /// </summary>
    private static string Escape(string text) => text.Replace("[", "[lb]", StringComparison.Ordinal);
}
