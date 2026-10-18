using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Diagnostics;
using System.Text.Json;

using MemoirsReader.Native;
namespace MemoirsReader.Diagnostics;

public sealed class ProfiledChatterboxEngine : IDisposable
{
    private readonly InferenceSession encoder, embedder, languageModel, decoder;
    private readonly DiagnosticBpeTokenizer tokenizer;
    private DenseTensor<float>? cachedCond, cachedSpeaker, cachedFeatures;
    private DenseTensor<long>? cachedPrompt;
    private string? referenceKey;
    public int ReferencePreparations { get; private set; }
    public ChatterboxTimings? LastTimings { get; private set; }
    public long[] LastSpeechTokens { get; private set; } = Array.Empty<long>();
    public Dictionary<string,double> ModelLoadSeconds { get; } = new();
    private readonly bool profiling;
    public string[] FinishProfiling() => profiling ? new[]{encoder.EndProfiling(),embedder.EndProfiling(),languageModel.EndProfiling(),decoder.EndProfiling()} : Array.Empty<string>();
    public ProfiledChatterboxEngine(string directory, int threads, Action<string> progress, CancellationToken cancellation = default, string? profileDirectory = null, string? decoderCache = null, bool allowSpinning = true, bool prepacking = true, string? languageModelOverride = null)
    {
        profiling = profileDirectory != null;
        if (profileDirectory != null) Directory.CreateDirectory(profileDirectory);
        var loaded = new List<InferenceSession>();
        InferenceSession Load(string name)
        {
            cancellation.ThrowIfCancellationRequested();
            using var options = new SessionOptions { IntraOpNumThreads = threads, InterOpNumThreads = 1 };
            if (!allowSpinning)
            {
                options.AddSessionConfigEntry("session.intra_op.allow_spinning","0");
                options.AddSessionConfigEntry("session.inter_op.allow_spinning","0");
            }
            if (!prepacking && name=="conditional_decoder") options.AddSessionConfigEntry("session.disable_prepacking","1");
            string source = Path.Combine(directory,"onnx",name+".onnx");
            if(name=="language_model" && languageModelOverride!=null) source=languageModelOverride;
            string? pending = null;
            if(name=="conditional_decoder" && decoderCache!=null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(decoderCache))!);
                if(File.Exists(decoderCache)) source=decoderCache;
                else
                {
                    pending=decoderCache+".partial.ort";
                    options.OptimizedModelFilePath=pending;
                    options.AddSessionConfigEntry("session.save_model_format","ORT");
                }
            }
            progress("Loading Chatterbox " + name);
            if (profileDirectory != null) options.ProfileOutputPathPrefix = Path.Combine(profileDirectory,name);
            options.EnableProfiling = profiling;
            var loadTimer = Stopwatch.StartNew();
            var session = new InferenceSession(source, options);
            if(pending!=null) File.Move(pending,decoderCache!,false);
            ModelLoadSeconds[name] = loadTimer.Elapsed.TotalSeconds;
            loaded.Add(session);
            return session;
        }
        try
        {
            encoder = Load("speech_encoder");
            embedder = Load("embed_tokens");
            languageModel = Load("language_model");
            decoder = Load("conditional_decoder");
            tokenizer = new DiagnosticBpeTokenizer(Path.Combine(directory, "tokenizer.json"));
        }
        catch { foreach (var session in loaded) session.Dispose(); throw; }
    }
    public float[] Generate(string voice, string text, Action<string> progress, CancellationToken cancellation, int maxTokens = 1024)
    {
        cancellation.ThrowIfCancellationRequested();
        var run = Stopwatch.StartNew();
        string key = AudioIdentity.Key(voice);
        if (key != referenceKey)
        {
            progress("Preparing voice sample");
            var (audio, rate) = WaveAudio.Read(voice);
            audio = WaveAudio.Resample(audio, rate, 24000);
            using var encoded = encoder.Run(new[] { F("audio_values", audio, 1, audio.Length) });
            cachedCond = Copy<float>(encoded.ElementAt(0));
            cachedPrompt = Copy<long>(encoded.ElementAt(1));
            cachedSpeaker = Copy<float>(encoded.ElementAt(2));
            cachedFeatures = Copy<float>(encoded.ElementAt(3));
            referenceKey = key;
            ReferencePreparations++;
        }
        var cond = cachedCond!;
        var prompt = cachedPrompt!;
        var speaker = cachedSpeaker!;
        var features = cachedFeatures!;
        double referenceSeconds = run.Elapsed.TotalSeconds;
        double embeddingSeconds = 0, languageSeconds = 0;
        var ids = tokenizer.Encode(text);

        long[] positions = ids.Select((id, i) => id >= 6561 ? 0L : i - 1L).ToArray();
        var generated = new List<long> { 6561 };
        string[] cacheNames = languageModel.InputMetadata.Keys.Where(x => x.StartsWith("past_key_values.")).ToArray();
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? previous = null;
        int attentionLength = 0;
        try
        {
            for (int step = 0; step < maxTokens; step++)
            {
                cancellation.ThrowIfCancellationRequested();
                var stage = Stopwatch.StartNew();
                using var embedded = embedder.Run(new[] { L("input_ids", ids, 1, ids.Length), L("position_ids", positions, 1, positions.Length), F("exaggeration", new[] { .5f }, 1) });
                embeddingSeconds += stage.Elapsed.TotalSeconds;
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
                stage.Restart();
                var result = languageModel.Run(inputs);
                languageSeconds += stage.Elapsed.TotalSeconds;
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
                if (step % 10 == 0) progress($"Chatterbox: {step+1} speech tokens ({run.Elapsed.TotalSeconds:F1}s)");
                if (next == 6562) break;
                ids = new[] { (long)next }; positions = new[] { step + 1L }; attentionLength++;
            }
        }
        finally { previous?.Dispose(); }
        if (generated[^1] != 6562) throw new InvalidDataException($"No stop token after {maxTokens} tokens; output was not accepted as complete.");
        long[] speech = prompt.ToArray().Concat(generated.Skip(1).Take(generated.Count - 2)).ToArray();
        cancellation.ThrowIfCancellationRequested();
        progress($"Decoding {generated.Count - 2} speech tokens");
        var decodeTimer = Stopwatch.StartNew();
        using var decoded = decoder.Run(new[]
        {
            L("speech_tokens", speech, 1, speech.Length),
            NamedOnnxValue.CreateFromTensor("speaker_embeddings", speaker),
            NamedOnnxValue.CreateFromTensor("speaker_features", features)
        });
        double decodeSeconds = decodeTimer.Elapsed.TotalSeconds;
        float[] waveform = decoded.First().AsTensor<float>().ToArray();
        cancellation.ThrowIfCancellationRequested();
        LastSpeechTokens = generated.ToArray();
        LastTimings = new(referenceSeconds,embeddingSeconds,languageSeconds,decodeSeconds,run.Elapsed.TotalSeconds,generated.Count-2,waveform.Length/24000.0);
        return waveform;
    }
    public void Dispose()
    {
        decoder.Dispose(); languageModel.Dispose(); embedder.Dispose(); encoder.Dispose();
    }
    private static DenseTensor<T> Copy<T>(DisposableNamedOnnxValue value) { var t = value.AsTensor<T>(); return new DenseTensor<T>(t.ToArray(), t.Dimensions.ToArray()); }
    private static NamedOnnxValue F(string name, float[] values, params int[] dimensions) => NamedOnnxValue.CreateFromTensor(name, new DenseTensor<float>(values, dimensions));
    private static NamedOnnxValue L(string name, long[] values, params int[] dimensions) => NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(values, dimensions));
}

public sealed record ChatterboxTimings(double ReferenceSeconds,double EmbeddingSeconds,double LanguageSeconds,double DecodeSeconds,double TotalSeconds,int SpeechTokens,double AudioSeconds);
