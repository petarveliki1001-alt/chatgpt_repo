using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Diagnostics;
using System.Text.Json;

static class ChatterboxProbe
{
    public static void Generate(string directory, string voice, string text, string output, int maxTokens = 256)
    {
        var overall = Stopwatch.StartNew();
        using var options = new SessionOptions { IntraOpNumThreads = 4, InterOpNumThreads = 1 };
        InferenceSession Load(string name)
        {
            Console.WriteLine($"Loading Chatterbox {name}");
            return new InferenceSession(Path.Combine(directory, "onnx", name + ".onnx"), options);
        }
        using var encoder = Load("speech_encoder");
        using var embedder = Load("embed_tokens");
        using var languageModel = Load("language_model");
        using var decoder = Load("conditional_decoder");
        double loadSeconds = overall.Elapsed.TotalSeconds;
        var run = Stopwatch.StartNew();
        var (audio, rate) = WaveAudio.Read(voice);
        audio = WaveAudio.Resample(audio, rate, 24000);
        using var encoded = encoder.Run(new[] { F("audio_values", audio, 1, audio.Length) });
        var cond = Copy<float>(encoded.ElementAt(0));
        var prompt = Copy<long>(encoded.ElementAt(1));
        var speaker = Copy<float>(encoded.ElementAt(2));
        var features = Copy<float>(encoded.ElementAt(3));
        var ids = new BpeTokenizer(Path.Combine(directory, "tokenizer.json")).Encode(text);
        Console.WriteLine("Text token IDs: " + string.Join(',', ids));
        long[] positions = ids.Select((id, i) => id >= 6561 ? 0L : i - 1L).ToArray();
        var generated = new List<long> { 6561 };
        string[] cacheNames = languageModel.InputMetadata.Keys.Where(x => x.StartsWith("past_key_values.")).ToArray();
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? previous = null;
        int attentionLength = 0;
        try
        {
            for (int step = 0; step < maxTokens; step++)
            {
                using var embedded = embedder.Run(new[] { L("input_ids", ids, 1, ids.Length), L("position_ids", positions, 1, positions.Length), F("exaggeration", new[] { .5f }, 1) });
                var tokenEmbedding = embedded.First().AsTensor<float>();
                float[] values = tokenEmbedding.ToArray();
                int sequenceLength = tokenEmbedding.Dimensions[1], width = tokenEmbedding.Dimensions[2];
                if (step == 0)
                {
                    values = cond.ToArray().Concat(values).ToArray();
                    sequenceLength += cond.Dimensions[1];
                    attentionLength = sequenceLength;
                }
                var inputs = new List<NamedOnnxValue>
                {
                    F("inputs_embeds", values, 1, sequenceLength, width),
                    L("attention_mask", Enumerable.Repeat(1L, attentionLength).ToArray(), 1, attentionLength)
                };
                for (int i = 0; i < cacheNames.Length; i++)
                {
                    Tensor<float> cache;
                    if (previous == null)
                    {
                        int[] dims = languageModel.InputMetadata[cacheNames[i]].Dimensions.Select((d, axis) => axis == 2 ? 0 : d < 0 ? 1 : d).ToArray();
                        cache = new DenseTensor<float>(dims);
                    }
                    else cache = previous.ElementAt(i+1).AsTensor<float>();
                    inputs.Add(NamedOnnxValue.CreateFromTensor(cacheNames[i], cache));
                }
                var result = languageModel.Run(inputs);
                previous?.Dispose();
                previous = result;
                var logits = result.First().AsTensor<float>();
                int vocabularySize = logits.Dimensions[^1];
                float[] scores = logits.ToArray();
                int offset = scores.Length - vocabularySize;
                int last = (int)generated[^1];
                if (last < vocabularySize) scores[offset + last] = scores[offset + last] < 0 ? scores[offset + last] * 1.2f : scores[offset + last] / 1.2f;
                int next = 0;
                for (int token = 1; token < vocabularySize; token++) if (scores[offset + token] > scores[offset + next]) next = token;
                generated.Add(next);
                if (step % 10 == 0) Console.WriteLine($"Chatterbox token {step+1}: {next}, elapsed {run.Elapsed.TotalSeconds:F2}s");
                if (next == 6562) break;
                ids = new[] { (long)next }; positions = new[] { step + 1L }; attentionLength++;
            }
        }
        finally { previous?.Dispose(); }
        if (generated[^1] != 6562) throw new InvalidDataException($"No stop token after {maxTokens} tokens; output was not accepted as complete.");
        long[] speech = prompt.ToArray().Concat(generated.Skip(1).Take(generated.Count - 2)).ToArray();
        Console.WriteLine($"Decoding {generated.Count - 2} generated speech tokens");
        using var decoded = decoder.Run(new[]
        {
            L("speech_tokens", speech, 1, speech.Length),
            NamedOnnxValue.CreateFromTensor("speaker_embeddings", speaker),
            NamedOnnxValue.CreateFromTensor("speaker_features", features)
        });
        float[] waveform = decoded.First().AsTensor<float>().ToArray();
        WaveAudio.Write(output, waveform, 24000);
        File.WriteAllText(output + ".tokens.json", JsonSerializer.Serialize(generated));
        var report = new { engine = "Chatterbox ONNX FP32, C#", reference = voice, text, tokens = generated.Count-2, loadSeconds, inferenceSeconds = run.Elapsed.TotalSeconds, durationSeconds = waveform.Length/24000.0, rms = Math.Sqrt(waveform.Average(x => (double)x*x)), peak = waveform.Max(x => Math.Abs(x)) };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(output + ".json", json); Console.WriteLine(json);
    }
    private static DenseTensor<T> Copy<T>(DisposableNamedOnnxValue value) { var t = value.AsTensor<T>(); return new DenseTensor<T>(t.ToArray(), t.Dimensions.ToArray()); }
    private static NamedOnnxValue F(string name, float[] values, params int[] dimensions) => NamedOnnxValue.CreateFromTensor(name, new DenseTensor<float>(values, dimensions));
    private static NamedOnnxValue L(string name, long[] values, params int[] dimensions) => NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(values, dimensions));
}
