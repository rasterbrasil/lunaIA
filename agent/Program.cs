using System.Diagnostics;
using System.Speech.Recognition;
using Microsoft.Win32;

namespace LunaPC;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "LunaPC.SingleInstance.2026", out var createdNew);
        if (!createdNew) return;

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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly NotifyIcon _tray;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly PiperTts _speech;
    private readonly AiBrain _brain;
    private readonly Perception _perception;
    private readonly ActionEngine _actions;
    private readonly OperationalMemory _memory;
    private readonly AutonomyEngine _autonomy;
    private Timer? _startupTimer;
    private int _listening;

    public LunaAgentContext()
    {
        _speech = new PiperTts();
        _brain = new AiBrain();
        _memory = _brain.Memory;
        _perception = new Perception();
        _actions = new ActionEngine(ConfirmAction);
        _autonomy = new AutonomyEngine(_brain, _perception, _actions, _memory);

        _tray = new NotifyIcon { Icon = SystemIcons.Application, Visible = true, Text = "LUNA PC — IA privada offline" };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Falar com a LUNA (microfone)", null, (_, _) => StartListening());
        menu.Items.Add("Conversar por texto (sem microfone)", null, (_, _) => ShowTextTest());
        menu.Items.Add("👁 Ver estado do computador", null, (_, _) => ShowPerception());
        menu.Items.Add("🧬 Ver memória operacional", null, (_, _) => ShowMemory());
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
                ? "LUNA PC iniciada. Meu cérebro, minha percepção, minha ação e minha memória locais estão prontos."
                : "LUNA PC iniciada. Meu cérebro, minha percepção, minha ação e minha memória estão prontos. A voz neural ainda precisa ser preparada.");
        EnableStartup();

        // A versão anterior funcionava como aplicativo de bandeja e não abria uma janela visível.
        // Agora a conversa é apresentada automaticamente após o loop de mensagens iniciar.
        _startupTimer = new Timer { Interval = 350 };
        _startupTimer.Tick += (_, _) =>
        {
            _startupTimer?.Stop();
            _startupTimer?.Dispose();
            _startupTimer = null;
            ShowTextTest();
        };
        _startupTimer.Start();
    }

    private bool ConfirmAction(string description)
    {
        using var dialog = new ConfirmationForm(description);
        return dialog.ShowDialog() == DialogResult.Yes;
    }

    private void OnHotkey() => ShowTextTest();

    private void ShowTextTest()
    {
        using var form = new TextCommandForm(HandleCommandAsync);
        form.ShowDialog();
    }

    private void ShowPerception()
    {
        try
        {
            var snapshot = _perception.Capture();
            using var form = new PerceptionForm(snapshot);
            form.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não consegui analisar o estado do computador.\n\n{ex.Message}", "LUNA PC — Percepção", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowMemory()
    {
        var text = _memory.ForBrain(maxItems: 100);
        using var form = new MemoryForm(text);
        form.ShowDialog();
    }

    private void StartListening()
    {
        if (Interlocked.Exchange(ref _listening, 1) == 1) return;
        Speak("Pode falar.");
        _ = Task.Run(ListenAndProcess);
    }

    private async Task ListenAndProcess()
    {
        try
        {
            using var recognizer = CreateRecognizer();
            if (recognizer is null) { Speak("O reconhecimento de voz do Windows não está instalado. Use Conversar por texto."); return; }
            recognizer.LoadGrammar(new DictationGrammar());
            recognizer.InitialSilenceTimeout = TimeSpan.FromSeconds(5);
            recognizer.BabbleTimeout = TimeSpan.FromSeconds(3);
            recognizer.EndSilenceTimeout = TimeSpan.FromMilliseconds(900);
            recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1.5);
            var result = recognizer.Recognize(TimeSpan.FromSeconds(12));
            var text = result?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) { Speak("Não consegui entender. Tente novamente."); return; }
            var answer = await HandleCommandAsync(text);
            Speak(answer);
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

    private async Task<string> HandleCommandAsync(string text)
    {
        var trimmed = text.Trim();
        var lower = trimmed.ToLowerInvariant();

        if (lower.Contains("analise meu computador") || lower.Contains("analisa meu computador") || lower.Contains("estado do computador") || lower.Contains("veja meu computador") || lower.Contains("ver computador"))
        {
            var snapshot = _perception.CaptureForBrain();
            var decision = await _brain.ThinkAsync("Analise o estado observado do computador abaixo e me explique o que está acontecendo, sem afirmar que executou ações.\n\n" + snapshot);
            return decision?.Response ?? "Não consegui interpretar o estado do computador.";
        }

        if (lower.StartsWith("leia o arquivo ") || lower.StartsWith("ler o arquivo "))
        {
            var marker = lower.StartsWith("leia o arquivo ") ? "leia o arquivo " : "ler o arquivo ";
            var path = trimmed[marker.Length..].Trim().Trim('"');
            try
            {
                var content = _perception.ReadTextFile(path);
                var decision = await _brain.ThinkAsync("Leia e analise este arquivo. Explique os pontos importantes sem inventar informações.\n\nARQUIVO: " + path + "\n\nCONTEÚDO:\n" + content);
                return decision?.Response ?? content;
            }
            catch (Exception ex) { return "Não consegui ler o arquivo: " + ex.Message; }
        }

        // Todos os pedidos normais passam pelo agente: observar → entender → decidir → agir → observar → verificar.
        try
        {
            var result = await _autonomy.ExecuteAsync(trimmed);
            return result.Response;
        }
        catch (OperationCanceledException) { return "Parei a tarefa antes de concluí-la."; }
        catch (Exception ex) { return "Encontrei um problema ao executar a tarefa: " + ex.Message; }
    }

    private void Speak(string text) { if (_speech.IsReady) _speech.Speak(text); }

    private void EnableStartup()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true); key?.SetValue("LunaPC", $"\"{Application.ExecutablePath}\""); } catch { }
    }

    protected override void ExitThreadCore()
    {
        _startupTimer?.Stop();
        _startupTimer?.Dispose();
        UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
        _hotkeyWindow.DestroyHandle();
        _tray.Visible = false;
        _tray.Dispose();
        _brain.Dispose();
        _perception.Dispose();
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

internal sealed class ConfirmationForm : Form
{
    public ConfirmationForm(string action)
    {
        Text = "LUNA PC — Confirmação necessária";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 520; Height = 230;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        var label = new Label { Text = "⚠️ Esta ação pode alterar ou remover dados do computador.\n\nA LUNA quer:\n" + action + "\n\nDeseja permitir?", Left = 20, Top = 20, Width = 465, Height = 115 };
        var yes = new Button { Text = "Sim, permitir", DialogResult = DialogResult.Yes, Left = 275, Top = 150, Width = 105, Height = 34 };
        var no = new Button { Text = "Não", DialogResult = DialogResult.No, Left = 390, Top = 150, Width = 75, Height = 34 };
        Controls.AddRange([label, yes, no]);
        AcceptButton = yes; CancelButton = no;
    }
}

internal sealed class PerceptionForm : Form
{
    public PerceptionForm(ComputerSnapshot snapshot)
    {
        Text = "LUNA PC — Percepção";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 900; Height = 650;
        var box = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10) };
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("👁 PERCEPÇÃO DA LUNA");
        sb.AppendLine($"Computador: {snapshot.MachineName}");
        sb.AppendLine($"Sistema: {snapshot.OperatingSystem}");
        sb.AppendLine($"CPU: {snapshot.ProcessorCount} núcleos | RAM disponível: {snapshot.TotalAvailableMemoryBytes / 1024 / 1024:N0} MB");
        sb.AppendLine(); sb.AppendLine("JANELAS VISÍVEIS:");
        foreach (var w in snapshot.Windows) sb.AppendLine($"• {w.Title} | {w.Process} (PID {w.ProcessId}) [{w.X},{w.Y} {w.Width}x{w.Height}]");
        sb.AppendLine(); sb.AppendLine("PROCESSOS (maior uso de memória):");
        foreach (var p in snapshot.Processes) sb.AppendLine($"• {p.Name} (PID {p.ProcessId}) | {p.MemoryBytes / 1024 / 1024:N0} MB | {p.MainWindowTitle}");
        box.Text = sb.ToString();
        Controls.Add(box);
    }
}

