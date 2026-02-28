using System.Text.Json;

namespace Raka.Cli.Session;

/// <summary>
/// Manages persistent CLI sessions. Stores the current connection info
/// so subsequent CLI calls can reuse it (like Playwright CLI sessions).
/// </summary>
internal sealed class SessionManager
{
    private static readonly string SessionDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "raka", "sessions");

    private static readonly string ActiveSessionFile = Path.Combine(SessionDir, "active.json");

    public sealed class SessionInfo
    {
        public string PipeName { get; set; } = "";
        public int ProcessId { get; set; }
        public string? ProcessName { get; set; }
        public string? WindowTitle { get; set; }
        public DateTime ConnectedAt { get; set; }
    }

    public static SessionInfo? LoadActive()
    {
        if (!File.Exists(ActiveSessionFile))
            return null;

        try
        {
            var json = File.ReadAllText(ActiveSessionFile);
            var session = JsonSerializer.Deserialize(json, CliJsonContext.Default.SessionInfo);

            // Verify the target process is still alive
            if (session != null)
            {
                try
                {
                    System.Diagnostics.Process.GetProcessById(session.ProcessId);
                    return session;
                }
                catch
                {
                    // Process no longer exists
                    ClearActive();
                    return null;
                }
            }
        }
        catch
        {
            // Corrupt session file
        }

        return null;
    }

    public static void SaveActive(SessionInfo session)
    {
        Directory.CreateDirectory(SessionDir);
        var json = JsonSerializer.Serialize(session, CliJsonContext.Pretty.SessionInfo);
        File.WriteAllText(ActiveSessionFile, json);
    }

    public static void ClearActive()
    {
        if (File.Exists(ActiveSessionFile))
            File.Delete(ActiveSessionFile);
    }
}
