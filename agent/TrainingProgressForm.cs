namespace LunaPC;

internal sealed class TrainingProgressForm : Form
{
    private readonly Label _status;
    private readonly Label _stats;
    private readonly ProgressBar _progress;
    private readonly TextBox _log;

    public TrainingProgressForm()
    {
        Text = "LUNA PC — Treinamento do cérebro";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 720;
        Height = 520;
        MinimumSize = new Size(650, 450);

        var title = new Label
        {
            Text = "🧠 LUNA — TREINAMENTO EM ANDAMENTO",
            Left = 24, Top = 20, Width = 640, Height = 32,
            Font = new Font("Segoe UI", 16, FontStyle.Bold)
        };
        var source = new Label
        {
            Text = "Fonte: Wikipédia em português (dados públicos licenciados) • IA externa: NÃO",
            Left = 25, Top = 58, Width = 640, Height = 25
        };
        _status = new Label
        {
            Text = "Preparando...",
            Left = 25, Top = 95, Width = 640, Height = 30,
            Font = new Font("Segoe UI", 11, FontStyle.Bold)
        };
        _progress = new ProgressBar
        {
            Left = 25, Top = 132, Width = 640, Height = 25,
            Minimum = 0, Maximum = 100, Value = 0,
            Style = ProgressBarStyle.Continuous
        };
        _stats = new Label
        {
            Text = "Documentos: 0 • Blocos treinados: 0",
            Left = 25, Top = 165, Width = 640, Height = 25
        };
        _log = new TextBox
        {
            Left = 25, Top = 200, Width = 640, Height = 235,
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9), BackColor = SystemColors.Window
        };

        Controls.AddRange([title, source, _status, _progress, _stats, _log]);
    }

    public void Report(TrainingProgress progress)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => Report(progress)));
            return;
        }

        _status.Text = progress.Status;
        _progress.Value = Math.Clamp(progress.Percent, 0, 100);
        _stats.Text = $"Documentos: {progress.Documents} • Blocos treinados: {progress.TrainingChunks} • Etapa: {progress.Step}/{progress.TotalSteps}";
        if (!string.IsNullOrWhiteSpace(progress.LogLine))
        {
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {progress.LogLine}{Environment.NewLine}");
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
    }
}

internal readonly record struct TrainingProgress(
    string Status,
    int Percent,
    int Documents,
    int TrainingChunks,
    int Step,
    int TotalSteps,
    string LogLine);
