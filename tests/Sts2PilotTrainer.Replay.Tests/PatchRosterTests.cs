using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The roster's own reading of itself, on a machine with no game.
///
/// The line it writes into the game's log is the whole of step one of this feature -
/// what a player attaches to a bug report - so it is asserted here rather than only
/// looked at once in a client.
/// </summary>
public sealed class PatchRosterTests
{
    [Fact]
    public void OneLineNamesTheMemberItsOwnersAndEachKindOfPatch()
    {
        var member = new PatchedMember(
            "MegaCrit.Sts2.Core.Combat.CombatState",
            "DrawCards(Int32)",
            [PatchRoster.HostOwnerId, "somebody.else"],
            Prefixes: 2,
            Postfixes: 1,
            Transpilers: 0,
            Finalizers: 3);

        Assert.Equal(
            "MegaCrit.Sts2.Core.Combat.CombatState.DrawCards(Int32) patched by " +
            "sts2-pilot-trainer.runmobile, somebody.else (2 prefix, 1 postfix, 0 transpiler, 3 finalizer)",
            member.Describe());
    }

    /// <summary>
    /// A member the host shares with somebody else is a member somebody else patched.
    ///
    /// The stricter half of the rule, and the one an "outside our own set" reading
    /// would miss: two prefixes on the same method are the case where the order they
    /// run in decides the answer, so our owning it too makes it worse rather than
    /// permitted.
    /// </summary>
    [Fact]
    public void AMemberTheHostAlsoPatchedStillCountsAsPatchedBySomebodyElse()
    {
        var roster = new PatchRoster
        {
            Members =
            [
                new PatchedMember("Type", "Ours()", [PatchRoster.HostOwnerId], 1, 0, 0, 0),
                new PatchedMember("Type", "Shared()", [PatchRoster.HostOwnerId, "them"], 2, 0, 0, 0),
            ],
        };

        Assert.True(roster.NamesTheHost);
        var shared = Assert.Single(roster.PatchedByAnybodyElse);
        Assert.Equal("Shared()", shared.Member);
        Assert.Equal(["them"], shared.ForeignOwners);
    }

    /// <summary>
    /// The validator refuses a roster entry that names no owner, because who patched
    /// a member is the whole reading.
    /// </summary>
    [Fact]
    public void ARosterEntryWithNoOwnerIsRefused()
    {
        var manifest = Fixtures.NativeManifest();
        var broken = manifest with
        {
            Environment = manifest.Environment with
            {
                Mods = Fact<ModEnvironment>.Captured(
                    manifest.Environment.Mods.Value with
                    {
                        Patches = new PatchRoster
                        {
                            Members = [new PatchedMember("Type", "Member()", [], 1, 0, 0, 0)],
                        },
                    },
                    manifest.Environment.Mods.Evidence!),
            },
        };

        Assert.Contains(
            ManifestValidator.Validate(broken).Problems,
            problem => problem.Contains("names no owner", StringComparison.Ordinal));
    }
}
