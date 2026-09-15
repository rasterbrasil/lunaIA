using System.Text;
using System.Text.Json;

namespace LunaPC;

/// <summary>
/// From-scratch decoder-style Transformer core.
/// No external model, weights, runtime or AI service is used.
/// The current training stage learns the output head locally from the seed corpus;
/// the Transformer stack itself is fully native and ready for progressive full-weight training.
/// </summary>
internal sealed class NativeTransformerBrain
{
    private const int Vocab = 128;
    private const int ModelWidth = 128;
    private const int Heads = 4;
    private const int Layers = 4;
    private const int FeedForward = 512;
    private const int MaxSequence = 128;
    private readonly object _sync = new();
    private readonly string _weightsPath;
    private readonly float[,] _tokenEmbedding = new float[Vocab, ModelWidth];
    private readonly float[,] _positionEmbedding = new float[MaxSequence, ModelWidth];
    private readonly LayerWeights[] _layers = Enumerable.Range(0, Layers).Select(_ => new LayerWeights()).ToArray();
    private readonly float[,] _output = new float[ModelWidth, Vocab];
    private readonly float[] _bias = new float[Vocab];
    private readonly Random _random = new(20260915);
    private bool _trained;

    // Deliberately fixed, compact Unicode-aware character vocabulary.
    // Unknown characters are normalized to a space so generation can never emit invalid UTF-8 bytes.
    private const string Vocabulary = " abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZáàâãéêíóôõúçÁÀÂÃÉÊÍÓÔÕÚÇ0123456789.,!?;:-_()[]{}'/\\\"@#$%&*+=<>|\n\r";

    public NativeTransformerBrain(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _weightsPath = Path.Combine(dataDirectory, "luna-transformer.weights.json");
        Initialize();
        LoadIfPresent();
    }

    public bool IsTrained => _trained;
    public int ParameterCount =>
        Vocab * ModelWidth + MaxSequence * ModelWidth +
        Layers * (4 * ModelWidth * ModelWidth + 2 * ModelWidth * FeedForward + 4 * ModelWidth) +
        ModelWidth * Vocab + Vocab;

    private void Initialize()
    {
        var scale = MathF.Sqrt(2f / ModelWidth);
        for (var i = 0; i < Vocab; i++)
            for (var j = 0; j < ModelWidth; j++)
                _tokenEmbedding[i, j] = NextWeight(scale);

        for (var p = 0; p < MaxSequence; p++)
            for (var j = 0; j < ModelWidth; j++)
                _positionEmbedding[p, j] = MathF.Sin(p / MathF.Pow(10000f, (2f * (j / 2)) / ModelWidth));

        foreach (var layer in _layers)
        {
            Fill(layer.Q, scale); Fill(layer.K, scale); Fill(layer.V, scale); Fill(layer.O, scale);
            Fill(layer.F1, scale); Fill(layer.F2, scale);
            Array.Fill(layer.Norm1, 1f); Array.Fill(layer.Norm2, 1f);
        }

        for (var i = 0; i < ModelWidth; i++)
            for (var j = 0; j < Vocab; j++)
                _output[i, j] = NextWeight(0.02f);
    }

    public void Train(string corpus, int epochs = 3, float learningRate = 0.01f, CancellationToken ct = default)
    {
        var ids = Encode(corpus);
        if (ids.Length < 16) return;

        lock (_sync)
        {
            // Local teacher-forcing stage. It trains the Transformer output head from scratch,
            // while preserving the complete decoder stack for the next full-backprop stage.
            var context = Math.Min(96, MaxSequence);
            var step = Math.Max(1, context / 4);
            for (var epoch = 0; epoch < Math.Max(1, epochs); epoch++)
            {
                for (var end = context; end < ids.Length; end += step)
                {
                    ct.ThrowIfCancellationRequested();
                    var start = Math.Max(0, end - context);
                    var length = end - start;
                    var input = new int[length];
                    Array.Copy(ids, start, input, 0, length);
                    var hidden = ForwardLast(input);
                    var probs = Softmax(hidden, 0.9f);
                    for (var v = 0; v < Vocab; v++)
                    {
                        var grad = probs[v] - (v == ids[end] ? 1f : 0f);
                        for (var h = 0; h < ModelWidth; h++)
                            _output[h, v] -= learningRate * grad * hidden[h];
                        _bias[v] -= learningRate * grad;
                    }
                }
            }
            _trained = true;
            Save();
        }
    }

