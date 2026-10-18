using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace MemoirsReader.Diagnostics;
static class F5OperatorProfile
{
    public static (string[] Traces, double[] Seconds) Run(string directory,string capture,string output,int threads)
    {
        using var options=new SessionOptions{IntraOpNumThreads=threads,InterOpNumThreads=1};
        using var prepare=new InferenceSession(Path.Combine(directory,"F5_Preprocess.onnx"),options);
        using var profileOptions=new SessionOptions{IntraOpNumThreads=threads,InterOpNumThreads=1};
        profileOptions.ProfileOutputPathPrefix=Path.Combine(output,"f5-transformer-step");
        profileOptions.EnableProfiling=true;
        using var transformer=new InferenceSession(Path.Combine(directory,"F5_Transformer.onnx"),profileOptions);
        short[] pcm=MemoryMarshal.Cast<byte,short>(File.ReadAllBytes(capture+".input.pcm16")).ToArray();
        int[] ids=MemoryMarshal.Cast<byte,int>(File.ReadAllBytes(capture+".input.ids.i32")).ToArray();
        float[] noise=MemoryMarshal.Cast<byte,float>(File.ReadAllBytes(capture+".input.noise.f32")).ToArray();
        using var metadata=JsonDocument.Parse(File.ReadAllText(capture+".input.json"));
        long duration=metadata.RootElement.GetProperty("duration").GetInt64();
        int[] shape=metadata.RootElement.GetProperty("noiseShape").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
        using var prepared=prepare.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("audio",new DenseTensor<short>(pcm,new[]{1,1,pcm.Length})),
            NamedOnnxValue.CreateFromTensor("text_ids",new DenseTensor<int>(ids,new[]{1,ids.Length})),
            NamedOnnxValue.CreateFromTensor("max_duration",new DenseTensor<long>(new[]{duration},new[]{1}))
        });
        var inputs=new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("noise",new DenseTensor<float>(noise,shape)),
            NamedOnnxValue.CreateFromTensor("time_step.1",new DenseTensor<int>(new[]{0},new[]{1}))
        };
        foreach(string name in new[]{"rope_cos_q","rope_sin_q","rope_cos_k","rope_sin_k","cat_mel_text","cat_mel_text_drop"})
            inputs.Add(NamedOnnxValue.CreateFromTensor(name,prepared.First(x=>x.Name==name).AsTensor<float>()));
        var seconds=new List<double>();
        for(int repeat=0;repeat<3;repeat++)
        {
            var timer=Stopwatch.StartNew();
            using var result=transformer.Run(inputs);
            seconds.Add(timer.Elapsed.TotalSeconds);
            if(result.First(x=>x.Name=="denoised").AsTensor<float>().Any(x=>!float.IsFinite(x))) throw new InvalidDataException("Invalid transformer output");
        }
        return (new[]{transformer.EndProfiling()},seconds.ToArray());
    }
}
