using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text.Json;

using MemoirsReader.Native;
namespace MemoirsReader.Diagnostics;
static class SpeakerDiagnostics
{
    public static void Compare(string model, string reference, string output, string[] audioFiles)
    {
        using var options = new SessionOptions { IntraOpNumThreads = 4, InterOpNumThreads = 1 };
        using var encoder = new InferenceSession(model, options);
        float[] Encode(string path)
        {
            var (samples, rate) = WaveAudio.Read(path);
            samples = WaveAudio.Resample(samples, rate, 24000);
            using var result = encoder.Run(new[] {NamedOnnxValue.CreateFromTensor("audio_values",new DenseTensor<float>(samples,new[]{1,samples.Length}))});
            return result.ElementAt(2).AsTensor<float>().ToArray();
        }
        var original = Encode(reference);
        var rows = audioFiles.Select(file =>
        {
            var vector = Encode(file);
            double dot=0, a=0, b=0;
            for (int i=0;i<vector.Length;i++) {dot+=(double)original[i]*vector[i];a+=(double)original[i]*original[i];b+=(double)vector[i]*vector[i];}
            return new {file,cosine=dot/Math.Sqrt(a*b)};
        }).ToArray();
        string json=JsonSerializer.Serialize(new {reference,method="Chatterbox reference-speaker embeddings; diagnostic similarity only, not a listening-quality score",comparisons=rows},new JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(output,json);Console.WriteLine(json);
    }
}
