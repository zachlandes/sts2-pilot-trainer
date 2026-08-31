using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The initialisation sequence, in the order the game needs it, with the reason for
/// each step. Order here is not stylistic: several of these steps fail silently and
/// leave a null behind that only surfaces three subsystems later.
/// </summary>
internal static class EngineInitialization
{
    internal static EngineStartupReport InitializeOnce()
    {
        var warnings = new List<string>();

        // Confine every write the engine makes to a directory this project owns.
        // The player's install and saves are read-only inputs, and the engine has
        // no idea that is the arrangement.
        var libDir = AssemblyResolution.ResolveLibDirectory();
        if (libDir is not null)
        {
            Godot.HeadlessSandbox.SetRoot(Path.Combine(Path.GetDirectoryName(libDir)!, "sandbox"));
        }

        // Platform services first: several gameplay paths reach for the platform
        // layer, and touching it early turns a mid-run null reference into a warning
        // here where it can be reported.
        Try(warnings, "platform", () => _ = MegaCrit.Sts2.Core.Platform.PlatformUtil.PrimaryPlatform);

        // A profile id and preferences must exist before any run is created. The
        // preferences object in particular is read from gameplay code, so a null one
        // is a crash rather than a default.
        Try(warnings, "profile", () => SaveManager.Instance.InitProfileId(0));
        Try(warnings, "prefs", () => SaveManager.Instance.InitPrefsDataForTest());
        Try(warnings, "progress", () => SaveManager.Instance.InitProgressData());

        // Tell the engine it is running headless.
        //
        // This is the switch the game's own automated tests use, and it is the only
        // supported way to make the room, card, creature and banner constructors
        // return null instead of reaching for a Godot scene that does not exist.
        //
        // It is not free of gameplay reach: a handful of gameplay classes consult it,
        // mostly to skip animation waits, and RunManager.ShouldApplyTutorialModifications
        // consults it too. Tutorial modifications only ever apply to a player's very
        // first run, so switching them off matches what an experienced player's run
        // does anyway - but that is an argument, not a measurement. The measurement is
        // the map check: Act 1's topology is generated through the code this flag
        // touches, and it comes out identical with the flag on and off, and identical
        // to the map the source video shows. See docs/headless-fidelity.md.
        SetTestMode(true);

        // Declare the mod loader finished with nothing loaded.
        //
        // This host loads no mods at all, by design. The engine gates its reflection
        // over mod assemblies behind the loader having run, so without this the first
        // run construction throws - and more importantly, a half-initialised loader
        // would leave it ambiguous whether content came from a mod.
        //
        // Loading nothing is what makes the content hash meaningful: it is the base
        // game's hash, and if it equals the hash observed in a source video then the
        // content that video was played against matches this environment, whatever
        // mods were installed there.
        // The setter is internal to the game, so the backing field is set directly.
        typeof(ModManager)
            .GetField("<State>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, ModManagerState.Skipped);

        // Asset preloading pulls textures, audio and animations out of the resource
        // pack. There is no renderer here to want them, and the engine exposes this
        // as a supported switch - which is preferable to patching the loader.
        Try(warnings, "preload-off", () => MegaCrit.Sts2.Core.Assets.PreloadManager.Enabled = false);

        HeadlessPatches.Apply(warnings);
        Localization.Initialize(warnings);
        ReleaseInfoBinding.Install(warnings);

        var subtypes = AbstractModelSubtypes.All;
        int registered = 0, failed = 0;
        for (var i = 0; i < subtypes.Count; i++)
        {
            try
            {
                ModelDb.Inject(subtypes[i]);
                registered++;
            }
            catch (Exception ex)
            {
                failed++;
                if (failed <= 5) warnings.Add($"model {subtypes[i].Name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // The id-serialization cache sorts content by owning assembly, which means
        // the mod/base-game map has to exist first. Without this the cache refuses to
        // initialise and the content hash comes back as a perfectly stable zero.
        Try(warnings, "assembly-info", MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init);

        // Combat actions serialize model ids by index, and the index table is built
        // once from the registered database. Without it, the first card play throws.
        // It also computes the content hash this project gates on.
        Try(warnings, "model-id-cache", ModelIdSerializationCache.Init);

        return new EngineStartupReport(registered, failed, warnings);
    }

    /// <summary>
    /// The game's content hash, computed the way the game computes it.
    ///
    /// Found by reflection over the multiplayer version-info type rather than
    /// reimplemented, because a hash we compute ourselves would answer a question
    /// nobody asked: the value that matters is the one the game puts on screen and
    /// compares between peers, and the only way to be sure we have that value is to
    /// let the game produce it.
    /// </summary>
    internal static string ContentHash()
    {
        var versionInfoType = typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Multiplayer.PeerVersionInfo")
            ?? throw new EngineException(
                "PeerVersionInfo is absent from this build. The content-hash gate cannot be evaluated, and " +
                "without it there is no mod-parity check - refusing rather than replaying blind.");

        var localDefault = versionInfoType.GetMethod("LocalDefault", BindingFlags.Public | BindingFlags.Static)
            ?? throw new EngineException("PeerVersionInfo.LocalDefault is absent from this build.");

        var info = localDefault.Invoke(null, null)
                   ?? throw new EngineException("PeerVersionInfo.LocalDefault returned null.");

        var hashField = versionInfoType.GetField("idDatabaseHash", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new EngineException("PeerVersionInfo.idDatabaseHash is absent from this build.");

        var value = hashField.GetValue(info)
                    ?? throw new EngineException("PeerVersionInfo.idDatabaseHash was null.");

        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!;
    }

    /// <summary>
    /// Sets the engine's headless/test flag. The setter is internal to the game, and
    /// its own documentation says never to call the initialiser, so the backing field
    /// is set directly rather than going through a path the game reserves for its
    /// test runners.
    /// </summary>
    internal static void SetTestMode(bool on)
    {
        var field = typeof(MegaCrit.Sts2.Core.TestSupport.TestMode)
            .GetField("<IsOn>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new EngineException("TestMode.IsOn is absent from this build.");
        field.SetValue(null, on);
    }

    private static void Try(List<string> warnings, string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            warnings.Add($"{what}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

/// <summary>
/// Runtime patches that stand in for subsystems the headless host does not have.
///
/// Each one replaces a presentation-layer call that blocks forever or throws with
/// no scene tree. None of them touch a gameplay decision, and each says why. They
/// are applied with Harmony, which the game itself ships and loads - so this is the
/// same mechanism any Workshop mod uses, not an unsupported hook.
/// </summary>
internal static class HeadlessPatches
{
    private const string HarmonyId = "sts2-pilot-trainer.headless";

    internal static void Apply(List<string> warnings)
    {
        var harmony = new Harmony(HarmonyId);
        var assembly = typeof(ModelDb).Assembly;

        // Cmd.Wait(float) sleeps for an animation. With no frame loop the wait never
        // completes and the action executor stalls, so it returns immediately. It
        // gates presentation timing only; the game documents its animation-timing
        // randomness as explicitly non-gameplay.
        Neutralize(harmony, assembly, "MegaCrit.Sts2.Core.Commands.Cmd", "Wait", warnings);

        // TalkCmd.Play shows a monster speech bubble, which needs a scene node.
        Neutralize(harmony, assembly, "MegaCrit.Sts2.Core.Commands.TalkCmd", "Play", warnings);

        // Asset preloading. PreloadManager.Enabled is off, but the loaders still
        // assemble their asset-path lists before consulting it, and those lists
        // dereference texture properties the stubs leave null. Each entry point is
        // named rather than matched by prefix, so adding a loader in a future build
        // shows up as a warning here instead of as a silent behaviour change.
        foreach (var loader in new[]
                 {
                     "LoadRunAssets", "LoadActAssets", "LoadRoomEventAssets", "LoadRoomCombatAssets",
                     "LoadRoomTreasureAssets", "LoadRoomMerchantAssets", "LoadRoomRestSite", "LoadRoomAssets",
                 })
        {
            Neutralize(harmony, assembly, "MegaCrit.Sts2.Core.Assets.PreloadManager", loader, warnings);
        }

        // Saving. The run is created with shouldSave:false, but the engine still
        // reaches for the save subsystem on room entry to persist progress. This host
        // must not write a save at all: the player's save directory is a read-only
        // input, and a headless run is not a run they played.
        foreach (var saver in new[] { "SaveRun", "SaveProgressFile", "SavePrefsFile", "SaveProfileFile" })
        {
            Neutralize(harmony, assembly, "MegaCrit.Sts2.Core.Saves.SaveManager", saver, warnings);
        }

        // Screen fades between rooms and acts. Pure vfx, and they dereference a
        // scene tree that does not exist here.
        Neutralize(harmony, assembly, "MegaCrit.Sts2.Core.Runs.RunManager", "FadeOut", warnings);
        Neutralize(harmony, assembly, "MegaCrit.Sts2.Core.Runs.RunManager", "FadeIn", warnings);
        Neutralize(harmony, assembly, "MegaCrit.Sts2.Core.Runs.RunManager", "ClearScreens", warnings);
        Neutralize(harmony, assembly, "MegaCrit.Sts2.Core.Runs.RunManager", "UpdateRichPresence", warnings);
    }

    /// <summary>
    /// Replaces a presentation-layer method with a no-op, keeping its return shape.
    /// Applies to every overload of the name, and records a warning if the name is
    /// gone - a patch that silently stops matching is how a headless host quietly
    /// starts behaving differently from the one that was validated.
    /// </summary>
    private static void Neutralize(
        Harmony harmony, Assembly assembly, string typeName, string methodName, List<string> warnings)
    {
        var type = assembly.GetType(typeName);
        if (type is null)
        {
            warnings.Add($"headless patch: type {typeName} not found in this build");
            return;
        }

        var methods = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == methodName)
            .ToArray();

        if (methods.Length == 0)
        {
            warnings.Add($"headless patch: {typeName}.{methodName} not found in this build");
            return;
        }

        foreach (var method in methods)
        {
            // Harmony rejects a __result parameter on a void method, so the prefix
            // is chosen by return shape rather than one prefix covering both.
            var prefixName = method.ReturnType == typeof(void) ? nameof(SkipVoid) : nameof(SkipReturningCompletedTask);
            var prefix = typeof(HeadlessPatches).GetMethod(prefixName, BindingFlags.NonPublic | BindingFlags.Static)!;
            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
            }
            catch (Exception ex)
            {
                warnings.Add($"headless patch {typeName}.{methodName}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Harmony prefix: skip the original and hand back a finished task.</summary>
    private static bool SkipReturningCompletedTask(ref object? __result)
    {
        __result = Task.CompletedTask;
        return false;
    }

    /// <summary>Harmony prefix for void methods: skip the original entirely.</summary>
    private static bool SkipVoid() => false;
}

public sealed class EngineException(string message) : Exception(message);
