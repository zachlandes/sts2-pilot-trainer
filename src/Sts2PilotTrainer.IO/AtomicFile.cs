namespace Sts2PilotTrainer.IO;

/// <summary>
/// The one way anything in this project replaces a file: write a temporary sibling,
/// then move it over the target.
///
/// A half-written file is the failure this prevents. The arbiter's evidence and the
/// mod's own store are read back by other processes - and, for the mod, by the next
/// launch of a game the player is in the middle of - so a crash between two writes
/// must leave the previous file intact rather than a truncated one.
///
/// The temporary sibling is inside the destination directory rather than a system
/// temp directory, because a move across filesystems is a copy and a delete and is
/// not atomic. It is checked for containment even though this code composed it, so
/// that a caller passing a directory the containment rule would refuse is refused
/// here too rather than one line later.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string content) =>
        Write(path, temporary => File.WriteAllText(temporary, content));

    public static void WriteAllBytes(string path, byte[] content) =>
        Write(path, temporary => File.WriteAllBytes(temporary, content));

    private static void Write(string path, Action<string> writeTemporary)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)
            ?? throw new ArgumentException($"'{path}' has no directory to write into.", nameof(path));
        var temporary = PathContainment.RequireContained(
            directory,
            Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp"));

        try
        {
            writeTemporary(temporary);
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
