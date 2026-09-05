using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The table that says which of the game's own members each recorded decision maps
/// onto, checked against the build it claims to describe.
///
/// Two things can rot it and neither is visible by reading it. The game can rename or
/// remove a member, which turns a mapping into a sentence about nothing; and this
/// repository can add a row without the driver's switch gaining a case, which the
/// driver itself reports as drift the moment such a verb is applied. The command
/// answers the first, and this runs it - through the CLI, like every other integration test here, because the
/// table lives in the project that carries the game assembly and nothing in CI may
/// reference it.
/// </summary>
public class EngineCommandTableTests
{
    [GameFact]
    public void EveryMappedMemberStillExistsOnThisBuildAndEveryVerbIsAccountedFor()
    {
        var result = Arbiter.Run("engine-commands");

        Assert.True(result.ExitCode == 0, result.All);
        Assert.Contains("every verb is accounted for", result.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every verb the format names is either mapped or listed with a reason, and the
    /// listing says which. Read off the command's own output rather than from a copy
    /// of the alphabet kept here, so a verb added to the format and forgotten
    /// everywhere else fails this.
    /// </summary>
    [GameFact]
    public void EveryVerbInTheFormatIsNamedInTheListing()
    {
        var result = Arbiter.Run("engine-commands");

        foreach (var verb in Enum.GetValues<ActionVerb>())
        {
            Assert.Contains(verb.ToString(), result.Output, StringComparison.Ordinal);
        }
    }
}
