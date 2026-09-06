using System.Reflection;
using Sts2PilotTrainer.Engine;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Puts <see cref="EngineHost"/> back to the process these tests were started in.
///
/// A started headless engine is process-wide and is exactly what
/// <c>AdoptRunningGame</c> refuses on, so a test that starts one changes the answer
/// every later test gets from this host. Starting it is not rare and is not always
/// deliberate: reading this build goes through <c>GameIdentity.Read</c>, which asks the
/// engine for a content hash, so anything that asks the library which runs it holds has
/// started one.
///
/// The engine itself stays initialised, which is what makes a second start a no-op.
/// Only the flags that say this process owns a headless engine are cleared.
/// </summary>
internal static class HeadlessEngine
{
    internal static void Forget()
    {
        Field("_started").SetValue(null, false);
        Field("<Origin>k__BackingField").SetValue(null, EngineOrigin.None);
        Field("<Startup>k__BackingField").SetValue(null, null);
    }

    private static FieldInfo Field(string name) =>
        typeof(EngineHost).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"EngineHost has no {name}; this reset is out of date.");
}
