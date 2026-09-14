using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Speech.Recognition;
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
    private readonly PiperTts _speech;
    private readonly AiBrain _brain;
    private int _listening;

    public LunaAgentContext()
    {
        _speech = new PiperTts();
        _brain = new AiBrain();

        _tray = new NotifyIcon { Icon = SystemIcons.Application, Visible = true, Text = "LUNA PC — IA privada offline" };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Falar com a LUNA (microfone)", null, (_, _) => StartListening());
        menu.Items.Add("Conversar por texto (sem microfone)", null, (_, _) => ShowTextTest());
        menu.Items.Add("Testar voz neural", null, (_, _) => Speak("Olá, Marcos. Eu sou a LUNA. Minha voz agora é neural, feminina e gerada localmente no seu computador."));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowTextTest();

        _hotkeyWindow = new HotkeyWindow(OnHotkey);
        if (!RegisterHotKey(_hotkeyWindow.Handle, HotkeyId, ModControl | ModAlt, VkL))
            Speak("LUNA PC iniciada. Não consegui registrar o atalho global Ctrl Alt L.");
        else
            Speak(_speech.IsReady
                ? "LUNA PC iniciada. Meu cérebro e minha voz locais estão prontos. Para conversar sem microfone, use o modo Conversar por texto."
                : "LUNA PC iniciada. Meu cérebro local está pronto. A voz neural ainda precisa ser preparada. Você pode conversar por texto sem microfone.");
        EnableStartup();
    }

    private void OnHotkey() => ShowTextTest();

    private void ShowTextTest()
    {
        using var form = new TextCommandForm(HandleCommand);
        form.ShowDialog();
    }

    private void StartListening()
    {
        if (Interlocked.Exchange(ref _listening, 1) == 1) return;
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
                Speak("O reconhecimento de voz do Windows não está instalado. Como você está sem microfone, use Conversar por texto. Mais adiante vamos substituir esse reconhecimento por um reconhecimento neural local.");
                return;
            }
            recognizer.LoadGrammar(new DictationGrammar());
            recognizer.InitialSilenceTimeout = TimeSpan.FromSeconds(5);
            recognizer.BabbleTimeout = TimeSpan.FromSeconds(3);
            recognizer.EndSilenceTimeout = TimeSpan.FromMilliseconds(900);
            recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1.5);
            var result = recognizer.Recognize(TimeSpan.FromSeconds(12));
            var text = result?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) { Speak("Não consegui entender. Tente novamente."); return; }
            HandleCommand(text);
        }
        catch (InvalidOperationException) { Speak("Não consegui acessar o microfone. Verifique se ele está disponível no Windows."); }
        catch { Speak("Tive um problema ao ouvir você. Vamos tentar novamente."); }
        finally { Interlocked.Exchange(ref _listening, 0); }
    }

    private static SpeechRecognitionEngine? CreateRecognizer()
    {
        try
        {
            var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
            var ptBr = recognizers.FirstOrDefault(r => r.Culture.Name.Equals("pt-BR", StringComparison.OrdinalIgnoreCase));
            return ptBr is not null ? new SpeechRecognitionEngine(ptBr) : recognizers.Count > 0 ? new SpeechRecognitionEngine(recognizers[0]) : null;
        }
        catch { return null; }
    }

    private void HandleCommand(string text)
    {
        var command = text.Trim().ToLowerInvariant();
        if (ContainsAny(command, "quem é você", "quem e voce", "o que você é", "o que voce e"))
        { Speak("Eu sou a LUNA, uma inteligência artificial pessoal e privada. Meu cérebro e minha voz podem funcionar localmente no seu computador, sem depender de uma API de terceiros."); return; }
        if (ContainsAny(command, "abra o chrome", "abrir o chrome", "abre o chrome", "abra chrome"))
        { if (TryStart("chrome.exe")) Speak("Abrindo o Chrome."); else Speak("Não encontrei o Chrome instalado neste computador."); return; }
        if (ContainsAny(command, "abra meu github", "abrir meu github", "abre meu github", "abra o github"))
        { OpenUrl("https://github.com/rasterbrasil/lunaIA"); Speak("Abrindo o GitHub da LUNA PC. Essa ação precisa de internet."); return; }
        _ = AskBrainAsync(text);
    }

    private async Task AskBrainAsync(string text)
    {
        try
        {
            var answer = await _brain.AskAsync(text);
            Speak(string.IsNullOrWhiteSpace(answer) ? "Meu cérebro local não retornou uma resposta." : answer);
        }
        catch
        {
            Speak("Não consegui falar com meu cérebro local agora.");
        }
    }

    private static bool ContainsAny(string text, params string[] values) => values.Any(text.Contains);

    private static bool TryStart(string fileName)
    {
        try { Process.Start(new ProcessStartInfo { FileName = fileName, UseShellExecute = true }); return true; }
        catch { return false; }
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

    private void Speak(string text)
    {
        if (_speech.IsReady)
        {
            _speech.Speak(text);
            return;
        }
    }

    private static void EnableStartup()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true); key?.SetValue("LunaPC", $"\"{Application.ExecutablePath}\""); } catch { }
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
        public HotkeyWindow(Action callback) { _callback = callback; CreateHandle(new CreateParams()); }
        protected override void WndProc(ref Message m) { if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId) _callback(); base.WndProc(ref m); }
    }
}

internal sealed class TextCommandForm : Form
{
    private readonly TextBox _input;
    private readonly Action<string> _command;

    public TextCommandForm(Action<string> command)
    {
        _command = command;
        Text = "LUNA PC — Conversar por texto";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 560; Height = 220;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;

        var title = new Label { Text = "🧠 LUNA PC — Cérebro local", Left = 20, Top = 18, Width = 500, Font = new Font("Segoe UI", 14, FontStyle.Bold) };
        var info = new Label { Text = "Digite sua mensagem. Não precisa de microfone.", Left = 20, Top = 55, Width = 500 };
        _input = new TextBox { Left = 20, Top = 82, Width = 500 };
        _input.PlaceholderText = "Ex.: Luna, como você está?";
        var send = new Button { Text = "Enviar para a LUNA", Left = 20, Top = 120, Width = 160, Height = 34 };
        send.Click += (_, _) => Submit();
        _input.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Submit(); } };
        Controls.AddRange([title, info, _input, send]);
        AcceptButton = send;
        Shown += (_, _) => _input.Focus();
    }

    private void Submit()
    {
        var text = _input.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        _command(text);
        _input.Clear();
    }
}
