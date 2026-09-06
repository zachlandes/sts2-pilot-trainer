using System.Runtime.CompilerServices;
using HarmonyLib;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What the roster says, against what is actually patched in this process.
///
/// Asserted by patching something and looking, rather than against a copy of what the
/// reading is expected to produce. A roster assembled from what a component believes
/// it patched would agree with itself however the patch went, which is the failure it
/// exists to catch: a patch whose target a build renamed resolves to nothing and
/// applies silently.
///
/// Every assertion here is about containment. <c>Harmony.GetAllPatchedMethods</c>
/// reads one registry for the whole process, so another test's patches are in the
/// answer too and an equality assertion would fail on somebody else's work.
/// </summary>
public sealed class HarmonyRosterTests
{
    /// <summary>
    /// A member this test patches appears on the roster, named, owned and counted -
    /// and is gone from it once the patch comes off.
    ///
    /// Both halves, because a roster that named everything forever would pass the
    /// first assertion just as well while saying nothing about what is patched now.
    /// </summary>
    [Fact]
    public void TheRosterNamesWhatIsPatchedRightNowAndNothingAfterItComesOff()
    {
        var owner = $"sts2-pilot-trainer.roster-test.{Guid.NewGuid():N}";
        var harmony = new Harmony(owner);
        var target = typeof(HarmonyRosterTests).GetMethod(
            nameof(PatchedForTheRoster),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        try
        {
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(HarmonyRosterTests), nameof(Prefix)));

            var member = Assert.Single(
                HarmonyRoster.Read().Members, entry => entry.Owners.Contains(owner));

            Assert.Equal(typeof(HarmonyRosterTests).FullName, member.DeclaringType);
            Assert.Equal($"{nameof(PatchedForTheRoster)}(Int32)", member.Member);
            Assert.Equal(1, member.Prefixes);
            Assert.Equal(0, member.Postfixes);
            Assert.Equal(0, member.Transpilers);
            Assert.Equal(0, member.Finalizers);
        }
        finally
        {
            harmony.UnpatchAll(owner);
        }

        Assert.DoesNotContain(HarmonyRoster.Read().Members, entry => entry.Owners.Contains(owner));
    }

    /// <summary>
    /// The roster a recorder captures into a recording is that same reading, not a
    /// second one assembled beside it.
    ///
    /// <c>ModEnvironment.AsRecorded</c> is where the mod list and the roster become one
    /// captured fact, and the preflight rule reads the roster off it by owner id. A
    /// capture path that dropped or rebuilt the roster would leave every recording
    /// judged by the declaration rule alone with nothing saying so.
    /// </summary>
    [Fact]
    public void WhatIsCapturedIntoARecordingIsTheRosterThatWasRead()
    {
        var owner = $"sts2-pilot-trainer.roster-capture-test.{Guid.NewGuid():N}";
        var harmony = new Harmony(owner);
        var target = typeof(HarmonyRosterTests).GetMethod(
            nameof(AlsoPatchedForTheRoster),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        try
        {
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(HarmonyRosterTests), nameof(Prefix)));

            var captured = ModEnvironment.AsRecorded(
                [new LocalMod("Runmobile", "Runmobile", "0.1.0", AffectsGameplay: false, "Loaded")],
                HarmonyRoster.Read());

            var roster = Assert.IsType<PatchRoster>(captured.Patches);
            Assert.Contains(roster.Members, entry => entry.Owners.Contains(owner));

            // And the rule sees it as somebody else's patch, because it is one.
            Assert.Contains(roster.PatchedByAnybodyElse, entry => entry.Owners.Contains(owner));
        }
        finally
        {
            harmony.UnpatchAll(owner);
        }
    }

    /// <summary>
    /// The shell's own patches land under the id the preflight rule looks for.
    ///
    /// The rule refuses a recording whose roster names no member owned by
    /// <see cref="PatchRoster.HostOwnerId"/>, on the grounds that a started shell has
    /// definitely patched something. This is what makes that true rather than assumed:
    /// the mod builds its Harmony instance from that same const, so a rename that broke
    /// the pairing would fail here rather than turning every future recording into a
    /// broken reading.
    /// </summary>
    [GameFact]
    public void TheShellsOwnPatchesAreOwnedByTheIdThePreflightLooksFor()
    {
        _ = EngineHost.StartupPhase();
        var harmony = new Harmony(PatchRoster.HostOwnerId);

        var patched = new List<System.Reflection.MethodBase>();

        try
        {
            patched.AddRange(RunmobileMod.InstallShellPatches(harmony));

            var roster = HarmonyRoster.Read();

            Assert.NotEmpty(patched);
            Assert.All(patched, method => Assert.Contains(
                roster.Members,
                entry => entry.DeclaringType == method.DeclaringType?.FullName
                    && entry.Member.StartsWith($"{method.Name}(", StringComparison.Ordinal)
                    && entry.Owners.Contains(PatchRoster.HostOwnerId)));
        }
        finally
        {
            // Everything this project installs shares that id, so unpatching by id
            // would take the yield-suppression patch off with it - and its one-shot
            // latch means nothing would put it back.
            foreach (var method in patched)
            {
                harmony.Unpatch(method, HarmonyPatchType.All, PatchRoster.HostOwnerId);
            }
        }
    }

    /// <summary>
    /// The yield-suppression patch lands under that same id.
    ///
    /// It is installed on a framework member, from the engine rather than the mod
    /// shell, and it is never unpatched - so once a watched journey has driven a fight
    /// it is on every roster the recorder reads for the rest of the session. Under an
    /// id of its own the preflight read it as somebody else's patch and refused a
    /// clean recording, naming a mod that does not exist.
    /// </summary>
    [Fact]
    public void TheYieldSuppressionPatchIsOwnedByTheIdThePreflightLooksFor()
    {
        // Idempotent on purpose: the patch is installed once per process and stays,
        // so this holds whether or not something else already enabled suppression.
        YieldSuppression.Enable().Dispose();

        Assert.Contains(
            HarmonyRoster.Read().Members,
            entry => entry.DeclaringType == "System.Runtime.CompilerServices.YieldAwaitable+YieldAwaiter"
                && entry.Member == "get_IsCompleted()"
                && entry.Owners.Contains(PatchRoster.HostOwnerId));
    }

    /// <summary>Something to patch. Never inlined, because a method the JIT folded
    /// away is one Harmony cannot attach to.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int PatchedForTheRoster(int value) => value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int AlsoPatchedForTheRoster(int value) => value;

    private static void Prefix()
    {
    }
}
