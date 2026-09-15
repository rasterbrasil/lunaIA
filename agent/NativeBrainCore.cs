using System.Text;
using System.Text.Json;

namespace LunaPC;

/// <summary>
/// Self-contained neural language core. No external model, API, runtime or service.
/// The implementation is deliberately small at first so it can be trained and scaled locally.
/// </summary>
internal sealed class NativeBrainCore
{
    private const int Vocab = 256;
    private readonly int _hidden;
    private readonly float[,] _embedding;
    private readonly float[,] _recurrent;
    private readonly float[,] _output;
    private readonly float[] _bias;
    private readonly object _sync = new();
    private readonly string _weightsPath;
    private bool _trained;

    public NativeBrainCore(string dataDirectory, int hidden = 96)
    {
        _hidden = Math.Clamp(hidden, 32, 512);
        Directory.CreateDirectory(dataDirectory);
        _weightsPath = Path.Combine(dataDirectory, "luna-native-brain.weights.json");
        _embedding = new float[Vocab, _hidden];
        _recurrent = new float[_hidden, _hidden];
        _output = new float[_hidden, Vocab];
        _bias = new float[Vocab];
        Initialize();
        LoadIfPresent();
    }

    public bool IsTrained => _trained;
    public int ParameterCount => Vocab * _hidden + _hidden * _hidden + _hidden * Vocab + Vocab;

    private void Initialize()
    {
        var rng = new Random(1731);
        float Scale(int fan) => MathF.Sqrt(2f / fan);
        for (var i = 0; i < Vocab; i++)
            for (var j = 0; j < _hidden; j++) _embedding[i, j] = (float)(rng.NextDouble() * 2 - 1) * Scale(Vocab);
        for (var i = 0; i < _hidden; i++)
            for (var j = 0; j < _hidden; j++) _recurrent[i, j] = (float)(rng.NextDouble() * 2 - 1) * Scale(_hidden);
        for (var i = 0; i < _hidden; i++)
            for (var j = 0; j < Vocab; j++) _output[i, j] = (float)(rng.NextDouble() * 2 - 1) * Scale(_hidden);
    }

