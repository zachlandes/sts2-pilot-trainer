using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Stands the localization system up with no translation data at all.
///
/// The real tables live inside the game's Godot resource pack, which a headless
/// process has no reader for. Rather than extract and redistribute MegaCrit's
/// text - which this project will not do - localization is stubbed so that every
/// lookup succeeds and returns its own key.
///
/// This is safe precisely because nothing this project compares is localized. The
/// canonical state is built from model ids and numbers; display text is on the
/// canonical form's excluded list by design, so a digest can never depend on which
/// language a reader happens to run in. What it costs is legibility in a raw dump,
/// which is what model ids are for.
///
/// One honest caveat, recorded rather than buried: the game can pick a random
/// string from a table (<c>LocString.GetRandomWithPrefix</c>). Against empty
/// tables there is nothing to pick from. If any gameplay path drew from a
/// run-persistent RNG stream to choose flavour text, the stub would change that
/// stream's position. The proof this milestone runs would catch such a divergence
/// - it compares generated map topology and combat outcomes against the video -
/// but it is a caveat, not a proof of absence.
/// </summary>
internal static class Localization
{
    private static LocTable? _emptyTable;

    internal static void Initialize(List<string> warnings)
    {
        try
        {
            // The constructor reads configuration this process does not have, so the
            // instance is built uninitialised and its fields set directly.
            var manager = (LocManager)RuntimeHelpers.GetUninitializedObject(typeof(LocManager));
            SetField(manager, "_tables", new Dictionary<string, LocTable>(StringComparer.Ordinal));
            SetField(manager, "_engTables", new Dictionary<string, LocTable>(StringComparer.Ordinal));

            // The callback list's element type is internal to the game, so it is
            // constructed from the field's own type rather than named here.
            var callbacksField = typeof(LocManager).GetField("_localeChangeCallbacks", Instance)!;
            callbacksField.SetValue(manager, Activator.CreateInstance(callbacksField.FieldType));

            // These properties are public to read and not to write, so the compiler
            // is right to refuse. Setting the backing fields is deliberate: this
            // object was never constructed, and nothing else can populate them.
            SetField(manager, "<Language>k__BackingField", "eng");
            SetField(manager, "<CultureInfo>k__BackingField", System.Globalization.CultureInfo.InvariantCulture);
            SetField(manager, "<StringComparer>k__BackingField", StringComparer.Ordinal);
            typeof(LocManager)
                .GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, manager);

            _emptyTable = new LocTable("stub", new Dictionary<string, string>(StringComparer.Ordinal), null!);

            ApplyPatches(warnings);
        }
        catch (Exception ex)
        {
            warnings.Add($"localization: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ApplyPatches(List<string> warnings)
    {
        var harmony = new Harmony("sts2-pilot-trainer.localization");
        var self = typeof(Localization);

        Patch(harmony, typeof(LocManager), "GetTable", self, nameof(ReturnEmptyTable), warnings);
        // LocString.GetFormattedText is the choke point every display string goes
        // through. Patching it here rather than the formatter underneath means the
        // formatter is never reached at all - which matters, because the game builds
        // hover tips for cards and relics while generating event options, and a
        // half-initialised formatter turns that into a null reference deep inside
        // event setup.
        Patch(harmony, typeof(LocString), "GetFormattedText", self, nameof(ReturnLocStringKey), warnings);
        Patch(harmony, typeof(LocString), "GetRawText", self, nameof(ReturnLocStringKey), warnings);
        Patch(harmony, typeof(LocTable), "HasEntry", self, nameof(ReturnTrue), warnings);
        Patch(harmony, typeof(LocTable), "IsLocalKey", self, nameof(ReturnTrue), warnings);
        Patch(harmony, typeof(LocTable), "GetRawText", self, nameof(ReturnKey), warnings);
        Patch(harmony, typeof(LocTable), "GetLocStringsWithPrefix", self, nameof(ReturnEmptyLocStrings), warnings);
        Patch(harmony, typeof(LocString), "Exists", self, nameof(ReturnTrue), warnings);
    }

    private static void Patch(
        Harmony harmony, Type target, string method, Type patchHolder, string patchName, List<string> warnings)
    {
        var originals = target
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == method)
            .ToArray();

        if (originals.Length == 0)
        {
            warnings.Add($"localization patch: {target.Name}.{method} not found in this build");
            return;
        }

        var prefix = new HarmonyMethod(patchHolder.GetMethod(patchName, BindingFlags.NonPublic | BindingFlags.Static)!);
        foreach (var original in originals)
        {
            try
            {
                harmony.Patch(original, prefix: prefix);
            }
            catch (Exception ex)
            {
                warnings.Add($"localization patch {target.Name}.{method}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, Instance)
            ?? throw new EngineException($"LocManager.{name} is absent from this build.");
        field.SetValue(target, value);
    }

    // ── Harmony prefixes. Returning false skips the original. ──

    private static bool ReturnEmptyTable(ref LocTable __result)
    {
        __result = _emptyTable!;
        return false;
    }

    private static bool ReturnTrue(ref bool __result)
    {
        __result = true;
        return false;
    }

    private static bool ReturnKey(ref string __result, string key)
    {
        __result = key;
        return false;
    }

    private static bool ReturnLocStringKey(ref string __result, LocString __instance)
    {
        __result = $"{__instance.LocTable}.{__instance.LocEntryKey}";
        return false;
    }

    private static bool ReturnEmptyLocStrings(ref IReadOnlyList<LocString> __result)
    {
        __result = [];
        return false;
    }
}
