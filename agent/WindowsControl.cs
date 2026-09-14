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
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

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
        if (!IsBrowserWindowTitle(title)) return false;

        return Has(normalized,
            "new tab", "nova guia", "new tab page", "pagina nova", "start page", "pagina inicial",
            "chrome new tab", "google chrome new tab", "google chrome nova guia",
            "microsoft edge new tab", "microsoft edge nova guia",
            "edge new tab", "edge nova guia", "new tab - google chrome", "nova guia - google chrome",
            "new tab - microsoft edge", "nova guia - microsoft edge");
    }

    public static bool ActivateExistingBlankBrowserWindow()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var title = WindowTitle(hWnd);
            if (IsBlankBrowserWindowTitle(title))
            {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return found != IntPtr.Zero && SetForegroundWindow(found);
    }

    public static bool ActivateExistingBrowserWindow()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var title = WindowTitle(hWnd);
            if (IsBrowserWindowTitle(title))
            {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return found != IntPtr.Zero && SetForegroundWindow(found);
    }

    public static LunaResult OpenNewBrowserWindow()
    {
        try
        {
            var title = ActiveWindowTitle();
            if (!IsBrowserWindowTitle(title)) return new("A janela ativa não é um navegador.");
            var result = PressKey("ctrl+n");
            if (!result.Executed) return result;
            Thread.Sleep(700);
            return new("Abri uma nova janela do navegador para não interromper a página que já estava em uso.", true);
        }
        catch (Exception ex) { return new($"Não consegui abrir uma nova janela do navegador: {ex.Message}"); }
    }

    public static LunaResult TypeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new("Não recebi nenhum texto para digitar.");

        try
        {
            SendKeys.SendWait(EscapeForSendKeys(text));
            return new($"Digitei o texto na janela ativa: {ActiveWindowTitle()}.", true);
        }
        catch (Exception ex)
        {
            return new($"Não consegui digitar na janela ativa: {ex.Message}");
        }
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

        if (string.IsNullOrEmpty(mapped))
            return new($"Ainda não conheço a tecla ou atalho '{key}'.");

        try
        {
            SendKeys.SendWait(mapped);
            return new($"Pressionei {key} na janela ativa: {ActiveWindowTitle()}.", true);
        }
        catch (Exception ex)
        {
            return new($"Não consegui pressionar {key}: {ex.Message}");
        }
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
            else
                builder.Append(ch);
        }
        return builder.ToString();
    }
}