internal sealed class MemoryForm : Form
{
    public MemoryForm(string json)
    {
        Text = "LUNA PC — Memória operacional";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 850; Height = 600;
        var box = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Text = json };
        Controls.Add(box);
    }
}

internal sealed class TextCommandForm : Form
{
    private readonly TextBox _input;
    private readonly Button _send;
    private readonly TextBox _conversation;
    private readonly Func<string, Task<string>> _command;

    public TextCommandForm(Func<string, Task<string>> command)
    {
        _command = command;
        Text = "LUNA PC — Conversar por texto";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 620; Height = 430;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        var title = new Label { Text = "🧠👁🖐️🧬 LUNA PC — Agente", Left = 20, Top = 18, Width = 560, Font = new Font("Segoe UI", 14, FontStyle.Bold) };
        var info = new Label { Text = "A LUNA interpreta, observa, age, verifica e aprende fatos operacionais confirmados. Ações potencialmente perigosas pedem confirmação.", Left = 20, Top = 55, Width = 560 };
        _conversation = new TextBox { Left = 20, Top = 82, Width = 560, Height = 230, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window };
        _input = new TextBox { Left = 20, Top = 325, Width = 455 };
        _input.PlaceholderText = "Ex.: Luna, organize minha pasta de documentos.";
        _send = new Button { Text = "Enviar para a LUNA", Left = 485, Top = 323, Width = 95, Height = 34 };
        _send.Click += async (_, _) => await SubmitAsync();
        _input.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await SubmitAsync(); } };
        Controls.AddRange([title, info, _conversation, _input, _send]);
        AcceptButton = _send;
        Shown += (_, _) => _input.Focus();
    }

    private async Task SubmitAsync()
    {
        var text = _input.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) || !_send.Enabled) return;
        _conversation.AppendText($"Você: {text}{Environment.NewLine}");
        _input.Clear(); _send.Enabled = false; _input.Enabled = false;
        _conversation.AppendText("LUNA: pensando..." + Environment.NewLine);
        try
        {
            var answer = await _command(text);
            var marker = "LUNA: pensando..." + Environment.NewLine;
            var current = _conversation.Text;
            if (current.EndsWith(marker, StringComparison.Ordinal)) _conversation.Text = current[..^marker.Length];
            _conversation.AppendText($"LUNA: {answer}{Environment.NewLine}{Environment.NewLine}");
            _conversation.SelectionStart = _conversation.TextLength; _conversation.ScrollToCaret();
        }
        catch (Exception ex) { _conversation.AppendText($"LUNA: Não consegui processar sua mensagem agora. {ex.Message}{Environment.NewLine}{Environment.NewLine}"); }
        finally { _send.Enabled = true; _input.Enabled = true; _input.Focus(); }
    }
}
