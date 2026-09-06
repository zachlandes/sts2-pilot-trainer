using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves;
using Sts2PilotTrainer.IO;

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
        var failures = new List<string>();
        if (Environment.GetEnvironmentVariable("STS2_PILOT_TRAINER_TEST_REQUIRED_INIT_FAILURE") is { Length: > 0 } forced)
        {
            failures.Add($"forced required-step failure: {forced}");
        }

        // Confine every write the engine makes to a directory this project owns.
        // The player's install and saves are read-only inputs, and the engine has
        // no idea that is the arrangement.
        var worktreeRoot = WorktreeLocator.Find();
        var libDir = AssemblyResolution.ResolveLibDirectory();
        var sandboxRoot = libDir is null
            ? Path.Combine(worktreeRoot, "build", "sandbox")
            : Path.Combine(Path.GetDirectoryName(libDir)!, "sandbox");
        Godot.HeadlessSandbox.SetRoot(WorktreePath.Require(sandboxRoot));

        // Platform services first: several gameplay paths reach for the platform
        // layer, and touching it early turns a mid-run null reference into a warning
        // here where it can be reported.
        TryOptional(warnings, "platform", () => _ = MegaCrit.Sts2.Core.Platform.PlatformUtil.PrimaryPlatform);

        // A profile id and preferences must exist before any run is created. The
        // preferences object in particular is read from gameplay code, so a null one
        // is a crash rather than a default.
        TryRequired(failures, "profile", () => SaveManager.Instance.InitProfileId(0));
        TryRequired(failures, "prefs", () => SaveManager.Instance.InitPrefsDataForTest());

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
        var modManagerState = typeof(ModManager)
            .GetField("<State>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        if (modManagerState is null)
        {
            failures.Add("mod loader: ModManager.State backing field is absent");
        }
        else
        {
            modManagerState.SetValue(null, ModManagerState.Skipped);
        }

        // Asset preloading pulls textures, audio and animations out of the resource
        // pack. There is no renderer here to want them, and the engine exposes this
        // as a supported switch - which is preferable to patching the loader.
        TryRequired(failures, "preload-off", () => MegaCrit.Sts2.Core.Assets.PreloadManager.Enabled = false);

        HeadlessPatches.Apply(failures);
        Localization.Initialize(failures);
        ReleaseInfoBinding.Install(failures);

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
                if (failed <= 5) failures.Add($"model {subtypes[i].Name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        TryRequired(failures, "progress", () => SaveManager.Instance.InitProgressData());

        // The id-serialization cache sorts content by owning assembly, which means
        // the mod/base-game map has to exist first. Without this the cache refuses to
        // initialise and the content hash comes back as a perfectly stable zero.
        TryRequired(failures, "assembly-info", MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init);

        // Combat actions serialize model ids by index, and the index table is built
        // once from the registered database. Without it, the first card play throws.
        // It also computes the content hash this project gates on.
        TryRequired(failures, "model-id-cache", ModelIdSerializationCache.Init);
        if (registered == 0) failures.Add("model injection registered zero models");
        if (failed > 5) failures.Add($"model injection: {failed - 5} additional model(s) failed");

        return new EngineStartupReport(registered, failed, warnings, failures);
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
    internal static void SetTestMode(bool on) => TestModeField.SetValue(null, on);

    /// <summary>
    /// Resolved once: <see cref="HeadlessPatches"/> flips this field around individual
    /// engine calls, so it is on a path taken several times per shop rather than once
    /// per process.
    /// </summary>
    private static readonly FieldInfo TestModeField =
        typeof(MegaCrit.Sts2.Core.TestSupport.TestMode)
            .GetField("<IsOn>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new EngineException("TestMode.IsOn is absent from this build.");

    private static void TryRequired(List<string> failures, string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            failures.Add($"{what}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryOptional(List<string> warnings, string what, Action action)
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
/// Most of them replace a presentation-layer call that blocks forever or throws with
/// no scene tree. A second, smaller set does the opposite: it puts back a gameplay
/// decision the engine's own test-mode flag takes away. None of them invent a
/// decision, and each says why. They are applied with Harmony, which the game itself
/// ships and loads - so this is the same mechanism any Workshop mod uses, not an
/// unsupported hook.
/// </summary>
internal static class HeadlessPatches
{
    private const string HarmonyId = "sts2-pilot-trainer.headless";

    internal static void Apply(List<string> warnings)
    {
        var harmony = new Harmony(HarmonyId);
        var assembly = typeof(ModelDb).Assembly;

        RestoreRetailBranches(harmony, assembly, warnings);

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
        foreach (var saver in new[] { "SaveProgressFile", "SavePrefsFile", "SaveProfile" })
        {
            Neutralize(harmony, assembly, "MegaCrit.Sts2.Core.Saves.SaveManager", saver, warnings);
        }

        // SaveRun is the same refusal with one addition: with a collector installed it
        // hands the game's own ToSave(preFinishedRoom) over instead of dropping it, which
        // is the only way to obtain the save the retail client takes at a floor arrival.
        // With none installed it calls nothing and writes nothing, exactly as the
        // neutralize above does. See RunSaveInterception.
        InterceptSaveRun(harmony, assembly, warnings);

        // Screen fades between rooms and acts. Pure vfx, and they dereference a
        // scene tree that does not exist here.
        Neutralize(harmony, assembly, "MegaCrit.Sts2.Core.Runs.RunManager", "FadeOut", warnings);
        Neutralize(harmony, assembly, "MegaCrit.Sts2.Core.Runs.RunManager", "FadeIn", warnings);
        Neutralize(harmony, assembly, "MegaCrit.Sts2.Core.Runs.RunManager", "ClearScreens", warnings);
        Neutralize(harmony, assembly, "MegaCrit.Sts2.Core.Runs.RunManager", "UpdateRichPresence", warnings);
    }

    /// <summary>
    /// The gameplay paths that consume randomness only when the engine's test-mode
    /// flag is off, run here the way retail runs them.
    ///
    /// This is a different kind of patch from every other one in this class and the
    /// difference is the point. The rest stand in for a subsystem that is absent. These
    /// exist because <c>TestMode.IsOn</c> is not only a presentation switch: at three
    /// sites it changes what the game *generates*, and each one leaves a run-persistent
    /// random stream in a different place than the same run leaves it in the player's
    /// own client. A replay that diverges from the recording it is checking is the one
    /// failure this project cannot tolerate, and it is silent - the streams' positions
    /// are inside the canonical state, so the divergence surfaces as a boundary digest
    /// that does not match, downstream of the room that caused it.
    ///
    /// The remedy is to turn the flag off for the duration of the call and let the
    /// engine's own retail branch run. Nothing here reimplements a cost, a roll or a
    /// pool: the game computes what it would have computed. Each of the three bodies
    /// is synchronous and touches no scene tree on its retail path, which is what makes
    /// the flip safe to scope this way - see docs/headless-fidelity.md.
    ///
    /// A name that stops matching is a failure rather than a warning. A silently
    /// unpatched site here is a headless host that reproduces nothing and says so
    /// nowhere.
    /// </summary>
    private static void RestoreRetailBranches(Harmony harmony, Assembly assembly, List<string> failures)
    {
        // MerchantPotionEntry.CalcCost multiplies the potion's shelf price by
        // PlayerRng.Shops.NextFloat(0.95, 1.05), and only when test mode is off. A
        // normal merchant builds exactly three potion entries, so every shop a headless
        // replay walks into leaves player.rng.Shops three draws behind the recording's,
        // permanently, for the rest of the run. Measured on two native recordings: the
        // recorded digest at every boundary from the shop's own floor entry onward is
        // reproduced by Shops+3 and by nothing else.
        //
        // The card and relic entries take the same draw with no guard, which is why
        // only the potion slots drift.
        InRetailMode(harmony, assembly, "MegaCrit.Sts2.Core.Entities.Merchant.MerchantPotionEntry", "CalcCost", failures);

        // Cauldron hands the player its potions from a hard-coded array when test mode
        // is on, and constructs unpopulated PotionRewards when it is off. An unpopulated
        // reward draws from PlayerRng.Rewards when it is populated; a pre-filled one
        // never does. Same defect, different stream.
        InRetailMode(harmony, assembly, "MegaCrit.Sts2.Core.Models.Relics.Cauldron", "GenerateRewards", failures);

        // Calling Bell is the same shape and costs more than a stream position: its
        // retail branch pulls three relics from the run's own relic grab bag by rarity,
        // and the test branch hands back Anchor, Gremlin Horn and Mummified Hand without
        // touching it. Left alone, a headless replay of a run that picked this relic up
        // gets different relics *and* a differently-positioned bag.
        InRetailMode(harmony, assembly, "MegaCrit.Sts2.Core.Models.Relics.CallingBell", "GenerateRewards", failures);
    }

    /// <summary>
    /// Runs one method with the engine's test-mode flag off and puts it back
    /// afterwards, including when the method throws.
    ///
    /// The depth counter is not for concurrency - this host is single-threaded - it is
    /// for a patched method that reaches another patched method, so the flag is only
    /// restored by the outermost call.
    /// </summary>
    private static void InRetailMode(
        Harmony harmony, Assembly assembly, string typeName, string methodName, List<string> failures)
    {
        var type = assembly.GetType(typeName);
        if (type is null)
        {
            failures.Add($"retail-branch patch: type {typeName} not found in this build");
            return;
        }

        var methods = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == methodName)
            .ToArray();

        if (methods.Length == 0)
        {
            failures.Add($"retail-branch patch: {typeName}.{methodName} not found in this build");
            return;
        }

        var prefix = typeof(HeadlessPatches).GetMethod(nameof(EnterRetailMode), BindingFlags.NonPublic | BindingFlags.Static)!;
        var finalizer = typeof(HeadlessPatches).GetMethod(nameof(LeaveRetailMode), BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var method in methods)
        {
            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(finalizer));
            }
            catch (Exception ex)
            {
                failures.Add($"retail-branch patch {typeName}.{methodName}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static int _retailModeDepth;

    /// <summary>Harmony prefix: take the retail branch through this call.</summary>
    private static void EnterRetailMode()
    {
        if (_retailModeDepth++ == 0) EngineInitialization.SetTestMode(false);
    }

    /// <summary>Harmony finalizer: restore the headless flag, thrown or returned.</summary>
    private static void LeaveRetailMode()
    {
        if (--_retailModeDepth == 0) EngineInitialization.SetTestMode(true);
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

    /// <summary>
    /// Patches <c>SaveManager.SaveRun</c> so nothing is ever written and, when
    /// something is collecting, the save the game was about to write is offered to it.
    ///
    /// A missing name here is a failure rather than a warning, unlike the neutralize
    /// beside it: a host that silently stopped intercepting this method would write no
    /// save and collect none either, and the only symptom would be a snapshot cache
    /// that never materialises anything, for a reason nothing says out loud.
    /// </summary>
    private static void InterceptSaveRun(Harmony harmony, Assembly assembly, List<string> failures)
    {
        var type = assembly.GetType("MegaCrit.Sts2.Core.Saves.SaveManager");
        var methods = type?
            .GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "SaveRun")
            .ToArray() ?? [];

        if (methods.Length == 0)
        {
            failures.Add(
                "headless patch: MegaCrit.Sts2.Core.Saves.SaveManager.SaveRun not found in this build. This " +
                "host neither writes a save nor collects one without it, and both silences look like success.");
            return;
        }

        var prefix = typeof(HeadlessPatches).GetMethod(
            nameof(CollectAndSkipSaveRun), BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var method in methods)
        {
            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
            }
            catch (Exception ex)
            {
                failures.Add($"headless patch SaveManager.SaveRun: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Harmony prefix: offer the save to whatever is collecting, then skip the
    /// original and hand back a finished task.</summary>
    private static bool CollectAndSkipSaveRun(object[] __args, ref object? __result)
    {
        if (RunSaveInterception.Armed)
        {
            RunSaveInterception.Offer(__args.Length > 0 ? __args[0] : null);
        }

        __result = Task.CompletedTask;
        return false;
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
