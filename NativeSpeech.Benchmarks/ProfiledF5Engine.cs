using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Diagnostics;
using System.Text;
using MemoirsReader.Native;
namespace MemoirsReader.Diagnostics;

public sealed class ProfiledF5Engine : IDisposable
{
    private readonly InferenceSession prepare, transformer, decoder;
    private readonly Dictionary<string, int> vocabulary;
    private string? referenceKey;
    private short[]? referencePcm;
    public int ReferencePreparations { get; private set; }
    private readonly bool profiling;
    public string[] FinishProfiling() => profiling ? new[]{prepare.EndProfiling(),transformer.EndProfiling(),decoder.EndProfiling()} : Array.Empty<string>();

    public ProfiledF5Engine(string directory, string vocabPath, int threads, Action<string> progress, CancellationToken cancellation = default, string? profileDirectory = null)
    {
        profiling = profileDirectory != null;
        if (profileDirectory != null) Directory.CreateDirectory(profileDirectory);
        var loaded = new List<InferenceSession>();
        InferenceSession Load(string name)
        {
            cancellation.ThrowIfCancellationRequested();
            using var options = new SessionOptions { IntraOpNumThreads = threads, InterOpNumThreads = 1 };
            progress("Loading F5 " + name);
            if (profileDirectory != null) options.ProfileOutputPathPrefix = Path.Combine(profileDirectory,name);
            options.EnableProfiling = profiling;
            var session = new InferenceSession(Path.Combine(directory, name + ".onnx"), options);
            loaded.Add(session);
            return session;
        }
        try
        {
            prepare = Load("F5_Preprocess"); transformer = Load("F5_Transformer"); decoder = Load("F5_Decode");
            vocabulary = File.ReadAllLines(vocabPath).Select((line, index) => (line, index)).ToDictionary(x => x.line, x => x.index);
        }
        catch { foreach (var session in loaded) session.Dispose(); throw; }
    }
    public float[] Generate(string voice, string referenceText, string narration, double speed, Action<string> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        referenceText = Normalize(referenceText).Trim(); narration = Normalize(narration).Trim();
        if (referenceText.Length == 0) throw new ArgumentException("F5 needs the words spoken in the selected recording. No transcription service is included. Chatterbox needs no transcript.");
        if (narration.Length == 0) throw new ArgumentException("Enter narration text.");
        if ((referenceText + narration).Any(c => c > 127)) throw new NotSupportedException("This native F5 integration currently supports English text only.");
        if (!double.IsFinite(speed) || speed < .7 || speed > 1.2) throw new ArgumentOutOfRangeException(nameof(speed));
        string key = AudioIdentity.Key(voice);
        if (key != referenceKey)
        {
            progress("Preparing F5 voice sample (full recording)");
            var (samples, rate) = WaveAudio.Read(voice);
            samples = WaveAudio.Resample(samples, rate, 24000);
            float peak = samples.Max(x => Math.Abs(x));
            referencePcm = samples.Select(x => (short)Math.Clamp((int)(x / peak * 32767), short.MinValue, short.MaxValue)).ToArray();
            referenceKey = key; ReferencePreparations++;
        }
        short[] pcm = referencePcm!;
        int[] ids = (referenceText + " " + narration).Select(c => vocabulary.GetValueOrDefault(c.ToString(), 0)).ToArray();
        int referenceFrames = pcm.Length / 256 + 1;
        long duration = checked(referenceFrames + (long)(referenceFrames * (double)Encoding.UTF8.GetByteCount(narration) / Encoding.UTF8.GetByteCount(referenceText) / speed));
        using var prepared = prepare.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("audio",new DenseTensor<short>(pcm,new[]{1,1,pcm.Length})),
            NamedOnnxValue.CreateFromTensor("text_ids",new DenseTensor<int>(ids,new[]{1,ids.Length})),
            NamedOnnxValue.CreateFromTensor("max_duration",new DenseTensor<long>(new[]{duration},new[]{1}))
        });
        Tensor<float> noise = prepared.First(x => x.Name == "noise").AsTensor<float>();
        Tensor<int> time = new DenseTensor<int>(new[]{0},new[]{1});
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? last = null;
        try
        {
            var timer = Stopwatch.StartNew();
            for (int step = 0; step < 31; step++)
            {
                cancellation.ThrowIfCancellationRequested();
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("noise",noise),
                    NamedOnnxValue.CreateFromTensor("time_step.1",time)
                };
                foreach (string name in new[]{"rope_cos_q","rope_sin_q","rope_cos_k","rope_sin_k","cat_mel_text","cat_mel_text_drop"})
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name,prepared.First(x=>x.Name==name).AsTensor<float>()));
                var next = transformer.Run(inputs);
                last?.Dispose(); last = next;
                noise = next.First(x=>x.Name=="denoised").AsTensor<float>();
                time = next.First(x=>x.Name=="time_step").AsTensor<int>();
                progress($"F5 step {step+1}/31 ({timer.Elapsed.TotalSeconds:F1}s)");
            }
            cancellation.ThrowIfCancellationRequested();
            progress("Decoding F5 audio");
            using var decoded = decoder.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("denoised",noise),
                NamedOnnxValue.CreateFromTensor("ref_signal_len",prepared.First(x=>x.Name=="ref_signal_len").AsTensor<long>())
            });
            cancellation.ThrowIfCancellationRequested();
            return decoded.First().AsTensor<short>().Select(x=>x/32768f).ToArray();
        }
        finally { last?.Dispose(); }
    }
    public void Dispose() { decoder.Dispose(); transformer.Dispose(); prepare.Dispose(); }
    private static string Normalize(string text) => text.Replace(';',',').Replace('“','"').Replace('”','"').Replace('‘','\'').Replace('’','\'');
}
