using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LunaPC;

internal sealed class AndroidCommandServer
{
    public const int Port = 38765;
    private readonly TcpListener _listener;
    private readonly string _pairCode;
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public AndroidCommandServer()
    {
        _pairCode = LoadOrCreatePairCode();
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public string PairCode => _pairCode;
    public bool IsConnected => _client?.Connected == true;

    public async Task<string> ExecuteAsync(BrainAction action, CancellationToken ct)
    {
        if (!IsConnected) throw new InvalidOperationException("O LUNA Android ainda não está conectado ao LUNA PC.");
        await _sendLock.WaitAsync(ct);
        try
        {
            var command = new { token = _pairCode, action = action.Type, target = action.Target, value = action.Value, arguments = action.Arguments, x = action.X, y = action.Y };
            await _writer!.WriteLineAsync(JsonSerializer.Serialize(command));
            await _writer.FlushAsync(ct);
            var response = await _reader!.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(response)) throw new IOException("O Android encerrou a conexão.");
            using var json = JsonDocument.Parse(response);
            var ok = json.RootElement.TryGetProperty("ok", out var okElement) && okElement.GetBoolean();
            var message = json.RootElement.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
            if (!ok) throw new InvalidOperationException(message ?? "O Android recusou a ação.");
            return message ?? "Ação executada no Android.";
        }
        finally { _sendLock.Release(); }
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                client.NoDelay = true;
                var reader = new StreamReader(client.GetStream(), Encoding.UTF8, false, 4096, true);
                var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                var hello = await reader.ReadLineAsync();
                if (hello != "LUNA-ANDROID/1|" + _pairCode) { client.Close(); continue; }
                _client?.Close();
                _client = client; _reader = reader; _writer = writer;
                await writer.WriteLineAsync("OK|LUNA-PC/1");
            }
            catch { await Task.Delay(1000); }
        }
    }

    private static string LoadOrCreatePairCode()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaPC");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "android-pair.code");
        if (File.Exists(path)) return File.ReadAllText(path).Trim();
        var code = Random.Shared.Next(100000, 999999).ToString();
        File.WriteAllText(path, code);
        return code;
    }
}
