using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The scene-tree calls an event's own work makes without asking whether a scene
/// exists, rewritten headlessly in the bodies that make them so the work runs to its
/// gameplay.
///
/// Most of the game's content reaches the presentation layer through a null-conditional
/// - <c>NRun.Instance?.GlobalUi</c> - and does nothing with no scene tree. Five events
/// on v0.111.0 do not: Dense Vegetation plays its hiss and rumbles the screen before
/// it offers the fight, Jungle Maze Adventure plays a line before it pays, Amalgamator
/// and Punch Off shake the screen as they resolve, and the Trial redraws its portrait
/// and opens the abandon popup its double-down is. Each dereferences a static
/// <c>Instance</c> that is null here, the option's task faults on the null, and the
/// game's own <c>TaskHelper.RunSafely</c> logs the fault and swallows it: the event
/// stands on the same page for ever and a recording of the option cannot replay.
///
/// The singletons cannot be stood in for at the getter. The engine reads
/// <c>NGame.Instance</c> to ask whether there is a client at all, and its own commands
/// read it with a null-conditional and go on to a member of it - <c>RelicCmd.Obtain</c>
/// reaches the run node under it as a blessing is taken - so a non-null answer fails
/// inside the engine's own work; <c>RunManager.CleanUp</c> clears the modal container
/// where there is one, and found the first version of this class that way. So the
/// calls are rewritten where they are made instead: in every body of every event
/// model that makes one of them, each call in <see cref="Table"/> becomes a call to
/// the static stand-in beside it, which takes the same arguments off the stack - the
/// instance first - and leaves the same shape behind, and does nothing. An
/// <c>Instance</c> reads as null there, so a body that reads one with a null-conditional
/// goes on as it does today, and a member reached on it that the table does not name
/// fails on the null it always failed on, which <see cref="EventOptionWork"/> then
/// refuses by name. The bodies are found by reading the IL rather than named, so an
/// event a game update adds is covered and one that stops making the calls is not
/// patched for nothing; the compiler-written state machines and closures nested in an
/// event count as its bodies, because that is where an async option's code lives.
///
/// Nothing decides anything: the sound, the shake, the portrait and the popup are
/// effects, and the gameplay either side of them is the engine's own. The same shape
/// as the rest of <see cref="HeadlessPatches"/>, at the call rather than at the callee,
/// because two of the callees cannot be compiled against the stubs at all and a
/// prefix on a body Harmony cannot compile is no patch.
/// </summary>
internal static class PresentationStandIns
{
    private const BindingFlags Every =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    /// <summary>One call rewritten: the game member, by type, name and parameter
    /// types, and the stand-in here that replaces a call to it.</summary>
    private sealed record Rewrite(Type Type, string Member, Type[] Parameters, string StandIn);

    /// <summary>The calls rewritten in event bodies. A getter is named as the compiler
    /// names it.</summary>
    private static readonly IReadOnlyList<Rewrite> Table =
    [
        new(typeof(NGame), "get_Instance", [], nameof(NoGame)),
        new(typeof(NGame), nameof(NGame.ScreenRumble), [typeof(ShakeStrength), typeof(ShakeDuration), typeof(RumbleStyle)], nameof(NoScreenRumble)),
        new(typeof(NGame), nameof(NGame.ScreenShakeTrauma), [typeof(ShakeStrength)], nameof(NoScreenShakeTrauma)),
        new(typeof(NGame), nameof(NGame.ScreenShake), [typeof(ShakeStrength), typeof(ShakeDuration), typeof(float)], nameof(NoScreenShake)),
        new(typeof(NDebugAudioManager), "get_Instance", [], nameof(NoAudio)),
        new(typeof(NDebugAudioManager), nameof(NDebugAudioManager.Play), [typeof(string), typeof(float), typeof(PitchVariance)], nameof(NoPlay)),
        new(typeof(NDebugAudioManager), nameof(NDebugAudioManager.Stop), [typeof(int), typeof(float)], nameof(NoStop)),
        new(typeof(NDebugAudioManager), nameof(NDebugAudioManager.StopAll), [], nameof(NoStopAll)),
        new(typeof(NEventRoom), "get_Instance", [], nameof(NoEventRoom)),
        new(typeof(NEventRoom), "get_Layout", [], nameof(NoLayout)),
        new(typeof(NEventRoom), "get_VfxContainer", [], nameof(NoVfxContainer)),
        new(typeof(NEventRoom), nameof(NEventRoom.SetPortrait), [typeof(Texture2D)], nameof(NoPortrait)),
        new(typeof(NEventLayout), nameof(NEventLayout.RemoveNodesOnPortrait), [], nameof(NoRemoveNodesOnPortrait)),
        new(typeof(NEventLayout), nameof(NEventLayout.AddVfxAnchoredToPortrait), [typeof(Node)], nameof(NoVfxAnchoredToPortrait)),
        new(typeof(NModalContainer), "get_Instance", [], nameof(NoModals)),
        new(typeof(NModalContainer), nameof(NModalContainer.Add), [typeof(Node), typeof(bool)], nameof(NoModal)),
        new(typeof(AssetCache), nameof(AssetCache.GetScene), [typeof(string)], nameof(EmptyScene)),
        new(typeof(AssetCache), nameof(AssetCache.GetTexture2D), [typeof(string)], nameof(NoTexture)),
    ];

