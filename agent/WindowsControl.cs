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

    public static string ActiveWindowTitle()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero) return "nenhuma janela";
        var title = new StringBuilder(512);
        GetWindowText(handle, title, title.Capacity);
        return string.IsNullOrWhiteSpace(title.ToString()) ? "janela sem título" : title.ToString();
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
