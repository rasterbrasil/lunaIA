using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LunaPC;

/// <summary>
/// Executa ações no Windows. Mantém SendInput como fallback, mas resolve nomes
/// humanos de aplicativos para executáveis reais antes de iniciar um processo.
/// </summary>
internal sealed class ActionEngine
{
    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1, MOUSEEVENTF_LEFTDOWN = 2, MOUSEEVENTF_LEFTUP = 4, KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4;
    private readonly Func<string, bool> _confirm;
    [DllImport("user32.dll")] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk,wScan; public uint dwFlags,time; public IntPtr dwExtraInfo; }

    public ActionEngine(Func<string,bool> confirm) => _confirm=confirm;

    public async Task<ActionExecutionResult> ExecuteAsync(IEnumerable<BrainAction> actions,CancellationToken ct=default)
    {
        var results=new List<string>();
        foreach(var a in actions)
        {
            ct.ThrowIfCancellationRequested();
            if(string.IsNullOrWhiteSpace(a.Type)) continue;
            if(IsRisky(a)&&!_confirm(Describe(a))){results.Add("Cancelada pelo usuário: "+Describe(a));continue;}
            try{results.Add(await ExecuteOneAsync(a,ct));}
            catch(Exception ex){results.Add("Falhou: "+Describe(a)+" — "+ex.Message);}
        }
        return new(results);
    }

    private async Task<string> ExecuteOneAsync(BrainAction a,CancellationToken ct)
    {
        switch(a.Type)
        {
            case "open_app":
            {
                var executable = ResolveApplication(a.Target);
                var process = Process.Start(new ProcessStartInfo(executable)
                {
                    Arguments = a.Arguments ?? "",
                    UseShellExecute = true
                });
                if (process is null) throw new InvalidOperationException("O Windows não conseguiu iniciar o aplicativo.");
                await Task.Delay(900,ct);
                return "Abri " + a.Target + " (" + executable + ").";
            }
            case "open_url": Process.Start(new ProcessStartInfo(a.Url){UseShellExecute=true}); await Task.Delay(600,ct); return "Abri o site.";
            case "run_process": Process.Start(new ProcessStartInfo(a.Target){Arguments=a.Arguments??"",UseShellExecute=true}); return "Executei "+a.Target+".";
            case "click": if(!SetCursorPos(a.X,a.Y)) throw new InvalidOperationException("Não consegui mover o mouse."); SendMouse(MOUSEEVENTF_LEFTDOWN);SendMouse(MOUSEEVENTF_LEFTUP);return $"Cliquei em ({a.X},{a.Y}).";
            case "move_mouse": if(!SetCursorPos(a.X,a.Y)) throw new InvalidOperationException("Não consegui mover o mouse.");return $"Mudei o mouse para ({a.X},{a.Y}).";
            case "type_text": SendUnicodeText(a.Value??"");return "Digitei o texto.";
            case "key": SendKeys(a.Value??"");return "Pressionei "+a.Value+".";
            case "create_directory": Directory.CreateDirectory(Path.GetFullPath(a.Path));return "Criei a pasta.";
            case "copy_file": File.Copy(Path.GetFullPath(a.Path),Path.GetFullPath(a.Destination),true);return "Copiei o arquivo.";
            case "move_file": File.Move(Path.GetFullPath(a.Path),Path.GetFullPath(a.Destination),true);return "Movi o arquivo.";
            case "delete_file": File.Delete(Path.GetFullPath(a.Path));return "Excluí o arquivo.";
            case "run_powershell": using(var p=Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -Command "+Quote(a.Value)){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true})){if(p==null)throw new InvalidOperationException("PowerShell não iniciou.");await p.WaitForExitAsync(ct);var e=await p.StandardError.ReadToEndAsync(ct);if(p.ExitCode!=0)throw new InvalidOperationException(e);return "Executei a tarefa PowerShell.";}
            default: throw new InvalidOperationException("Ação não suportada: "+a.Type);
        }
    }

    /// <summary>
    /// Converte o nome que o usuário fala/escreve em um alvo que o Windows consegue iniciar.
    /// Também aceita nomes de executáveis e caminhos completos.
    /// </summary>
    private static string ResolveApplication(string target)
    {
        var raw = (target ?? "").Trim().Trim('"');
        var normalized = RemoveDiacritics(raw).ToLowerInvariant();
        normalized = normalized.Replace("  ", " ");

        var aliases = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            ["calculadora"] = "calc.exe",
            ["calculator"] = "calc.exe",
            ["calc"] = "calc.exe",
            ["google chrome"] = "chrome.exe",
            ["chrome"] = "chrome.exe",
            ["navegador chrome"] = "chrome.exe",
            ["microsoft edge"] = "msedge.exe",
            ["edge"] = "msedge.exe",
            ["navegador edge"] = "msedge.exe",
            ["bloco de notas"] = "notepad.exe",
            ["notepad"] = "notepad.exe",
            ["explorador de arquivos"] = "explorer.exe",
            ["explorador de ficheiros"] = "explorer.exe",
            ["explorer"] = "explorer.exe",
            ["gerenciador de tarefas"] = "taskmgr.exe",
            ["task manager"] = "taskmgr.exe",
            ["prompt de comando"] = "cmd.exe",
            ["cmd"] = "cmd.exe",
            ["powershell"] = "powershell.exe",
            ["configuracoes"] = "ms-settings:",
            ["configuracoes do windows"] = "ms-settings:"
        };

        if (aliases.TryGetValue(normalized, out var executable)) return executable;
        if (File.Exists(raw)) return raw;
        if (raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || raw.Contains(':') || raw.Contains('\\')) return raw;

        // Permite que o Windows resolva aplicativos registrados, atalhos e comandos disponíveis no PATH.
        return raw;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray();
        return new string(chars).Normalize(System.Text.NormalizationForm.FormC);
    }

    private static bool IsRisky(BrainAction a)=>a.Risk is "confirm" or "high"||a.Type is "delete_file" or "run_powershell" or "move_file";
    private static string Describe(BrainAction a)=>a.Type switch{"click"=>$"Clicar em ({a.X},{a.Y})","type_text"=>"Digitar texto","key"=>$"Pressionar {a.Value}","delete_file"=>$"Excluir {a.Path}","move_file"=>$"Mover {a.Path} para {a.Destination}","run_powershell"=>$"Executar PowerShell: {a.Value}",_=>$"Executar {a.Type}: {a.Target}"};
    private static string Quote(string s)=>"\""+(s??"").Replace("\"","\\\"")+"\"";
    private static void SendMouse(uint flags){var i=new INPUT{type=INPUT_MOUSE,U=new InputUnion{mi=new MOUSEINPUT{dwFlags=flags}}};if(SendInput(1,new[]{i},Marshal.SizeOf<INPUT>())!=1)throw new InvalidOperationException("O Windows recusou o mouse.");}
    private static void SendUnicodeText(string text){var list=new List<INPUT>();foreach(var c in text){list.Add(Key(c,false));list.Add(Key(c,true));}if(list.Count>0&&SendInput((uint)list.Count,list.ToArray(),Marshal.SizeOf<INPUT>())!=list.Count)throw new InvalidOperationException("O Windows recusou a digitação.");}
    private static INPUT Key(char c,bool up)=>new(){type=INPUT_KEYBOARD,U=new InputUnion{ki=new KEYBDINPUT{wScan=c,dwFlags=KEYEVENTF_UNICODE|(up?KEYEVENTF_KEYUP:0)}}};
    private static void SendKeys(string combo){var keys=combo.Split('+',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(KeyCode).ToList();if(keys.Any(k=>k==0))throw new InvalidOperationException("Tecla não reconhecida: "+combo);var list=keys.Select(k=>KeyInput(k,false)).Concat(keys.AsEnumerable().Reverse().Select(k=>KeyInput(k,true))).ToArray();if(SendInput((uint)list.Length,list,Marshal.SizeOf<INPUT>())!=list.Length)throw new InvalidOperationException("O Windows recusou a tecla.");}
    private static INPUT KeyInput(ushort k,bool up)=>new(){type=INPUT_KEYBOARD,U=new InputUnion{ki=new KEYBDINPUT{wVk=k,dwFlags=up?KEYEVENTF_KEYUP:0}}};
    private static ushort KeyCode(string s)=>s.ToUpperInvariant() switch{"ENTER"=>13,"ESC" or "ESCAPE"=>27,"TAB"=>9,"SPACE"=>32,"BACKSPACE"=>8,"DELETE" or "DEL"=>46,"HOME"=>36,"END"=>35,"LEFT"=>37,"UP"=>38,"RIGHT"=>39,"DOWN"=>40,"CTRL" or "CONTROL"=>17,"ALT"=>18,"SHIFT"=>16,"WIN" or "WINDOWS"=>91,"F1"=>112,"F2"=>113,"F3"=>114,"F4"=>115,"F5"=>116,"F6"=>117,"F7"=>118,"F8"=>119,"F9"=>120,"F10"=>121,"F11"=>122,"F12"=>123,_ when s.Length==1&&char.IsLetterOrDigit(s[0])=>(ushort)char.ToUpperInvariant(s[0]),_=>0};
}
internal sealed record ActionExecutionResult(IReadOnlyList<string> Results);
