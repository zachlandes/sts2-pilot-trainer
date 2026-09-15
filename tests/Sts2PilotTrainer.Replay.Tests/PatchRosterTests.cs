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

    /// <summary>
    /// A current native recording must carry the end reading its recorder was in a
    /// position to take, while a version-7 recording is excused rather than repaired.
    /// </summary>
    [Fact]
    public void ACurrentNativeRecordingRequiresTheRunEndReadingAndAMigratedOneDoesNot()
    {
        var manifest = Fixtures.NativeManifest();
        var withoutEnd = manifest with
        {
            Environment = manifest.Environment with
            {
                Mods = manifest.Environment.Mods with
                {
                    Value = manifest.Environment.Mods.Value with
                    {
                        Patches = manifest.Environment.Mods.Value.Patches! with { AtRunEnd = null },
                    },
                },
            },
        };

        Assert.Contains(
            ManifestValidator.Validate(withoutEnd).Problems,
            problem => problem.Contains("patch_roster.run_end is absent", StringComparison.Ordinal));

        var withoutStart = manifest with
        {
            Environment = manifest.Environment with
            {
                Mods = manifest.Environment.Mods with
                {
                    Value = manifest.Environment.Mods.Value with { Patches = null },
                },
            },
        };
        Assert.Contains(
            ManifestValidator.Validate(withoutStart).Problems,
            problem => problem.Contains("patch_roster is absent", StringComparison.Ordinal));

        var migrated = withoutEnd with
        {
            Source = withoutEnd.Source with
            {
                Native = withoutEnd.Source.Native! with { MigratedFromVersion = 7 },
            },
        };
        Assert.True(ManifestValidator.Validate(migrated).IsValid);
    }

    /// <summary>The end roster has to be a captured reading at the last action,
    /// because anything earlier leaves later gameplay outside the comparison.</summary>
    [Fact]
    public void ARunEndRosterReadingBelongsAtTheLastAction()
    {
        var manifest = Fixtures.NativeManifest();
        var roster = manifest.Environment.Mods.Value.Patches!;
        var tooEarly = manifest with
        {
            Environment = manifest.Environment with
            {
                Mods = manifest.Environment.Mods with
                {
                    Value = manifest.Environment.Mods.Value with
                    {
                        Patches = roster with
                        {
                            AtRunEnd = roster.AtRunEnd! with
                            {
                                Evidence = FactEvidence.AtActionOrdinal(0, 1000),
                            },
                        },
                    },
                },
            },
        };

        Assert.Contains(
            ManifestValidator.Validate(tooEarly).Problems,
            problem => problem.Contains("patch_roster.run_end", StringComparison.Ordinal) &&
                       problem.Contains("belongs after action 1", StringComparison.Ordinal));
    }

    /// <summary>The end roster is validated by the same member rules as the start,
    /// rather than being trusted because the first reading was well formed.</summary>
    [Fact]
    public void ARunEndRosterEntryWithNoOwnerIsRefused()
    {
        var manifest = Fixtures.NativeManifest();
        var roster = manifest.Environment.Mods.Value.Patches!;
        var broken = manifest with
        {
            Environment = manifest.Environment with
            {
                Mods = manifest.Environment.Mods with
                {
                    Value = manifest.Environment.Mods.Value with
                    {
                        Patches = roster with
                        {
                            AtRunEnd = Fact<PatchRoster>.Captured(
                                new PatchRoster
                                {
                                    Members = [new PatchedMember("Type", "Member()", [], 1, 0, 0, 0)],
                                },
                                FactEvidence.AtActionOrdinal(1, 2000)),
                        },
                    },
                },
            },
        };

        Assert.Contains(
            ManifestValidator.Validate(broken).Problems,
            problem => problem.Contains("patch_roster.run_end entry", StringComparison.Ordinal) &&
                       problem.Contains("names no owner", StringComparison.Ordinal));
    }

    /// <summary>
    /// A serialized manifest's <c>patch_roster</c> carries only the two readings and
    /// their provenance. The computed comparison is an answer about them, not another
    /// fact the recorder observed.
    /// </summary>
    [Fact]
    public void ASerializedRosterCarriesOnlyTheCapturedReadings()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(
            ManifestJson.Serialize(Fixtures.NativeManifest()))!.AsObject();

        var roster = json["environment"]!["mods"]!["Value"]!["patch_roster"]!.AsObject();

        Assert.Equal(["members", "run_end"], roster.Select(property => property.Key));
        Assert.Equal(
            ["members"], roster["run_end"]!["Value"]!.AsObject().Select(property => property.Key));
    }
}