    /// <summary>The rewrites resolved against this build, by the game member each
    /// replaces; filled by <see cref="Install"/>.</summary>
    private static readonly Dictionary<MethodBase, System.Reflection.MethodInfo> Resolved = [];

    internal static void Install(Harmony harmony, List<string> failures)
    {
        Resolved.Clear();
        foreach (var rewrite in Table)
        {
            var original = rewrite.Type.GetMethod(rewrite.Member, Every, rewrite.Parameters);
            if (original is null)
            {
                failures.Add(
                    $"presentation stand-in: {rewrite.Type.Name}.{rewrite.Member}({string.Join(", ", rewrite.Parameters.Select(parameter => parameter.Name))}) " +
                    "not found in this build, so an event that reaches it faults headlessly.");
                continue;
            }

            Resolved[original] = typeof(PresentationStandIns).GetMethod(rewrite.StandIn, BindingFlags.NonPublic | BindingFlags.Static)!;
        }

        var transpiler = new HarmonyMethod(typeof(PresentationStandIns).GetMethod(nameof(Rewritten), BindingFlags.NonPublic | BindingFlags.Static)!);
        foreach (var body in EventBodiesCallingTheTable())
        {
            try
            {
                harmony.Patch(body, transpiler: transpiler);
            }
            catch (Exception ex)
            {
                failures.Add($"presentation stand-in {body.DeclaringType?.FullName}.{body.Name}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Every body of every event model that calls a member of the table. A
    /// body the stubs cannot read whole is read as far as it goes, the way the caller
    /// scan reads it.</summary>
    private static IEnumerable<MethodBase> EventBodiesCallingTheTable() =>
        ChoiceEntryPoints.LoadedGameTypes()
            .Where(type => typeof(EventModel).IsAssignableFrom(type))
            .SelectMany(BodiesOf)
            .Where(body => ChoiceEntryPoints.CalleesAsFarAsReadable(body).Any(Resolved.ContainsKey));

    private static IEnumerable<MethodBase> BodiesOf(Type type) =>
        type.GetMethods(Every).Concat<MethodBase>(type.GetConstructors(Every))
            .Concat(type.GetNestedTypes(Every).SelectMany(BodiesOf));

    /// <summary>Harmony transpiler: every call to a member of the table becomes a call
    /// to its stand-in. The instruction is changed in place so the labels and blocks
    /// on it - a branch target, a try boundary - stay where they are.</summary>
    private static IEnumerable<CodeInstruction> Rewritten(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                && instruction.operand is MethodBase callee && Resolved.TryGetValue(callee, out var standIn))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = standIn;
            }

            yield return instruction;
        }
    }

    // ── The stand-ins, each taking what the call it replaces takes ─────────────

    private static NGame? NoGame() => null;

    private static void NoScreenRumble(NGame? game, ShakeStrength strength, ShakeDuration duration, RumbleStyle style)
    {
    }

    private static void NoScreenShakeTrauma(NGame? game, ShakeStrength strength)
    {
    }

    private static void NoScreenShake(NGame? game, ShakeStrength strength, ShakeDuration duration, float degAngle)
    {
    }

    private static NDebugAudioManager? NoAudio() => null;

    /// <summary>The audio manager's <c>Play</c> hands back a handle its <c>Stop</c>
    /// takes; zero is one it never issues.</summary>
    private static int NoPlay(NDebugAudioManager? audio, string streamName, float volume, PitchVariance variance) => 0;

    private static void NoStop(NDebugAudioManager? audio, int id, float fadeTime)
    {
    }

    private static void NoStopAll(NDebugAudioManager? audio)
    {
    }

    private static NEventRoom? NoEventRoom() => null;

    private static NEventLayout? NoLayout(NEventRoom? room) => null;

    private static Control? NoVfxContainer(NEventRoom? room) => null;

    private static void NoPortrait(NEventRoom? room, Texture2D portrait)
    {
    }

    private static void NoRemoveNodesOnPortrait(NEventLayout? layout)
    {
    }

    private static void NoVfxAnchoredToPortrait(NEventLayout? layout, Node? vfx)
    {
    }

    private static NModalContainer? NoModals() => null;

    private static void NoModal(NModalContainer? modals, Node modal, bool showBackstop)
    {
    }

    /// <summary>The stub scene instantiates a bare node, which is what the Trial's
    /// portrait vfx becomes here.</summary>
    private static PackedScene EmptyScene(AssetCache? cache, string path) => new();

    private static Texture2D? NoTexture(AssetCache? cache, string path) => null;
}
