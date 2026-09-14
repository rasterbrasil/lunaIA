using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using Microsoft.Win32;

namespace LunaPC;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var app = new LunaAgentContext();
        Application.Run();
    }
}

internal sealed class LunaAgentContext : ApplicationContext
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 9001;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint VkL = 0x4C;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly SpeechSynthesizer _speech;
    private readonly AiBrain _brain;
    private readonly object _speechLock = new();
    private int _listening;

    public LunaAgentContext()
    {
        _speech = new SpeechSynthesizer();
        _speech.SetOutputToDefaultAudioDevice();
        _speech.Rate = 0;
        _speech.Volume = 100;
        _brain = new AiBrain();

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "LUNA PC — Assistente"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Falar com a LUNA", null, (_, _) => StartListening());
        menu.Items.Add("Testar voz", null, (_, _) => Speak("Estou aqui, Marcos."));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => StartListening();

        _hotkeyWindow = new HotkeyWindow(OnHotkey);
        if (!RegisterHotKey(_hotkeyWindow.Handle, HotkeyId, ModControl | ModAlt, VkL))
            Speak("LUNA PC iniciada. Não consegui registrar o atalho global Ctrl Alt L.");
        else
            Speak("LUNA PC iniciada. Estou aqui, Marcos.");

        EnableStartup();
    }

    private void OnHotkey() => StartListening();

    private void StartListening()
    {
        if (Interlocked.Exchange(ref _listening, 1) == 1)
            return;

        Speak("Pode falar.");
        _ = Task.Run(ListenAndProcess);
    }

    private void ListenAndProcess()
    {
        try
        {
            using var recognizer = CreateRecognizer();
            if (recognizer is null)
            {
                Speak("Não encontrei reconhecimento de voz instalado no Windows.");
                return;
            }

            recognizer.LoadGrammar(new DictationGrammar());
            recognizer.InitialSilenceTimeout = TimeSpan.FromSeconds(5);
            recognizer.BabbleTimeout = TimeSpan.FromSeconds(3);
            recognizer.EndSilenceTimeout = TimeSpan.FromMilliseconds(900);
            recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1.5);

            var result = recognizer.Recognize(TimeSpan.FromSeconds(12));
            var text = result?.Text?.Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                Speak("Não consegui entender. Tente novamente.");
                return;
            }

            HandleCommand(text);
        }
        catch (InvalidOperationException)
        {
            Speak("Não consegui acessar o microfone. Verifique se ele está disponível no Windows.");
        }
        catch
        {
            Speak("Tive um problema ao ouvir você. Vamos tentar novamente.");
        }
        finally
        {
            Interlocked.Exchange(ref _listening, 0);
        }
    }

    private static SpeechRecognitionEngine? CreateRecognizer()
    {
        try
        {
            var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
            var ptBr = recognizers.FirstOrDefault(r =>
                r.Culture.Name.Equals("pt-BR", StringComparison.OrdinalIgnoreCase));

            return ptBr is not null
                ? new SpeechRecognitionEngine(ptBr)
                : recognizers.Count > 0 ? new SpeechRecognitionEngine(recognizers[0]) : null;
        }
        catch
        {
            return null;
        }
    }

    private void HandleCommand(string text)
    {
        var command = text.Trim().ToLowerInvariant();

        if (ContainsAny(command, "quem é você", "quem e voce", "o que você é", "o que voce e"))
        {
            Speak("Eu sou a LUNA PC. Estou começando a ganhar voz, ouvidos e, nas próximas etapas, mãos para controlar este computador.");
            return;
        }

        if (ContainsAny(command, "abra o chrome", "abrir o chrome", "abre o chrome", "abra chrome"))
        {
            if (TryStart("chrome.exe"))
                Speak("Abrindo o Chrome.");
            else
                Speak("Não encontrei o Chrome instalado neste computador.");
            return;
        }

        if (ContainsAny(command, "abra meu github", "abrir meu github", "abre meu github", "abra o github"))
        {
            OpenUrl("https://github.com/rasterbrasil/lunaIA");
            Speak("Abrindo o GitHub da LUNA PC.");
            return;
        }

        _ = AskBrainAsync(text);
    }

    private async Task AskBrainAsync(string text)
    {
        if (!_brain.IsConfigured)
        {
            Speak($"Entendi: {text}. Meu cérebro de IA ainda não está conectado neste computador. A base já está pronta; falta configurar a chave da API com segurança.");
            return;
        }

        try
        {
            var answer = await _brain.AskAsync(text);
            Speak(string.IsNullOrWhiteSpace(answer)
                ? "Não consegui obter uma resposta do meu cérebro de IA."
                : answer);
        }
        catch
        {
            Speak("Não consegui falar com meu cérebro de IA agora.");
        }
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(text.Contains);

    private static bool TryStart(string fileName)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void Speak(string text)
    {
        try
        {
            lock (_speechLock)
            {
                _speech.SpeakAsyncCancelAll();
                _speech.SpeakAsync(text);
            }
        }
        catch { }
    }

    private static void EnableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            var exe = Application.ExecutablePath;
            key?.SetValue("LunaPC", $"\"{exe}\"");
        }
        catch { }
    }

    protected override void ExitThreadCore()
    {
        UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
        _hotkeyWindow.DestroyHandle();
        _tray.Visible = false;
        _tray.Dispose();
        _speech.Dispose();
        base.ExitThreadCore();
    }

    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly Action _callback;

        public HotkeyWindow(Action callback)
        {
            _callback = callback;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
                _callback();
            base.WndProc(ref m);
        }
    }
}
