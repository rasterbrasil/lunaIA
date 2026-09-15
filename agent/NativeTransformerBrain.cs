using System.Text;
using System.Text.Json;

namespace LunaPC;

/// <summary>
/// Native decoder-style Transformer implemented from scratch in C#.
/// No external model, weights, runtime or AI service is used.
/// This version performs real reverse-mode backpropagation through the
/// Transformer stack and updates all trainable weights with AdamW.
/// </summary>
internal sealed class NativeTransformerBrain
{
    private const string Vocabulary = " abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZáàâãéêíóôõúçÁÀÂÃÉÊÍÓÔÕÚÇ0123456789.,!?;:-_()[]{}'/\\\"@#$%&*+=<>|\n\r";
    private const int Vocab = 118;
    private const int ModelWidth = 128;
    private const int Heads = 4;
    private const int Layers = 4;
    private const int FeedForward = 512;
    private const int MaxSequence = 128;
    private const float Epsilon = 1e-5f;

    private readonly object _sync = new();
    private readonly string _weightsPath;
    private readonly float[,] _tokenEmbedding = new float[Vocab, ModelWidth];
    private readonly float[,] _positionEmbedding = new float[MaxSequence, ModelWidth];
    private readonly LayerWeights[] _layers = Enumerable.Range(0, Layers).Select(_ => new LayerWeights()).ToArray();
    private readonly float[,] _output = new float[ModelWidth, Vocab];
    private readonly float[] _bias = new float[Vocab];
    private readonly AdamState _adam;
    private readonly Random _random = new(20260915);
    private bool _trained;
    private long _optimizerStep;

    private readonly float[,] _outputGrad = new float[ModelWidth, Vocab];
    private readonly float[] _biasGrad = new float[Vocab];
    private readonly float[,] _tokenGrad = new float[Vocab, ModelWidth];

    public NativeTransformerBrain(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _weightsPath = Path.Combine(dataDirectory, "luna-transformer.weights.json");
        _adam = new AdamState();
        Initialize();
        LoadIfPresent();
    }

    public bool IsTrained => _trained;
    public int ParameterCount =>
        Vocab * ModelWidth +
        Layers * (4 * ModelWidth * ModelWidth + 2 * ModelWidth * FeedForward + 2 * ModelWidth) +
        ModelWidth * Vocab + Vocab;

    private void Initialize()
    {
        var scale = MathF.Sqrt(2f / ModelWidth);
        for (var i = 0; i < Vocab; i++)
            for (var j = 0; j < ModelWidth; j++) _tokenEmbedding[i, j] = NextWeight(scale);

        for (var p = 0; p < MaxSequence; p++)
            for (var j = 0; j < ModelWidth; j++)
            {
                var angle = p / MathF.Pow(10000f, (2f * (j / 2)) / ModelWidth);
                _positionEmbedding[p, j] = (j % 2 == 0) ? MathF.Sin(angle) : MathF.Cos(angle);
            }

        foreach (var layer in _layers)
        {
            Fill(layer.Q, scale); Fill(layer.K, scale); Fill(layer.V, scale); Fill(layer.O, scale);
            Fill(layer.F1, scale); Fill(layer.F2, scale);
            Array.Fill(layer.Norm1, 1f); Array.Fill(layer.Norm2, 1f);
        }

        for (var i = 0; i < ModelWidth; i++)
            for (var j = 0; j < Vocab; j++) _output[i, j] = NextWeight(0.02f);
    }

