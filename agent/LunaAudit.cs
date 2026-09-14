using System.Text.Json;

namespace LunaPC;

internal static class LunaAudit
{
    private static readonly object Gate = new();

    public static void Record(string input, LunaResult result)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaIA", "logs");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "actions.jsonl");
            var entry = new
            {
                timestamp = DateTimeOffset.Now,
                input,
                result = result.Text,
                executed = result.Executed
            };
            lock (Gate)
                File.AppendAllText(file, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch { }
    }
}
