using System.Reflection;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// How a choice entry point is named wherever one is named: its name and its
/// parameter names, which is what tells two overloads apart in the game's own source
/// and survives a parameter's type being renamed.
///
/// One owner, because the recorder's forwarder table, the assembly walk that
/// enumerates the entry points and the coverage number that counts prompts by entry
/// point all have to spell one the same way.
/// </summary>
public static class EntryPointSignature
{
    public static string Of(MethodBase method) =>
        $"{method.Name}({string.Join(", ", method.GetParameters().Select(parameter => parameter.Name))})";
}
