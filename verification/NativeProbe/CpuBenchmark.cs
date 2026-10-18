using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

static class CpuBenchmark
{
    // Measures actual transformer work with identical captured inputs. No ASR or Python.
    public static void Run(string directory, string capture, string output)
    {
        short[] pcm = MemoryMarshal.Cast<byte, short>(File.ReadAllBytes(capture + ".input.pcm16")).ToArray();
        int[] ids = MemoryMarshal.Cast<byte, int>(File.ReadAllBytes(capture + ".input.ids.i32")).ToArray();
        float[] noise = MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(capture + ".input.noise.f32")).ToArray();
        using var metadata = JsonDocument.Parse(File.ReadAllText(capture + ".input.json"));
        long duration = metadata.RootElement.GetProperty("duration").GetInt64();
        int[] shape = metadata.RootElement.GetProperty("noiseShape").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var rows = new List<object>();
        foreach (int threads in new[] { 1, 2, 4 })
        {
            using var options = new SessionOptions { IntraOpNumThreads = threads, InterOpNumThreads = 1 };
            var timer = Stopwatch.StartNew();
            using var prepare = new InferenceSession(Path.Combine(directory, "F5_Preprocess.onnx"), options);
            using var transformer = new InferenceSession(Path.Combine(directory, "F5_Transformer.onnx"), options);
            double loadSeconds = timer.Elapsed.TotalSeconds;
            using var prepared = prepare.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("audio", new DenseTensor<short>(pcm, new[] {1,1,pcm.Length})),
                NamedOnnxValue.CreateFromTensor("text_ids", new DenseTensor<int>(ids, new[] {1,ids.Length})),
                NamedOnnxValue.CreateFromTensor("max_duration", new DenseTensor<long>(new[]{duration},new[]{1}))
            });
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("noise", new DenseTensor<float>(noise, shape)),
                NamedOnnxValue.CreateFromTensor("time_step.1", new DenseTensor<int>(new[]{0},new[]{1}))
            };
            foreach (string name in new[] {"rope_cos_q","rope_sin_q","rope_cos_k","rope_sin_k","cat_mel_text","cat_mel_text_drop"})
                inputs.Add(NamedOnnxValue.CreateFromTensor(name,prepared.First(x => x.Name==name).AsTensor<float>()));
            using (var warmup = transformer.Run(inputs)) { }
            var seconds = new List<double>();
            double checksum = 0;
            for (int repeat = 0; repeat < 3; repeat++)
            {
                timer.Restart();
                using var result = transformer.Run(inputs);
                seconds.Add(timer.Elapsed.TotalSeconds);
                var values = result.First(x => x.Name == "denoised").AsTensor<float>();
                if (values.Any(x => !float.IsFinite(x))) throw new InvalidDataException("Nonfinite transformer result");
                checksum = values.Sum(x => (double)x);
            }
            double median = seconds.Order().ElementAt(1);
            var row = new { threads, loadSeconds, measuredStepSeconds = seconds, medianStepSeconds = median, checksum };
            rows.Add(row);
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            platform = RuntimeInformation.OSDescription,
            logicalProcessors = Environment.ProcessorCount,
            runtime = OrtEnv.Instance().GetVersionString(),
            scope = "FP32 native ONNX transformer, same first-step inputs; one warmup and three measured repetitions per setting. Not full synthesis or a quality assessment.",
            rows
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
