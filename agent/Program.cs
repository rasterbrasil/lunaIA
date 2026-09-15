using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LunaPC;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new LunaApplication());
    }
}

internal sealed class LunaApplication : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly LunaIntelligentAgent _core;
    private LunaChatForm? _chat;

    public LunaApplication()
    {
        _core = new LunaIntelligentAgent();
        _tray = new NotifyIcon { Icon = SystemIcons.Application, Visible = true, Text = "LUNA IA" };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir LUNA IA", null, (_, _) => ShowChat());
        menu.Items.Add("Testar LUNA IA", null, (_, _) => ShowChat("Luna, quem é você?"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowChat();
        ShowChat();
    }

    private void ShowChat(string? initialText = null)
    {
        if (_chat is { IsDisposed: false }) { _chat.Activate(); if (!string.IsNullOrWhiteSpace(initialText)) _chat.SendInitial(initialText); return; }
        _chat = new LunaChatForm(_core);
        _chat.FormClosed += (_, _) => _chat = null;
        _chat.Show();
        if (!string.IsNullOrWhiteSpace(initialText)) _chat.SendInitial(initialText);
    }

    protected override void ExitThreadCore()
    {
        _chat?.Close(); _tray.Visible = false; _tray.Dispose(); _core.Dispose(); base.ExitThreadCore();
    }
}

internal sealed class LunaChatForm : Form
{
    private readonly LunaIntelligentAgent _core;
    private readonly RichTextBox _conversation;
    private readonly TextBox _input;
    private readonly Button _send;
    private readonly Label _status;
    private readonly Label _brainState;
    private readonly BrainPanel _brain;
    private readonly System.Windows.Forms.Timer _pulse;
    private int _pulseStep;

    private static readonly Color Background = Color.FromArgb(7, 11, 20);
    private static readonly Color Panel = Color.FromArgb(12, 18, 31);
    private static readonly Color Panel2 = Color.FromArgb(16, 24, 41);
    private static readonly Color TextColor = Color.FromArgb(229, 239, 255);
    private static readonly Color Muted = Color.FromArgb(133, 154, 184);
    private static readonly Color Accent = Color.FromArgb(56, 214, 255);
    private static readonly Color Accent2 = Color.FromArgb(113, 91, 255);

    public LunaChatForm(LunaIntelligentAgent core)
    {
        _core = core;
        Text = "LUNA IA"; StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 620); Size = new Size(1040, 700);
        BackColor = Background; ForeColor = TextColor; Font = new Font("Segoe UI", 10F); DoubleBuffered = true;

        var header = new Panel { Left = 24, Top = 20, Width = 992, Height = 82, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, BackColor = Color.Transparent };
        var logo = new BrainLogo { Left = 0, Top = 5, Width = 58, Height = 58, Accent = Accent };
        var title = new Label { Text = "LUNA IA", Left = 70, Top = 3, Width = 420, Height = 36, ForeColor = TextColor, Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold) };
        var subtitle = new Label { Text = "NÚCLEO LOCAL  •  RACIOCÍNIO  •  MEMÓRIA  •  AUTONOMIA", Left = 72, Top = 39, Width = 560, Height = 24, ForeColor = Muted, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold) };
        _status = new Label { Text = "●  LUNA IA ONLINE", Left = 760, Top = 13, Width = 220, Height = 25, ForeColor = Accent, Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _brainState = new Label { Text = "CÉREBRO LOCAL ATIVO", Left = 700, Top = 40, Width = 280, Height = 22, ForeColor = Muted, Font = new Font("Segoe UI", 8F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        header.Controls.AddRange([logo, title, subtitle, _status, _brainState]);

        _conversation = new RichTextBox { Left = 24, Top = 116, Width = 720, Height = 484, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, ReadOnly = true, DetectUrls = true, BorderStyle = BorderStyle.None, BackColor = Panel, ForeColor = TextColor, Font = new Font("Segoe UI", 10.5F), Padding = new Padding(18), ScrollBars = RichTextBoxScrollBars.Vertical };
        _brain = new BrainPanel { Left = 762, Top = 116, Width = 254, Height = 250, Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = Panel };
        var brainTitle = new Label { Text = "NÚCLEO COGNITIVO", Left = 780, Top = 380, Width = 220, Height = 24, ForeColor = Accent, Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        var brainInfo = new Label { Text = "OBSERVAR\nINTERPRETAR\nDECIDIR\nEXECUTAR\nVERIFICAR\nRECUPERAR", Left = 780, Top = 410, Width = 220, Height = 120, ForeColor = Muted, Font = new Font("Consolas", 9F), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        var memoryInfo = new Label { Text = "MEMÓRIA LOCAL\nContexto persistente ativo", Left = 780, Top = 535, Width = 220, Height = 52, ForeColor = TextColor, Font = new Font("Segoe UI", 8.5F), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        var inputPanel = new Panel { Left = 24, Top = 616, Width = 992, Height = 56, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom, BackColor = Panel2 };
        _input = new TextBox { Left = 14, Top = 10, Width = 824, Height = 36, BorderStyle = BorderStyle.None, BackColor = Panel2, ForeColor = TextColor, Font = new Font("Segoe UI", 10.5F), PlaceholderText = "Fale com a LUNA... diga o objetivo, não apenas o comando.", Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _send = new Button { Text = "➤", Left = 848, Top = 8, Width = 130, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.FromArgb(4, 10, 18), Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold), Anchor = AnchorStyles.Right };
        _send.FlatAppearance.BorderSize = 0; inputPanel.Controls.AddRange([_input, _send]);
        Controls.AddRange([header, _conversation, _brain, brainTitle, brainInfo, memoryInfo, inputPanel]);
        AcceptButton = _send;
        _send.Click += async (_, _) => await SubmitAsync();
        _input.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; await SubmitAsync(); } };
        Shown += (_, _) => { AppendLuna("Olá. Eu sou a LUNA IA.\n\nMeu núcleo local está ativo. Eu observo, interpreto, decido, executo e verifico."); _input.Focus(); };
        _pulse = new System.Windows.Forms.Timer { Interval = 90 };
        _pulse.Tick += (_, _) => { _pulseStep = (_pulseStep + 1) % 100; _brain.Pulse = _pulseStep; _brain.Invalidate(); };
        _pulse.Start();
    }

    public void SendInitial(string text) { _input.Text = text; _ = SubmitAsync(); }

    private async Task SubmitAsync()
    {
        var text = _input.Text.Trim(); if (string.IsNullOrWhiteSpace(text) || !_send.Enabled) return;
        AppendUser(text); _input.Clear(); _send.Enabled = false; _input.Enabled = false;
        _status.Text = "●  LUNA IA RACIOCINANDO"; _status.ForeColor = Accent; _brainState.Text = "OBSERVANDO • INTERPRETANDO • DECIDINDO"; _brain.Active = true;
        try { var result = await _core.ProcessAsync(text); AppendLuna(result.Text); _status.Text = result.Executed ? "●  AÇÃO VERIFICADA" : "●  LUNA IA ONLINE"; _brainState.Text = result.Executed ? "EXECUÇÃO + VERIFICAÇÃO CONCLUÍDAS" : "AGUARDANDO PRÓXIMO OBJETIVO"; }
        catch (Exception ex) { AppendLuna("Erro interno controlado: " + ex.Message); _status.Text = "●  ERRO CONTROLADO"; _status.ForeColor = Color.Orange; }
        finally { _brain.Active = false; _send.Enabled = true; _input.Enabled = true; _input.Focus(); }
    }

    private void AppendUser(string text) { _conversation.SelectionColor = Accent2; _conversation.SelectionFont = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold); _conversation.AppendText("VOCÊ\n"); _conversation.SelectionColor = TextColor; _conversation.SelectionFont = new Font("Segoe UI", 10.5F); _conversation.AppendText(text + "\n\n"); _conversation.SelectionStart = _conversation.TextLength; _conversation.ScrollToCaret(); }
    private void AppendLuna(string text) { _conversation.SelectionColor = Accent; _conversation.SelectionFont = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold); _conversation.AppendText("LUNA IA\n"); _conversation.SelectionColor = TextColor; _conversation.SelectionFont = new Font("Segoe UI", 10.5F); _conversation.AppendText(text + "\n\n"); _conversation.SelectionStart = _conversation.TextLength; _conversation.ScrollToCaret(); }
    protected override void OnFormClosed(FormClosedEventArgs e) { _pulse.Stop(); _pulse.Dispose(); base.OnFormClosed(e); }
}

