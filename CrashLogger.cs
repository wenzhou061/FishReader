namespace FishReader;

internal static class CrashLogger
{
    private static readonly object Gate = new();

    public static void Write(string message, object? detail = null)
    {
        var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        if (detail is not null)
            entry += $"{Environment.NewLine}{detail}";
        entry += Environment.NewLine;

        lock (Gate)
        {
            foreach (var directory in CandidateDirectories())
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    File.AppendAllText(Path.Combine(directory, "app.log"), entry);
                    return;
                }
                catch
                {
                    // Logging must never mask the original failure.
                }
            }
        }
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "data");

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
            yield return Path.Combine(localAppData, "FishReader");
    }
}
