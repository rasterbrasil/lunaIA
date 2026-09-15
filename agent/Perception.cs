using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace LunaPC;

/// <summary>
/// Camada de percepção da LUNA. Apenas observa o computador; não executa ações.
/// </summary>
internal sealed class Perception : IDisposable
{
    private bool _disposed;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    public ComputerSnapshot Capture()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Perception));

        var windows = new List<WindowSnapshot>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var title = GetTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title)) return true;
            GetWindowThreadProcessId(hWnd, out var pid);
            string process = "desconhecido";
            try { process = Process.GetProcessById((int)pid).ProcessName; } catch { }
            GetWindowRect(hWnd, out var r);
            windows.Add(new WindowSnapshot(title, process, pid, r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top)));
            return true;
        }, IntPtr.Zero);

        var processes = Process.GetProcesses()
            .Select(p =>
            {
                try { return new ProcessSnapshot(p.ProcessName, p.Id, p.WorkingSet64, p.MainWindowTitle); }
                catch { return null; }
                finally { p.Dispose(); }
            })
            .Where(p => p is not null)
            .Cast<ProcessSnapshot>()
            .OrderByDescending(p => p.MemoryBytes)
            .Take(30)
            .ToList();

        return new ComputerSnapshot(
            DateTimeOffset.Now,
            Environment.MachineName,
            Environment.OSVersion.VersionString,
            Environment.Is64BitOperatingSystem,
            Environment.ProcessorCount,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            windows,
            processes);
    }

    public string CaptureForBrain()
    {
        var snapshot = Capture();
        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
    }

    public string ReadTextFile(string path, int maxCharacters = 50000)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Perception));
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) return $"Arquivo não encontrado: {fullPath}";
        var info = new FileInfo(fullPath);
        if (info.Length > 5 * 1024 * 1024) return $"Arquivo muito grande para leitura direta: {info.Length} bytes.";
        var text = File.ReadAllText(fullPath, Encoding.UTF8);
        return text.Length <= maxCharacters ? text : text[..maxCharacters] + "\n[conteúdo truncado]";
    }

    private static string GetTitle(IntPtr hWnd)
    {
        var buffer = new StringBuilder(512);
        GetWindowText(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public void Dispose() => _disposed = true;
}

internal sealed record WindowSnapshot(string Title, string Process, uint ProcessId, int X, int Y, int Width, int Height);
internal sealed record ProcessSnapshot(string Name, int ProcessId, long MemoryBytes, string MainWindowTitle);
internal sealed record ComputerSnapshot(
    DateTimeOffset CapturedAt,
    string MachineName,
    string OperatingSystem,
    bool Is64Bit,
    int ProcessorCount,
    long TotalAvailableMemoryBytes,
    IReadOnlyList<WindowSnapshot> Windows,
    IReadOnlyList<ProcessSnapshot> Processes);
