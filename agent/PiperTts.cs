using System.Diagnostics;
using System.Media;
using System.Text;

namespace LunaPC;

internal sealed class PiperTts : IDisposable
{
    private const string VoiceFileName = "dii_pt-BR.onnx";
    private readonly string _baseDirectory;
    private readonly string _piperExecutable;
    private readonly string _modelPath;
    private readonly string _audioDirectory;
    private readonly object _lock = new();
    private Process? _currentProcess;
    private SoundPlayer? _player;
    private bool _disposed;

    public bool IsReady => File.Exists(_piperExecutable) && File.Exists(_modelPath);

    public PiperTts()
    {
        _baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaPC", "tts");
        _piperExecutable = Path.Combine(_baseDirectory, "piper", "piper.exe");
        _modelPath = Path.Combine(_baseDirectory, "voices", VoiceFileName);
        _audioDirectory = Path.Combine(_baseDirectory, "audio");
        Directory.CreateDirectory(_audioDirectory);
    }

    public void Speak(string text)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text) || !IsReady) return;
        _ = Task.Run(() => SynthesizeAndPlayAsync(text));
    }

    private async Task SynthesizeAndPlayAsync(string text)
    {
        string output = Path.Combine(_audioDirectory, $"luna-{Guid.NewGuid():N}.wav");
        try
        {
            lock (_lock) StopCurrentPlayback();

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _piperExecutable,
                Arguments = $"--model \"{_modelPath}\" --output_file \"{output}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = Encoding.UTF8
            };

            lock (_lock) _currentProcess = process;
            process.Start();
            await process.StandardInput.WriteAsync(text);
            process.StandardInput.Close();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 || !File.Exists(output)) return;

            lock (_lock)
            {
                if (_disposed) return;
                _player = new SoundPlayer(output);
                _player.Load();
                _player.PlaySync();
            }
        }
        catch { }
        finally
        {
            lock (_lock)
            {
                if (_currentProcess is null || _currentProcess.HasExited)
                    _currentProcess = null;
            }
            try { if (File.Exists(output)) File.Delete(output); } catch { }
        }
    }

    public void Stop()
    {
        lock (_lock) StopCurrentPlayback();
    }

    private void StopCurrentPlayback()
    {
        try
        {
            if (_currentProcess is { HasExited: false }) _currentProcess.Kill(true);
        }
        catch { }
        try { _player?.Stop(); } catch { }
        _player?.Dispose();
        _player = null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            StopCurrentPlayback();
        }
    }
}
