using System.Reflection;
using HarmonyLib;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Reads what is patched in this process, out of Harmony's own registry.
///
/// The one owner of that reading. It is asked twice for different reasons - the mod
/// writes it into the game's log the moment its patches are installed, and the
/// recorder captures it into a run's environment at run start - and two readers would
/// be two things to fix when Harmony moves.
///
/// It touches nothing of the game. <c>Harmony.GetAllPatchedMethods</c> and
/// <c>GetPatchInfo</c> read a static registry Harmony keeps of its own work, so this
/// is safe at mod initialization, one phase before the game has a model database and
/// where reading the game at all takes the process down. That is not incidental: the
/// startup log line is the reason this reading exists, and it happens there.
///
/// Every patched method in the process, not only the game's own: a mod patching the
/// engine underneath the game is not a mod that patched nothing, and a filter here
/// would be the roster deciding what counts.
/// </summary>
public static class HarmonyRoster
{
    /// <summary>
    /// Everything patched right now, ordered so two readings of the same process
    /// produce the same list.
    ///
    /// Harmony's registry is keyed by method and hands its members back in whatever
    /// order it holds them, so the sort is what makes a roster comparable with
    /// another roster at all.
    /// </summary>
    public static PatchRoster Read()
    {
        var members = new List<PatchedMember>();
        foreach (var method in Harmony.GetAllPatchedMethods())
        {
            // Harmony returns null for a method it no longer holds patches for, which
            // is what an unpatch leaves behind. A member nothing patches is not a
            // patched member.
            if (Harmony.GetPatchInfo(method) is not { } info) continue;

            var owners = info.Owners.OrderBy(owner => owner, StringComparer.Ordinal).ToList();
            if (owners.Count == 0) continue;

            members.Add(new PatchedMember(
                method.DeclaringType?.FullName ?? "<no declaring type>",
                Signature(method),
                owners,
                info.Prefixes.Count,
                info.Postfixes.Count,
                info.Transpilers.Count,
                info.Finalizers.Count));
        }

        return new PatchRoster
        {
            Members = members
                .OrderBy(member => member.DeclaringType, StringComparer.Ordinal)
                .ThenBy(member => member.Member, StringComparer.Ordinal)
                .ToList(),
        };
    }

    /// <summary>
    /// The member's name with its parameter types.
    ///
    /// The parameters are there because a name alone does not identify an overload,
    /// and because a build that changed a signature rather than a name is exactly the
    /// drift this roster is a fingerprint against - the game grew two parameters on a
    /// run-start method once already, which a name-only roster would have read as
    /// unchanged.
    /// </summary>
    private static string Signature(MethodBase method) =>
        $"{method.Name}({string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name))})";
}