    public void Train(string corpus, int epochs = 2, float learningRate = 0.0035f, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(corpus)) return;
        var bytes = Encoding.UTF8.GetBytes(corpus);
        if (bytes.Length < 8) return;
        lock (_sync)
        {
            const int sequence = 48;
            for (var epoch = 0; epoch < Math.Max(1, epochs); epoch++)
            {
                var hidden = new float[_hidden];
                for (var p = 0; p < bytes.Length - sequence - 1; p += sequence)
                {
                    ct.ThrowIfCancellationRequested();
                    Array.Clear(hidden);
                    var states = new float[sequence + 1, _hidden];
                    for (var t = 0; t < sequence; t++)
                    {
                        var x = bytes[p + t];
                        for (var h = 0; h < _hidden; h++)
                        {
                            var sum = _embedding[x, h];
                            for (var k = 0; k < _hidden; k++) sum += hidden[k] * _recurrent[k, h];
                            hidden[h] = MathF.Tanh(sum);
                            states[t + 1, h] = hidden[h];
                        }
                        var target = bytes[p + t + 1];
                        var probs = Softmax(hidden, target);
                        for (var h = 0; h < _hidden; h++) _output[h, target] += learningRate * (1f - probs[target]) * hidden[h];
                        for (var h = 0; h < _hidden; h++)
                        {
                            var grad = (1f - probs[target]) * _output[h, target];
                            _embedding[x, h] += learningRate * grad * 0.02f;
                        }
                        _bias[target] += learningRate * (1f - probs[target]);
                    }
                }
            }
            _trained = true;
            Save();
        }
    }

    private float[] Softmax(float[] hidden, int target)
    {
        var logits = new float[Vocab];
        var max = float.MinValue;
        for (var v = 0; v < Vocab; v++)
        {
            float s = _bias[v];
            for (var h = 0; h < _hidden; h++) s += hidden[h] * _output[h, v];
            logits[v] = s;
            if (s > max) max = s;
        }
        var sum = 0f;
        for (var v = 0; v < Vocab; v++) { logits[v] = MathF.Exp(Math.Clamp(logits[v] - max, -20, 20)); sum += logits[v]; }
        for (var v = 0; v < Vocab; v++) logits[v] /= Math.Max(sum, 1e-8f);
        return logits;
    }

    public string Generate(string prompt, int maxBytes = 240, float temperature = 0.7f)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;
        lock (_sync)
        {
            var bytes = Encoding.UTF8.GetBytes(prompt);
            var hidden = new float[_hidden];
            foreach (var b in bytes) Step(b, hidden);
            var result = new List<byte>();
            var last = bytes[^1];
            for (var i = 0; i < maxBytes; i++)
            {
                var next = Predict(hidden, temperature);
                if (next == 0 || next == 10) break;
                result.Add((byte)next);
                Step((byte)next, hidden);
                last = (byte)next;
            }
            _ = last;
            return Encoding.UTF8.GetString(result.ToArray()).Trim();
        }
    }

    private void Step(byte input, float[] hidden)
    {
        var previous = (float[])hidden.Clone();
        for (var h = 0; h < _hidden; h++)
        {
            var sum = _embedding[input, h];
            for (var k = 0; k < _hidden; k++) sum += previous[k] * _recurrent[k, h];
            hidden[h] = MathF.Tanh(sum);
        }
    }

    private int Predict(float[] hidden, float temperature)
    {
        var logits = new float[Vocab];
        var max = float.MinValue;
        for (var v = 0; v < Vocab; v++)
        {
            float s = _bias[v];
            for (var h = 0; h < _hidden; h++) s += hidden[h] * _output[h, v];
            logits[v] = s / Math.Max(temperature, 0.05f);
            max = Math.Max(max, logits[v]);
        }
        var sum = 0f;
        for (var v = 0; v < Vocab; v++) { logits[v] = MathF.Exp(Math.Clamp(logits[v] - max, -30, 30)); sum += logits[v]; }
        var r = Random.Shared.NextSingle() * sum;
        for (var v = 0; v < Vocab; v++) { r -= logits[v]; if (r <= 0) return v; }
        return 32;
    }

    private void Save()
    {
        var data = new NativeWeights
        {
            Hidden = _hidden,
            Embedding = Flatten(_embedding),
            Recurrent = Flatten(_recurrent),
            Output = Flatten(_output),
            Bias = _bias,
            Trained = _trained
        };
        File.WriteAllText(_weightsPath, JsonSerializer.Serialize(data));
    }

    private void LoadIfPresent()
    {
        try
        {
            if (!File.Exists(_weightsPath)) return;
            var data = JsonSerializer.Deserialize<NativeWeights>(File.ReadAllText(_weightsPath));
            if (data is null || data.Hidden != _hidden || data.Embedding?.Length != _embedding.Length || data.Recurrent?.Length != _recurrent.Length || data.Output?.Length != _output.Length) return;
            Unflatten(data.Embedding, _embedding); Unflatten(data.Recurrent, _recurrent); Unflatten(data.Output, _output);
            if (data.Bias?.Length == _bias.Length) Array.Copy(data.Bias, _bias, _bias.Length);
            _trained = data.Trained;
        }
        catch { _trained = false; }
    }

    private static float[] Flatten(float[,] m)
    {
        var a = new float[m.Length]; Buffer.BlockCopy(m, 0, a, 0, sizeof(float) * a.Length); return a;
    }
    private static void Unflatten(float[] a, float[,] m) => Buffer.BlockCopy(a, 0, m, 0, sizeof(float) * a.Length);

    private sealed class NativeWeights
    {
        public int Hidden { get; set; }
        public float[]? Embedding { get; set; }
        public float[]? Recurrent { get; set; }
        public float[]? Output { get; set; }
        public float[]? Bias { get; set; }
        public bool Trained { get; set; }
    }
}
