using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LunaPC;

internal static class WindowsControl
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static string ActiveWindowTitle()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero) return "nenhuma janela";
        return WindowTitle(handle);
    }

    public static bool IsBrowserWindowTitle(string title)
    {
        return Has(Normalize(title), "chrome", "google chrome", "microsoft edge", "edge", "firefox", "opera", "brave", "vivaldi");
    }

    public static bool IsBlankBrowserWindowTitle(string title)
    {
        var normalized = Normalize(title);
        if (Has(normalized, "about:blank")) return true;
        if (!IsBrowserWindowTitle(title)) return false;
        return Has(normalized,
            "new tab", "nova guia", "new tab page", "pagina nova", "start page", "pagina inicial",
            "chrome new tab", "google chrome new tab", "google chrome nova guia",
            "microsoft edge new tab", "microsoft edge nova guia",
            "edge new tab", "edge nova guia", "new tab - google chrome", "nova guia - google chrome",
            "new tab - microsoft edge", "nova guia - microsoft edge");
    }

    public static IntPtr FindBrowserWindowHandle(bool blankOnly = false)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var title = WindowTitle(hWnd);
            if (!IsBrowserWindowHandle(hWnd)) return true;
            if (blankOnly && !IsBlankBrowserWindowTitle(title)) return true;
            found = hWnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static bool ActivateWindow(IntPtr hWnd)
        => hWnd != IntPtr.Zero && IsWindowVisible(hWnd) && SetForegroundWindow(hWnd);

    public static bool TryActivateBrowserWindow(bool blankOnly = false)
    {
        var found = FindBrowserWindowHandle(blankOnly);
        return ActivateWindow(found);
    }

    public static bool ActivateExistingBlankBrowserWindow() => TryActivateBrowserWindow(true);

    public static bool ActivateExistingBrowserWindow() => TryActivateBrowserWindow(false);

    public static string FindBrowserWindowTitle(bool blankOnly = false)
    {
        var handle = FindBrowserWindowHandle(blankOnly);
        return handle == IntPtr.Zero ? string.Empty : WindowTitle(handle);
    }

    public static LunaResult OpenNewBrowserWindow()
    {
        try
        {
            var activeTitle = ActiveWindowTitle();
            if (IsBrowserWindowTitle(activeTitle))
            {
                var result = PressKey("ctrl+n");
                if (!result.Executed) return result;
                Thread.Sleep(900);
                if (ActivateExistingBlankBrowserWindow())
                    return new("Abri uma nova janela do navegador e a deixei ativa sem interromper a página em uso.", true);
                return new("Abri uma nova janela do navegador, mas não consegui colocá-la em primeiro plano.");
            }

            if (TryStartBrowserExecutable("chrome.exe", "--new-window about:blank") ||
                TryStartBrowserExecutable("msedge.exe", "--new-window about:blank"))
            {
                Thread.Sleep(1200);
                if (ActivateExistingBlankBrowserWindow())
                    return new("Abri uma nova janela independente do navegador e a deixei ativa.", true);
                if (ActivateExistingBrowserWindow())
                    return new("Abri uma nova janela do navegador, mas precisei ativar uma janela existente.", true);
            }

            return new("Não consegui abrir e ativar uma nova janela independente do navegador.");
        }
        catch (Exception ex) { return new($"Não consegui abrir uma nova janela do navegador: {ex.Message}"); }
    }

    public static LunaResult TypeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new("Não recebi nenhum texto para digitar.");
        try
        {
            SendKeys.SendWait(EscapeForSendKeys(text));
            return new($"Digitei o texto na janela ativa: {ActiveWindowTitle()}.", true);
        }
        catch (Exception ex) { return new($"Não consegui digitar na janela ativa: {ex.Message}"); }
    }

    public static LunaResult PressKey(string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        var mapped = normalized switch
        {
            "enter" or "entrar" => "{ENTER}",
            "tab" => "{TAB}",
            "esc" or "escape" => "{ESC}",
            "espaço" or "espaco" or "space" => " ",
            "backspace" => "{BACKSPACE}",
            "delete" or "del" => "{DELETE}",
            "up" or "cima" => "{UP}",
            "down" or "baixo" => "{DOWN}",
            "left" or "esquerda" => "{LEFT}",
            "right" or "direita" => "{RIGHT}",
            "home" => "{HOME}",
            "end" => "{END}",
            "pageup" => "{PGUP}",
            "pagedown" => "{PGDN}",
            "f5" => "{F5}",
            "ctrl+l" or "control+l" => "^l",
            "ctrl+n" or "control+n" => "^n",
            "ctrl+t" or "control+t" => "^t",
            "ctrl+c" or "control+c" => "^c",
            "ctrl+v" or "control+v" => "^v",
            "ctrl+a" or "control+a" => "^a",
            "alt+tab" => "%{TAB}",
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(mapped)) return new($"Ainda não conheço a tecla ou atalho '{key}'.");
        try
        {
            SendKeys.SendWait(mapped);
            return new($"Pressionei {key} na janela ativa: {ActiveWindowTitle()}.", true);
        }
        catch (Exception ex) { return new($"Não consegui pressionar {key}: {ex.Message}"); }
    }

    private static bool IsBrowserWindowHandle(IntPtr hWnd)
    {
        try
        {
            GetWindowThreadProcessId(hWnd, out var processId);
            if (processId == 0) return false;
            using var process = Process.GetProcessById((int)processId);
            var name = process.ProcessName;
            return Has(Normalize(name), "chrome", "msedge", "firefox", "opera", "brave", "vivaldi");
        }
        catch { return IsBrowserWindowTitle(WindowTitle(hWnd)); }
    }

    private static bool TryStartBrowserExecutable(string executable, string arguments)
    {
        try
        {
            var candidates = executable.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)
                ? new[]
                {
                    executable,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", executable),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", executable),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", executable)
                }
                : new[]
                {
                    executable,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", executable),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", executable)
                };
            foreach (var candidate in candidates)
            {
                if (candidate.Contains(Path.DirectorySeparatorChar) && !File.Exists(candidate)) continue;
                try
                {
                    var process = Process.Start(new ProcessStartInfo { FileName = candidate, Arguments = arguments, UseShellExecute = true });
                    if (process is not null) return true;
                }
                catch { }
            }
        }
        catch { }
        return false;
    }

    private static string WindowTitle(IntPtr hWnd)
    {
        var title = new StringBuilder(512);
        GetWindowText(hWnd, title, title.Capacity);
        return string.IsNullOrWhiteSpace(title.ToString()) ? "janela sem título" : title.ToString();
    }

    private static string Normalize(string value)
    {
        var form = value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = form.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }

    private static bool Has(string text, params string[] terms) => terms.Any(text.Contains);

    private static string EscapeForSendKeys(string text)
    {
        var builder = new StringBuilder(text.Length + 16);
        foreach (var ch in text)
        {
            if (ch is '+' or '^' or '%' or '~' or '(' or ')' or '{' or '}' or '[' or ']')
                builder.Append('{').Append(ch).Append('}');
            else builder.Append(ch);
        }
        return builder.ToString();
    }
}