    public void Train(string corpus, int epochs = 1, float learningRate = 0.0008f, CancellationToken ct = default)
    {
        var ids = Encode(corpus);
        if (ids.Length < 16) return;

        lock (_sync)
        {
            const int context = 32;
            const int maxStepsPerEpoch = 16;
            var steps = 0;

            for (var epoch = 0; epoch < Math.Max(1, epochs); epoch++)
            {
                for (var end = context; end < ids.Length && steps < maxStepsPerEpoch; end += context)
                {
                    ct.ThrowIfCancellationRequested();
                    var start = Math.Max(0, end - context);
                    var length = end - start;
                    var input = new int[length];
                    Array.Copy(ids, start, input, 0, length);
                    var target = ids[end];
                    var cache = Forward(input, training: true);
                    Backward(cache, target, learningRate);
                    steps++;
                }
                if (steps >= maxStepsPerEpoch) break;
            }

            _trained = steps > 0;
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
                var cache = Forward(context, training: false);
                var next = Sample(Softmax(cache.LastHidden, temperature));
                if (next == IndexOf('\n') || next == IndexOf('\r')) break;
                generated.Add(next);
            }
            return Decode(generated);
        }
    }

    private ForwardCache Forward(int[] ids, bool training)
    {
        var length = Math.Min(ids.Length, MaxSequence);
        if (length == 0) return new ForwardCache(0);
        var offset = ids.Length - length;
        var cache = new ForwardCache(length);
        for (var t = 0; t < length; t++)
        {
            var id = ids[offset + t];
            cache.InputIds[t] = id;
            for (var h = 0; h < ModelWidth; h++) cache.X[t, h] = _tokenEmbedding[id, h] + _positionEmbedding[t, h];
        }

        var x = cache.X;
        for (var li = 0; li < Layers; li++)
        {
            var layer = _layers[li];
            var lc = cache.Layers[li];
            Copy(x, lc.X0);
            for (var t = 0; t < length; t++) LayerNorm(x, t, layer.Norm1, lc.Norm1, t);
            Multiply(lc.Norm1, layer.Q, lc.Q);
            Multiply(lc.Norm1, layer.K, lc.K);
            Multiply(lc.Norm1, layer.V, lc.V);

            var headWidth = ModelWidth / Heads;
            for (var t = 0; t < length; t++)
                for (var head = 0; head < Heads; head++)
                {
                    var baseIndex = head * headWidth;
                    var scores = new float[t + 1];
                    var max = float.NegativeInfinity;
                    for (var s = 0; s <= t; s++)
                    {
                        var dot = 0f;
                        for (var d = 0; d < headWidth; d++) dot += lc.Q[t, baseIndex + d] * lc.K[s, baseIndex + d];
                        scores[s] = dot / MathF.Sqrt(headWidth);
                        max = MathF.Max(max, scores[s]);
                    }
                    var sum = 0f;
                    for (var s = 0; s <= t; s++) { scores[s] = MathF.Exp(Math.Clamp(scores[s] - max, -30, 30)); sum += scores[s]; }
                    for (var s = 0; s <= t; s++)
                    {
                        var w = scores[s] / Math.Max(sum, 1e-8f);
                        lc.Attention[t, head, s] = w;
                        for (var d = 0; d < headWidth; d++) lc.Attended[t, baseIndex + d] += w * lc.V[s, baseIndex + d];
                    }
                }

            Multiply(lc.Attended, layer.O, lc.Projected);
            for (var t = 0; t < length; t++) for (var h = 0; h < ModelWidth; h++) lc.X1[t, h] = x[t, h] + lc.Projected[t, h];
            for (var t = 0; t < length; t++) LayerNorm(lc.X1, t, layer.Norm2, lc.Norm2, t);
            Multiply(lc.Norm2, layer.F1, lc.F1Pre);
            for (var t = 0; t < length; t++) for (var j = 0; j < FeedForward; j++) lc.F1Act[t, j] = Gelu(lc.F1Pre[t, j]);
            Multiply(lc.F1Act, layer.F2, lc.F2Out);
            for (var t = 0; t < length; t++) for (var h = 0; h < ModelWidth; h++) x[t, h] = lc.X1[t, h] + lc.F2Out[t, h];
        }

        for (var h = 0; h < ModelWidth; h++) cache.LastHidden[h] = x[length - 1, h];
        return cache;
    }

    private void Backward(ForwardCache cache, int target, float learningRate)
    {
        if (cache.Length == 0) return;
        var length = cache.Length;
        Zero(_outputGrad); Zero(_biasGrad); Zero(_tokenGrad);
        foreach (var l in _layers) { Zero(l.GQ); Zero(l.GK); Zero(l.GV); Zero(l.GO); Zero(l.GF1); Zero(l.GF2); Zero(l.GNorm1); Zero(l.GNorm2); }

        var dX = new float[length, ModelWidth];
        var probs = Softmax(cache.LastHidden, 1f);
        var dLogits = new float[Vocab];
        for (var v = 0; v < Vocab; v++) dLogits[v] = probs[v] - (v == target ? 1f : 0f);

        for (var h = 0; h < ModelWidth; h++)
            for (var v = 0; v < Vocab; v++) _outputGrad[h, v] = cache.LastHidden[h] * dLogits[v];
        for (var v = 0; v < Vocab; v++) _biasGrad[v] = dLogits[v];
        for (var h = 0; h < ModelWidth; h++)
            for (var v = 0; v < Vocab; v++) dX[length - 1, h] += dLogits[v] * _output[h, v];

        for (var li = Layers - 1; li >= 0; li--)
        {
            var layer = _layers[li];
            var lc = cache.Layers[li];
            var g = lc.Grad;
            Zero(g);

            // x2 = x1 + FFN(x1)
            for (var t = 0; t < length; t++) for (var h = 0; h < ModelWidth; h++)
            {
                g.DX1[t, h] += dX[t, h];
                g.DF2Out[t, h] = dX[t, h];
            }

            // FFN: F2Out = F1Act * F2
            AddMatMulGrad(lc.F1Act, g.DF2Out, layer.F2, g.DF2, g.DF1Act);
            for (var t = 0; t < length; t++) for (var j = 0; j < FeedForward; j++)
                g.DF1Pre[t, j] = g.DF1Act[t, j] * GeluDerivative(lc.F1Pre[t, j]);
            AddMatMulGrad(lc.Norm2, g.DF1Pre, layer.F1, g.DF1, g.DNorm2);
            BackwardLayerNorm(lc.X1, lc.Norm2, layer.Norm2, g.DNorm2, g.DX1, g.DNorm2Gamma);

            // x1 = x0 + Attention(x0)
            AddMatMulGrad(lc.Attended, g.DX1, layer.O, g.DO, g.DAttended);
            for (var t = 0; t < length; t++) for (var h = 0; h < ModelWidth; h++) g.DX0[t, h] = g.DX1[t, h];

            // Causal multi-head attention backward.
            var headWidth = ModelWidth / Heads;
            for (var t = 0; t < length; t++)
                for (var head = 0; head < Heads; head++)
                {
                    var baseIndex = head * headWidth;
                    var dWeights = new float[t + 1];
                    for (var s = 0; s <= t; s++)
                    {
                        var dot = 0f;
                        for (var d = 0; d < headWidth; d++)
                        {
                            var da = g.DAttended[t, baseIndex + d];
                            dot += da * lc.V[s, baseIndex + d];
                            g.DV[s, baseIndex + d] += lc.Attention[t, head, s] * da;
                        }
                        dWeights[s] = dot;
                    }
                    var weighted = 0f;
                    for (var s = 0; s <= t; s++) weighted += dWeights[s] * lc.Attention[t, head, s];
                    for (var s = 0; s <= t; s++)
                    {
                        var dScore = lc.Attention[t, head, s] * (dWeights[s] - weighted) / MathF.Sqrt(headWidth);
                        for (var d = 0; d < headWidth; d++)
                        {
                            g.DQ[t, baseIndex + d] += dScore * lc.K[s, baseIndex + d];
                            g.DK[s, baseIndex + d] += dScore * lc.Q[t, baseIndex + d];
                        }
                    }
                }

            AddMatMulGrad(lc.Norm1, g.DQ, layer.Q, g.DQW, g.DNorm1FromQ);
            AddMatMulGrad(lc.Norm1, g.DK, layer.K, g.DKW, g.DNorm1FromK);
            AddMatMulGrad(lc.Norm1, g.DV, layer.V, g.DVW, g.DNorm1FromV);
            for (var t = 0; t < length; t++) for (var h = 0; h < ModelWidth; h++)
                g.DNorm1[t, h] = g.DNorm1FromQ[t, h] + g.DNorm1FromK[t, h] + g.DNorm1FromV[t, h];
            BackwardLayerNorm(lc.X0, lc.Norm1, layer.Norm1, g.DNorm1, g.DX0, g.DNorm1Gamma);

            dX = g.DX0;
            for (var t = 0; t < length; t++)
            {
                var token = cache.InputIds[t];
                for (var h = 0; h < ModelWidth; h++) _tokenGrad[token, h] += dX[t, h];
            }

            AddInPlace(layer.GQ, g.DQW); AddInPlace(layer.GK, g.DKW); AddInPlace(layer.GV, g.DVW); AddInPlace(layer.GO, g.DO);
            AddInPlace(layer.GF1, g.DF1); AddInPlace(layer.GF2, g.DF2);
            AddInPlace(layer.GNorm1, g.DNorm1Gamma); AddInPlace(layer.GNorm2, g.DNorm2Gamma);
        }

        var gradNorm = GlobalGradientNorm();
        var clip = gradNorm > 1f ? 1f / gradNorm : 1f;
        ScaleAllGradients(clip);
        _optimizerStep++;
        ApplyAdamW(learningRate);
    }

    private float GlobalGradientNorm()
    {
        double sum = 0;
        void Acc(float[,] a) { for (var i = 0; i < a.GetLength(0); i++) for (var j = 0; j < a.GetLength(1); j++) sum += a[i, j] * a[i, j]; }
        void AccV(float[] a) { for (var i = 0; i < a.Length; i++) sum += a[i] * a[i]; }
        Acc(_outputGrad); AccV(_biasGrad); Acc(_tokenGrad);
        foreach (var l in _layers) { Acc(l.GQ); Acc(l.GK); Acc(l.GV); Acc(l.GO); Acc(l.GF1); Acc(l.GF2); AccV(l.GNorm1); AccV(l.GNorm2); }
        return MathF.Sqrt((float)sum);
    }

    private void ScaleAllGradients(float scale)
    {
        void Scale(float[,] a) { for (var i = 0; i < a.GetLength(0); i++) for (var j = 0; j < a.GetLength(1); j++) a[i, j] *= scale; }
        void ScaleV(float[] a) { for (var i = 0; i < a.Length; i++) a[i] *= scale; }
        Scale(_outputGrad); ScaleV(_biasGrad); Scale(_tokenGrad);
        foreach (var l in _layers) { Scale(l.GQ); Scale(l.GK); Scale(l.GV); Scale(l.GO); Scale(l.GF1); Scale(l.GF2); ScaleV(l.GNorm1); ScaleV(l.GNorm2); }
    }

    private void ApplyAdamW(float learningRate)
    {
        const float beta1 = 0.9f; const float beta2 = 0.999f; const float weightDecay = 0.01f;
        var b1t = 1f - MathF.Pow(beta1, (float)_optimizerStep);
        var b2t = 1f - MathF.Pow(beta2, (float)_optimizerStep);
        void Update(float[,] p, float[,] g, float[,] m, float[,] v, float decay)
        {
            for (var i = 0; i < p.GetLength(0); i++) for (var j = 0; j < p.GetLength(1); j++)
            {
                var grad = g[i, j]; m[i, j] = beta1 * m[i, j] + (1 - beta1) * grad; v[i, j] = beta2 * v[i, j] + (1 - beta2) * grad * grad;
                var mh = m[i, j] / Math.Max(b1t, 1e-8f); var vh = v[i, j] / Math.Max(b2t, 1e-8f);
                p[i, j] -= learningRate * (mh / (MathF.Sqrt(vh) + 1e-8f) + decay * p[i, j]);
            }
        }
        void UpdateV(float[] p, float[] g, float[] m, float[] v, float decay)
        {
            for (var i = 0; i < p.Length; i++) { var grad = g[i]; m[i] = beta1 * m[i] + (1 - beta1) * grad; v[i] = beta2 * v[i] + (1 - beta2) * grad * grad; p[i] -= learningRate * (m[i] / Math.Max(b1t, 1e-8f) / (MathF.Sqrt(v[i] / Math.Max(b2t, 1e-8f)) + 1e-8f) + decay * p[i]); }
        }
        Update(_output, _outputGrad, _adam.OutputM, _adam.OutputV, weightDecay);
        UpdateV(_bias, _biasGrad, _adam.BiasM, _adam.BiasV, 0);
        Update(_tokenEmbedding, _tokenGrad, _adam.TokenM, _adam.TokenV, weightDecay);
        foreach (var l in _layers)
        {
            Update(l.Q, l.GQ, l.MQ, l.VQ, weightDecay); Update(l.K, l.GK, l.MK, l.VK, weightDecay);
            Update(l.V, l.GV, l.MV, l.VV, weightDecay); Update(l.O, l.GO, l.MO, l.VO, weightDecay);
            Update(l.F1, l.GF1, l.MF1, l.VF1, weightDecay); Update(l.F2, l.GF2, l.MF2, l.VF2, weightDecay);
            UpdateV(l.Norm1, l.GNorm1, l.MNorm1, l.VNorm1, 0); UpdateV(l.Norm2, l.GNorm2, l.MNorm2, l.VNorm2, 0);
        }
    }

    private static void AddMatMulGrad(float[,] input, float[,] dOutput, float[,] weights, float[,] dWeights, float[,] dInput)
    {
        var rows = input.GetLength(0); var inner = input.GetLength(1); var cols = dOutput.GetLength(1);
        for (var r = 0; r < rows; r++) for (var c = 0; c < cols; c++)
        {
            var go = dOutput[r, c];
            for (var i = 0; i < inner; i++) dWeights[i, c] += input[r, i] * go;
        }
        for (var r = 0; r < rows; r++) for (var i = 0; i < inner; i++)
        {
            var sum = 0f;
            for (var c = 0; c < cols; c++) sum += dOutput[r, c] * weights[i, c];
            dInput[r, i] += sum;
        }
    }

    private static void BackwardLayerNorm(float[,] source, float[,] normalized, float[] gamma, float[,] dNorm, float[,] dSource, float[] dGamma)
    {
        var rows = source.GetLength(0); var n = source.GetLength(1);
        for (var r = 0; r < rows; r++)
        {
            var mean = 0f; for (var i = 0; i < n; i++) mean += source[r, i]; mean /= n;
            var inv = 0f; for (var i = 0; i < n; i++) { var d = source[r, i] - mean; inv += d * d; } inv = 1f / MathF.Sqrt(inv / n + Epsilon);
            var sum1 = 0f; var sum2 = 0f;
            for (var i = 0; i < n; i++) { var dy = dNorm[r, i] * gamma[i]; sum1 += dy; sum2 += dy * (source[r, i] - mean); dGamma[i] += dNorm[r, i] * normalized[r, i]; }
            for (var i = 0; i < n; i++) { var dy = dNorm[r, i] * gamma[i]; dSource[r, i] += inv / n * (n * dy - sum1 - (source[r, i] - mean) * inv * inv * sum2); }
        }
    }

    private static float Gelu(float x) => 0.5f * x * (1f + MathF.Tanh(0.79788456f * (x + 0.044715f * x * x * x)));
    private static float GeluDerivative(float x)
    {
        var u = 0.79788456f * (x + 0.044715f * x * x * x); var t = MathF.Tanh(u);
        return 0.5f * (1f + t) + 0.5f * x * (1f - t * t) * 0.79788456f * (1f + 3f * 0.044715f * x * x);
    }

    private static void LayerNorm(float[,] source, int row, float[] gamma, float[,] target, int targetRow)
    {
        var mean = 0f; for (var h = 0; h < ModelWidth; h++) mean += source[row, h]; mean /= ModelWidth;
        var variance = 0f; for (var h = 0; h < ModelWidth; h++) { var d = source[row, h] - mean; variance += d * d; } variance /= ModelWidth;
        var inv = 1f / MathF.Sqrt(variance + Epsilon);
        for (var h = 0; h < ModelWidth; h++) target[targetRow, h] = (source[row, h] - mean) * inv * gamma[h];
    }

    private float[] Softmax(float[] hidden, float temperature)
    {
        var logits = new float[Vocab]; var max = float.NegativeInfinity;
        for (var v = 0; v < Vocab; v++) { var value = _bias[v]; for (var h = 0; h < ModelWidth; h++) value += hidden[h] * _output[h, v]; logits[v] = value / Math.Max(0.05f, temperature); max = MathF.Max(max, logits[v]); }
        var sum = 0f; for (var v = 0; v < Vocab; v++) { logits[v] = MathF.Exp(Math.Clamp(logits[v] - max, -30, 30)); sum += logits[v]; }
        for (var v = 0; v < Vocab; v++) logits[v] /= Math.Max(sum, 1e-8f); return logits;
    }

    private int Sample(float[] probabilities)
    {
        var r = _random.NextSingle(); for (var i = 0; i < probabilities.Length; i++) { r -= probabilities[i]; if (r <= 0) return i; } return IndexOf(' ');
    }

    private static int[] Encode(string text) => text.Select(IndexOf).ToArray();
    private static string Decode(IEnumerable<int> ids) { var sb = new StringBuilder(); foreach (var id in ids) sb.Append(Vocabulary[Math.Clamp(id, 0, Vocab - 1)]); return sb.ToString().Trim(); }
    private static int IndexOf(char c) { var index = Vocabulary.IndexOf(c); return index >= 0 ? index : 0; }
    private float NextWeight(float scale) => (float)(_random.NextDouble() * 2d - 1d) * scale;
    private void Fill(float[,] matrix, float scale) { for (var i = 0; i < matrix.GetLength(0); i++) for (var j = 0; j < matrix.GetLength(1); j++) matrix[i, j] = NextWeight(scale); }
    private static void Copy(float[,] src, float[,] dst) { for (var i = 0; i < src.GetLength(0); i++) for (var j = 0; j < src.GetLength(1); j++) dst[i, j] = src[i, j]; }
    private static void Zero(float[,] a) => Array.Clear(a, 0, a.Length);
    private static void Zero(float[] a) => Array.Clear(a, 0, a.Length);
    private static void Zero(LayerGrad g) { Zero(g.DX0); Zero(g.DNorm1); Zero(g.DQ); Zero(g.DK); Zero(g.DV); Zero(g.DAttended); Zero(g.DX1); Zero(g.DNorm2); Zero(g.DF1Pre); Zero(g.DF1Act); Zero(g.DF2Out); Zero(g.DQW); Zero(g.DKW); Zero(g.DVW); Zero(g.DO); Zero(g.DF1); Zero(g.DF2); Zero(g.DNorm1FromQ); Zero(g.DNorm1FromK); Zero(g.DNorm1FromV); Zero(g.DNorm1Gamma); Zero(g.DNorm2Gamma); }
    private static void AddInPlace(float[,] dst, float[,] src) { for (var i = 0; i < dst.GetLength(0); i++) for (var j = 0; j < dst.GetLength(1); j++) dst[i, j] += src[i, j]; }
    private static void AddInPlace(float[] dst, float[] src) { for (var i = 0; i < dst.Length; i++) dst[i] += src[i]; }
    private static void Multiply(float[,] input, float[,] weights, float[,] output) { var rows = input.GetLength(0); var inner = input.GetLength(1); var columns = weights.GetLength(1); for (var r = 0; r < rows; r++) for (var c = 0; c < columns; c++) { var sum = 0f; for (var i = 0; i < inner; i++) sum += input[r, i] * weights[i, c]; output[r, c] = sum; } }

    private void Save()
    {
        var data = new TransformerWeights { Version = 2, Trained = _trained, OptimizerStep = _optimizerStep, TokenEmbedding = Flatten(_tokenEmbedding), Output = Flatten(_output), Bias = _bias, Layers = _layers.Select(SerializeLayer).ToArray() };
        File.WriteAllText(_weightsPath, JsonSerializer.Serialize(data));
    }

    private static SerializedLayer SerializeLayer(LayerWeights l) => new() { Q = Flatten(l.Q), K = Flatten(l.K), V = Flatten(l.V), O = Flatten(l.O), F1 = Flatten(l.F1), F2 = Flatten(l.F2), Norm1 = l.Norm1, Norm2 = l.Norm2 };

    private void LoadIfPresent()
    {
        try
        {
            if (!File.Exists(_weightsPath)) return;
            var data = JsonSerializer.Deserialize<TransformerWeights>(File.ReadAllText(_weightsPath));
            if (data?.Version != 2 || data.TokenEmbedding?.Length != _tokenEmbedding.Length || data.Output?.Length != _output.Length || data.Bias?.Length != _bias.Length || data.Layers?.Length != Layers) return;
            Unflatten(data.TokenEmbedding, _tokenEmbedding); Unflatten(data.Output, _output); Array.Copy(data.Bias, _bias, _bias.Length);
            for (var i = 0; i < Layers; i++) { var s = data.Layers[i]; if (s.Q?.Length != _layers[i].Q.Length || s.K?.Length != _layers[i].K.Length || s.V?.Length != _layers[i].V.Length || s.O?.Length != _layers[i].O.Length || s.F1?.Length != _layers[i].F1.Length || s.F2?.Length != _layers[i].F2.Length) return; Unflatten(s.Q, _layers[i].Q); Unflatten(s.K, _layers[i].K); Unflatten(s.V, _layers[i].V); Unflatten(s.O, _layers[i].O); Unflatten(s.F1, _layers[i].F1); Unflatten(s.F2, _layers[i].F2); if (s.Norm1?.Length == ModelWidth) Array.Copy(s.Norm1, _layers[i].Norm1, ModelWidth); if (s.Norm2?.Length == ModelWidth) Array.Copy(s.Norm2, _layers[i].Norm2, ModelWidth); }
            _optimizerStep = data.OptimizerStep; _trained = data.Trained;
        }
        catch { _trained = false; }
    }

    private static float[] Flatten(float[,] matrix) { var result = new float[matrix.Length]; var n = 0; for (var i = 0; i < matrix.GetLength(0); i++) for (var j = 0; j < matrix.GetLength(1); j++) result[n++] = matrix[i, j]; return result; }
    private static void Unflatten(float[] source, float[,] matrix) { var n = 0; for (var i = 0; i < matrix.GetLength(0); i++) for (var j = 0; j < matrix.GetLength(1); j++) matrix[i, j] = source[n++]; }

    private sealed class LayerWeights
    {
        public float[,] Q { get; } = new float[ModelWidth, ModelWidth]; public float[,] K { get; } = new float[ModelWidth, ModelWidth]; public float[,] V { get; } = new float[ModelWidth, ModelWidth]; public float[,] O { get; } = new float[ModelWidth, ModelWidth]; public float[,] F1 { get; } = new float[ModelWidth, FeedForward]; public float[,] F2 { get; } = new float[FeedForward, ModelWidth]; public float[] Norm1 { get; } = new float[ModelWidth]; public float[] Norm2 { get; } = new float[ModelWidth];
        public float[,] GQ { get; } = new float[ModelWidth, ModelWidth]; public float[,] GK { get; } = new float[ModelWidth, ModelWidth]; public float[,] GV { get; } = new float[ModelWidth, ModelWidth]; public float[,] GO { get; } = new float[ModelWidth, ModelWidth]; public float[,] GF1 { get; } = new float[ModelWidth, FeedForward]; public float[,] GF2 { get; } = new float[FeedForward, ModelWidth]; public float[] GNorm1 { get; } = new float[ModelWidth]; public float[] GNorm2 { get; } = new float[ModelWidth];
        public float[,] MQ { get; } = new float[ModelWidth, ModelWidth]; public float[,] MK { get; } = new float[ModelWidth, ModelWidth]; public float[,] MV { get; } = new float[ModelWidth, ModelWidth]; public float[,] MO { get; } = new float[ModelWidth, ModelWidth]; public float[,] MF1 { get; } = new float[ModelWidth, FeedForward]; public float[,] MF2 { get; } = new float[FeedForward, ModelWidth]; public float[] MNorm1 { get; } = new float[ModelWidth]; public float[] MNorm2 { get; } = new float[ModelWidth];
        public float[,] VQ { get; } = new float[ModelWidth, ModelWidth]; public float[,] VK { get; } = new float[ModelWidth, ModelWidth]; public float[,] VV { get; } = new float[ModelWidth, ModelWidth]; public float[,] VO { get; } = new float[ModelWidth, ModelWidth]; public float[,] VF1 { get; } = new float[ModelWidth, FeedForward]; public float[,] VF2 { get; } = new float[FeedForward, ModelWidth]; public float[] VNorm1 { get; } = new float[ModelWidth]; public float[] VNorm2 { get; } = new float[ModelWidth];
    }

    private sealed class ForwardCache
    {
        public int Length { get; } public int[] InputIds { get; } public float[,] X { get; } public LayerCache[] Layers { get; } public float[] LastHidden { get; }
        public ForwardCache(int length) { Length = length; InputIds = new int[length]; X = new float[length, ModelWidth]; Layers = Enumerable.Range(0, NativeTransformerBrain.Layers).Select(_ => new LayerCache(length)).ToArray(); LastHidden = new float[ModelWidth]; }
    }

    private sealed class LayerCache
    {
        public float[,] X0, Norm1, Q, K, V, Attended, Projected, X1, Norm2, F1Pre, F1Act, F2Out; public float[,,] Attention; public LayerGrad Grad;
        public LayerCache(int length) { X0 = new float[length, ModelWidth]; Norm1 = new float[length, ModelWidth]; Q = new float[length, ModelWidth]; K = new float[length, ModelWidth]; V = new float[length, ModelWidth]; Attended = new float[length, ModelWidth]; Projected = new float[length, ModelWidth]; X1 = new float[length, ModelWidth]; Norm2 = new float[length, ModelWidth]; F1Pre = new float[length, FeedForward]; F1Act = new float[length, FeedForward]; F2Out = new float[length, ModelWidth]; Attention = new float[length, Heads, length]; Grad = new LayerGrad(length); }
    }

    private sealed class LayerGrad
    {
        public float[,] DX0, DNorm1, DQ, DK, DV, DAttended, DX1, DNorm2, DF1Pre, DF1Act, DF2Out, DQW, DKW, DVW, DO, DF1, DF2, DNorm1FromQ, DNorm1FromK, DNorm1FromV; public float[] DNorm1Gamma, DNorm2Gamma;
        public LayerGrad(int length) { DX0 = new float[length, ModelWidth]; DNorm1 = new float[length, ModelWidth]; DQ = new float[length, ModelWidth]; DK = new float[length, ModelWidth]; DV = new float[length, ModelWidth]; DAttended = new float[length, ModelWidth]; DX1 = new float[length, ModelWidth]; DNorm2 = new float[length, ModelWidth]; DF1Pre = new float[length, FeedForward]; DF1Act = new float[length, FeedForward]; DF2Out = new float[length, ModelWidth]; DQW = new float[ModelWidth, ModelWidth]; DKW = new float[ModelWidth, ModelWidth]; DVW = new float[ModelWidth, ModelWidth]; DO = new float[ModelWidth, ModelWidth]; DF1 = new float[ModelWidth, FeedForward]; DF2 = new float[FeedForward, ModelWidth]; DNorm1FromQ = new float[length, ModelWidth]; DNorm1FromK = new float[length, ModelWidth]; DNorm1FromV = new float[length, ModelWidth]; DNorm1Gamma = new float[ModelWidth]; DNorm2Gamma = new float[ModelWidth]; }
    }

    private sealed class AdamState
    {
        public float[,] OutputM = new float[ModelWidth, Vocab], OutputV = new float[ModelWidth, Vocab], TokenM = new float[Vocab, ModelWidth], TokenV = new float[Vocab, ModelWidth]; public float[] BiasM = new float[Vocab], BiasV = new float[Vocab];
    }

    private sealed class TransformerWeights
    {
        public int Version { get; set; } public bool Trained { get; set; } public long OptimizerStep { get; set; } public float[]? TokenEmbedding { get; set; } public float[]? Output { get; set; } public float[]? Bias { get; set; } public SerializedLayer[]? Layers { get; set; }
    }
    private sealed class SerializedLayer { public float[]? Q { get; set; } public float[]? K { get; set; } public float[]? V { get; set; } public float[]? O { get; set; } public float[]? F1 { get; set; } public float[]? F2 { get; set; } public float[]? Norm1 { get; set; } public float[]? Norm2 { get; set; } }
}