internal sealed class BrainLogo : Control
{
    public Color Accent { get; set; } = Color.Cyan;
    public BrainLogo() => DoubleBuffered = true;
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var pen = new Pen(Accent, 2.2F); var r = new Rectangle(8, 9, Width - 16, Height - 18); e.Graphics.DrawEllipse(pen, r); using var node = new SolidBrush(Accent); e.Graphics.FillEllipse(node, Width / 2 - 4, Height / 2 - 4, 8, 8); e.Graphics.DrawLine(pen, Width / 2, 14, Width / 2, Height - 14); }
}

internal sealed class BrainPanel : Panel
{
    public int Pulse { get; set; }
    public bool Active { get; set; }
    public BrainPanel() => DoubleBuffered = true;
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var cx = Width / 2; var cy = Height / 2; var radius = Math.Min(Width, Height) / 2 - 38;
        using var ring = new Pen(Color.FromArgb(35, 214, 255), 1.2F); e.Graphics.DrawEllipse(ring, cx - radius, cy - radius, radius * 2, radius * 2); e.Graphics.DrawEllipse(ring, cx - radius + 18, cy - radius + 18, (radius - 18) * 2, (radius - 18) * 2);
        var nodes = new[] { (x: cx, y: cy - radius + 6), (x: cx + radius - 6, y: cy), (x: cx, y: cy + radius - 6), (x: cx - radius + 6, y: cy), (x: cx + 45, y: cy - 38), (x: cx - 42, y: cy + 34) };
        using var line = new Pen(Color.FromArgb(45, 113, 91, 255), 1.1F); foreach (var n in nodes) e.Graphics.DrawLine(line, cx, cy, n.x, n.y);
        var glow = Active ? 8 + (Pulse % 10) : 5; using var core = new SolidBrush(Color.FromArgb(56, 214, 255)); e.Graphics.FillEllipse(core, cx - glow, cy - glow, glow * 2, glow * 2);
        using var dot = new SolidBrush(Color.FromArgb(113, 91, 255)); foreach (var n in nodes) e.Graphics.FillEllipse(dot, n.x - 4, n.y - 4, 8, 8);
        using var label = new SolidBrush(Color.FromArgb(133, 154, 184)); using var font = new Font("Consolas", 8F, FontStyle.Bold); var text = Active ? "THINKING" : "READY"; e.Graphics.DrawString(text, font, label, cx - 27, Height - 27);
    }
}
