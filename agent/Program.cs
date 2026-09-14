using System.Drawing;
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
    private readonly LunaCore _core;
    private LunaChatForm? _chat;

    public LunaApplication()
    {
        _core = new LunaCore();
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
        if (_chat is { IsDisposed: false })
        {
            _chat.Activate();
            if (!string.IsNullOrWhiteSpace(initialText)) _chat.SendInitial(initialText);
            return;
        }
        _chat = new LunaChatForm(_core);
        _chat.FormClosed += (_, _) => _chat = null;
        _chat.Show();
        if (!string.IsNullOrWhiteSpace(initialText)) _chat.SendInitial(initialText);
    }

    protected override void ExitThreadCore()
    {
        _chat?.Close();
        _tray.Visible = false;
        _tray.Dispose();
        _core.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed class LunaChatForm : Form
{
    private readonly LunaCore _core;
    private readonly RichTextBox _conversation;
    private readonly TextBox _input;
    private readonly Button _send;
    private readonly Label _status;

    public LunaChatForm(LunaCore core)
    {
        _core = core;
        Text = "LUNA IA";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(700, 520);
        Size = new Size(760, 560);
        Font = new Font("Segoe UI", 10F);

        var title = new Label { Text = "🧠 LUNA IA", Left = 24, Top = 18, Width = 620, Height = 34, Font = new Font("Segoe UI", 18F, FontStyle.Bold) };
        var subtitle = new Label { Text = "Núcleo local • texto • memória local • sem API de nuvem", Left = 27, Top = 52, Width = 620, Height = 24 };
        _status = new Label { Text = "● LUNA IA ativa", Left = 27, Top = 78, Width = 620, Height = 24 };
        _conversation = new RichTextBox { Left = 24, Top = 108, Width = 696, Height = 320, ReadOnly = true, DetectUrls = true, BackColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10.5F) };
        _input = new TextBox { Left = 24, Top = 444, Width = 570, Height = 36, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom, PlaceholderText = "Escreva para a LUNA IA..." };
        _send = new Button { Text = "Enviar", Left = 604, Top = 442, Width = 116, Height = 38, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };

        Controls.AddRange([title, subtitle, _status, _conversation, _input, _send]);
        AcceptButton = _send;
        _send.Click += async (_, _) => await SubmitAsync();
        _input.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; await SubmitAsync(); } };
        Shown += (_, _) => { AppendLuna("Olá. Eu sou a LUNA IA. Este é meu núcleo local inicial. Vamos construir minha inteligência por etapas."); _input.Focus(); };
    }

    public void SendInitial(string text)
    {
        _input.Text = text;
        _ = SubmitAsync();
    }

    private async Task SubmitAsync()
    {
        var text = _input.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) || !_send.Enabled) return;
        AppendUser(text);
        _input.Clear();
        _send.Enabled = false;
        _input.Enabled = false;
        _status.Text = "● LUNA IA processando localmente...";
        try
        {
            var result = await _core.ProcessAsync(text);
            AppendLuna(result.Text);
            _status.Text = result.Executed ? "● Ação local executada" : "● LUNA IA ativa";
        }
        catch (Exception ex)
        {
            AppendLuna("Erro interno controlado: " + ex.Message);
            _status.Text = "● LUNA IA ativa • erro controlado";
        }
        finally
        {
            _send.Enabled = true;
            _input.Enabled = true;
            _input.Focus();
        }
    }

    private void AppendUser(string text)
    {
        _conversation.SelectionColor = Color.DarkBlue;
        _conversation.AppendText("Você: ");
        _conversation.SelectionColor = SystemColors.WindowText;
        _conversation.AppendText(text + Environment.NewLine + Environment.NewLine);
        _conversation.SelectionStart = _conversation.TextLength;
        _conversation.ScrollToCaret();
    }

    private void AppendLuna(string text)
    {
        _conversation.SelectionColor = Color.DarkGreen;
        _conversation.AppendText("LUNA IA: ");
        _conversation.SelectionColor = SystemColors.WindowText;
        _conversation.AppendText(text + Environment.NewLine + Environment.NewLine);
        _conversation.SelectionStart = _conversation.TextLength;
        _conversation.ScrollToCaret();
    }
}