    public string Generate(string prompt, int maxCharacters = 220, float temperature = 0.65f)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;
        lock (_sync)
        {
            var ids = Encode(prompt);
            var generated = new List<int>();
            for (var i = 0; i < Math.Max(1, maxCharacters); i++)
            {
                var context = ids.Concat(generated).TakeLast(MaxSequence).ToArray();
                var hidden = ForwardLast(context);
                var next = Sample(Softmax(hidden, temperature));
                if (next == IndexOf('\n') || next == IndexOf('\r')) break;
                generated.Add(next);
            }
            return Decode(generated);
        }
    }

    private float[] ForwardLast(int[] ids)
    {
        if (ids.Length == 0) return new float[ModelWidth];
        var length = Math.Min(ids.Length, MaxSequence);
        var x = new float[length, ModelWidth];
        var offset = ids.Length - length;

        for (var t = 0; t < length; t++)
        {
            var id = ids[offset + t];
            for (var h = 0; h < ModelWidth; h++)
                x[t, h] = _tokenEmbedding[id, h] + _positionEmbedding[t, h];
        }

        foreach (var layer in _layers)
        {
            var norm = new float[length, ModelWidth];
            for (var t = 0; t < length; t++) LayerNorm(x, t, layer.Norm1, norm, t);

            var attended = new float[length, ModelWidth];
            var headWidth = ModelWidth / Heads;
            for (var t = 0; t < length; t++)
            {
                for (var head = 0; head < Heads; head++)
                {
                    var scores = new float[t + 1];
                    var max = float.MinValue;
                    for (var s = 0; s <= t; s++)
                    {
                        var dot = 0f;
                        for (var k = 0; k < headWidth; k++)
                        {
                            var q = 0f; var key = 0f;
                            for (var d = 0; d < ModelWidth; d++)
                            {
                                q += norm[t, d] * layer.Q[d, head * headWidth + k];
                                key += norm[s, d] * layer.K[d, head * headWidth + k];
                            }
                            dot += q * key;
                        }
                        scores[s] = dot / MathF.Sqrt(headWidth);
                        max = MathF.Max(max, scores[s]);
                    }
                    var sum = 0f;
                    for (var s = 0; s <= t; s++) { scores[s] = MathF.Exp(Math.Clamp(scores[s] - max, -20, 20)); sum += scores[s]; }
                    for (var s = 0; s <= t; s++)
                    {
                        var weight = scores[s] / Math.Max(sum, 1e-8f);
                        for (var k = 0; k < headWidth; k++)
                        {
                            var value = 0f;
                            for (var d = 0; d < ModelWidth; d++) value += norm[s, d] * layer.V[d, head * headWidth + k];
                            attended[t, head * headWidth + k] += weight * value;
                        }
                    }
                }
            }

            var residual = new float[length, ModelWidth];
            for (var t = 0; t < length; t++)
                for (var h = 0; h < ModelWidth; h++)
                {
                    var projected = 0f;
                    for (var k = 0; k < ModelWidth; k++) projected += attended[t, k] * layer.O[k, h];
                    residual[t, h] = x[t, h] + projected;
                }

            var norm2 = new float[length, ModelWidth];
            for (var t = 0; t < length; t++) LayerNorm(residual, t, layer.Norm2, norm2, t);
            for (var t = 0; t < length; t++)
                for (var h = 0; h < ModelWidth; h++)
                {
                    var a = 0f;
                    for (var k = 0; k < ModelWidth; k++) a += norm2[t, k] * layer.F1[k, h];
                    var gelu = 0.5f * a * (1f + MathF.Tanh(0.79788456f * (a + 0.044715f * a * a * a)));
                    var b = 0f;
                    for (var k = 0; k < FeedForward; k++) b += (k == h ? gelu : 0f) * layer.F2[k, h];
                    x[t, h] = residual[t, h] + b;
                }
        }

        var result = new float[ModelWidth];
        for (var h = 0; h < ModelWidth; h++) result[h] = x[length - 1, h];
        return result;
    }

    private static void LayerNorm(float[,] source, int row, float[] gamma, float[,] target, int targetRow)
    {
        var mean = 0f;
        for (var h = 0; h < ModelWidth; h++) mean += source[row, h];
        mean /= ModelWidth;
        var variance = 0f;
        for (var h = 0; h < ModelWidth; h++) { var d = source[row, h] - mean; variance += d * d; }
        variance /= ModelWidth;
        var inv = 1f / MathF.Sqrt(variance + 1e-5f);
        for (var h = 0; h < ModelWidth; h++) target[targetRow, h] = (source[row, h] - mean) * inv * gamma[h];
    }

    private float[] Softmax(float[] hidden, float temperature)
    {
        var logits = new float[Vocab];
        var max = float.MinValue;
        for (var v = 0; v < Vocab; v++)
        {
            var value = _bias[v];
            for (var h = 0; h < ModelWidth; h++) value += hidden[h] * _output[h, v];
            logits[v] = value / Math.Max(0.05f, temperature);
            max = MathF.Max(max, logits[v]);
        }
        var sum = 0f;
        for (var v = 0; v < Vocab; v++) { logits[v] = MathF.Exp(Math.Clamp(logits[v] - max, -30, 30)); sum += logits[v]; }
        for (var v = 0; v < Vocab; v++) logits[v] /= Math.Max(sum, 1e-8f);
        return logits;
    }

    private int Sample(float[] probabilities)
    {
        var r = _random.NextSingle();
        for (var i = 0; i < probabilities.Length; i++) { r -= probabilities[i]; if (r <= 0) return i; }
        return IndexOf(' ');
    }

    private static int[] Encode(string text)
    {
        return text.Select(c => IndexOf(c)).ToArray();
    }

    private static string Decode(IEnumerable<int> ids)
    {
        var sb = new StringBuilder();
        foreach (var id in ids)
        {
            var safe = Math.Clamp(id, 0, Vocab - 1);
            var c = Vocabulary[safe];
            if (c == '\0') continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    private static int IndexOf(char c)
    {
        var index = Vocabulary.IndexOf(c);
        return index >= 0 && index < Vocab ? index : 0;
    }

    private float NextWeight(float scale) => (float)(_random.NextDouble() * 2d - 1d) * scale;

    private void Fill(float[,] matrix, float scale)
    {
        for (var i = 0; i < matrix.GetLength(0); i++)
            for (var j = 0; j < matrix.GetLength(1); j++) matrix[i, j] = NextWeight(scale);
    }

    private void Save()
    {
        var data = new TransformerWeights
        {
            Trained = _trained,
            TokenEmbedding = Flatten(_tokenEmbedding),
            Output = Flatten(_output),
            Bias = _bias
        };
        File.WriteAllText(_weightsPath, JsonSerializer.Serialize(data));
    }

    private void LoadIfPresent()
    {
        try
        {
            if (!File.Exists(_weightsPath)) return;
            var data = JsonSerializer.Deserialize<TransformerWeights>(File.ReadAllText(_weightsPath));
            if (data?.TokenEmbedding?.Length != _tokenEmbedding.Length || data.Output?.Length != _output.Length || data.Bias?.Length != _bias.Length) return;
            Unflatten(data.TokenEmbedding, _tokenEmbedding);
            Unflatten(data.Output, _output);
            Array.Copy(data.Bias, _bias, _bias.Length);
            _trained = data.Trained;
        }
        catch { _trained = false; }
    }

    private static float[] Flatten(float[,] matrix)
    {
        var result = new float[matrix.Length];
        var n = 0;
        for (var i = 0; i < matrix.GetLength(0); i++)
            for (var j = 0; j < matrix.GetLength(1); j++) result[n++] = matrix[i, j];
        return result;
    }

    private static void Unflatten(float[] source, float[,] matrix)
    {
        var n = 0;
        for (var i = 0; i < matrix.GetLength(0); i++)
            for (var j = 0; j < matrix.GetLength(1); j++) matrix[i, j] = source[n++];
    }

    private sealed class LayerWeights
    {
        public float[,] Q { get; } = new float[ModelWidth, ModelWidth];
        public float[,] K { get; } = new float[ModelWidth, ModelWidth];
        public float[,] V { get; } = new float[ModelWidth, ModelWidth];
        public float[,] O { get; } = new float[ModelWidth, ModelWidth];
        public float[,] F1 { get; } = new float[ModelWidth, FeedForward];
        public float[,] F2 { get; } = new float[FeedForward, ModelWidth];
        public float[] Norm1 { get; } = new float[ModelWidth];
        public float[] Norm2 { get; } = new float[ModelWidth];
    }

    private sealed class TransformerWeights
    {
        public bool Trained { get; set; }
        public float[]? TokenEmbedding { get; set; }
        public float[]? Output { get; set; }
        public float[]? Bias { get; set; }
    }
}
