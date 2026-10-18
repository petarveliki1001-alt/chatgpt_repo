using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

static class F5Probe
{
    public static void Generate(string directory, string vocabulary, string voice, string referenceText, string narration, string output)
    {
        var timer = Stopwatch.StartNew();
        using var options = new SessionOptions { IntraOpNumThreads = 4, InterOpNumThreads = 1 };
        using var prepare = new InferenceSession(Path.Combine(directory, "F5_Preprocess.onnx"), options);
        using var transformer = new InferenceSession(Path.Combine(directory, "F5_Transformer.onnx"), options);
        using var decoder = new InferenceSession(Path.Combine(directory, "F5_Decode.onnx"), options);
        double loadSeconds = timer.Elapsed.TotalSeconds;
        timer.Restart();
        var (samples, rate) = WaveAudio.Read(voice);
        samples = WaveAudio.Resample(samples, rate, 24000);
        float peak = samples.Max(x => Math.Abs(x));
        if (peak <= 0) throw new InvalidDataException("Silent reference");
        short[] pcm = samples.Select(x => (short)Math.Clamp((int)(x / peak * 32767), short.MinValue, short.MaxValue)).ToArray();
        referenceText = Normalize(referenceText).Trim();
        narration = Normalize(narration);
        if ((referenceText + narration).Any(c => c > 127)) throw new NotSupportedException("This verification probe currently implements the English tokenizer only.");
        var vocab = new Dictionary<string, int>();
        int token = 0;
        foreach (string line in File.ReadAllLines(vocabulary)) vocab[line] = token++;
        int[] ids = (referenceText + " " + narration).Select(c => vocab.GetValueOrDefault(c.ToString(), 0)).ToArray();
        int referenceFrames = pcm.Length / 256 + 1;
        long duration = referenceFrames + (long)(referenceFrames * (double)Encoding.UTF8.GetByteCount(narration) / Encoding.UTF8.GetByteCount(referenceText));
        using var prepared = prepare.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("audio", new DenseTensor<short>(pcm, new[] {1,1,pcm.Length})),
            NamedOnnxValue.CreateFromTensor("text_ids", new DenseTensor<int>(ids, new[] {1,ids.Length})),
            NamedOnnxValue.CreateFromTensor("max_duration", new DenseTensor<long>(new[]{duration},new[]{1}))
        });
        Tensor<float> noise = prepared.First(x => x.Name == "noise").AsTensor<float>();
        // Capture exact test inputs/noise for a controlled cross-binding comparison.
        File.WriteAllBytes(output + ".input.pcm16", MemoryMarshal.AsBytes(pcm.AsSpan()).ToArray());
        File.WriteAllBytes(output + ".input.ids.i32", MemoryMarshal.AsBytes(ids.AsSpan()).ToArray());
        File.WriteAllBytes(output + ".input.noise.f32", MemoryMarshal.AsBytes(noise.ToArray().AsSpan()).ToArray());
        File.WriteAllText(output + ".input.json", JsonSerializer.Serialize(new {duration,noiseShape=noise.Dimensions.ToArray(),textIdsLength=ids.Length}));
        Tensor<int> time = new DenseTensor<int>(new[]{0}, new[]{1});
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? last = null;
        try
        {
            // This pinned 2025 export has 32 time points / 31 integration intervals.
            // The publisher's CPU reference loop also uses NFE_STEP - 1.
            for (int step = 0; step < 31; step++)
            {
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("noise",noise),
                    NamedOnnxValue.CreateFromTensor("time_step.1",time)
                };
                foreach (string name in new[] {"rope_cos_q","rope_sin_q","rope_cos_k","rope_sin_k","cat_mel_text","cat_mel_text_drop"})
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name,prepared.First(x => x.Name==name).AsTensor<float>()));
                var next = transformer.Run(inputs);
                last?.Dispose(); last = next;
                noise = last.First(x=>x.Name=="denoised").AsTensor<float>();
                time = last.First(x=>x.Name=="time_step").AsTensor<int>();
                if ((step+1)%8==0 || step==30) Console.WriteLine($"F5 native step {step+1}/31, elapsed {timer.Elapsed.TotalSeconds:F2}s");
            }
            using var decoded = decoder.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("denoised",noise),
                NamedOnnxValue.CreateFromTensor("ref_signal_len",prepared.First(x=>x.Name=="ref_signal_len").AsTensor<long>())
            });
            float[] audio = decoded.First().AsTensor<short>().Select(x => x/32768f).ToArray();
            WaveAudio.Write(output,audio,24000);
            var report = new {engine="F5 ONNX FP32, C#",reference=voice,referenceText,narration,steps=31,loadSeconds,inferenceSeconds=timer.Elapsed.TotalSeconds,durationSeconds=audio.Length/24000.0,rms=Math.Sqrt(audio.Average(x=>(double)x*x)),peak=audio.Max(x=>Math.Abs(x))};
            string json = JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
            File.WriteAllText(output+".json",json); Console.WriteLine(json);
        }
        finally { last?.Dispose(); }
    }
    private static string Normalize(string text) => text.Replace(';',',').Replace('“','"').Replace('”','"').Replace('‘','\'').Replace('’','\'');
}
