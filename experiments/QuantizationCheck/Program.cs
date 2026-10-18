using Google.Protobuf;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Onnx;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
OrtEnv.Instance().DisableTelemetryEvents();
string root=Path.Combine(Path.GetTempPath(),"quant-check-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
try
{
    string source=Path.Combine(root,"fp32.onnx"),quantized=Path.Combine(root,"q8.onnx");
    var model=new ModelProto{IrVersion=8,Graph=new GraphProto{Name="independent_numeric_check"}};
    model.OpsetImport.Add(new OperatorSetIdProto{Domain="",Version=17});
    model.Graph.Input.Add(Info("A",1,4)); model.Graph.Output.Add(Info("Y",1,3));
    var weight=new TensorProto{Name="W",DataType=1}; weight.Dims.Add(new long[]{4,3});
    // Signed values, different per-column ranges, and an all-zero column.
    weight.FloatData.Add(new float[]{.1f,-.2f,0,.9f,.3f,0,-.4f,.6f,0,1,.2f,0});
    model.Graph.Initializer.Add(weight);
    var matmul=new NodeProto{Name="matmul",OpType="MatMul"}; matmul.Input.Add(new[]{"A","W"});matmul.Output.Add("Y");model.Graph.Node.Add(matmul);
    using(var file=File.Create(source)) model.WriteTo(file);
    byte[] original=SHA256.HashData(File.ReadAllBytes(source));
    var psi=new ProcessStartInfo("dotnet"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
    psi.ArgumentList.Add(typeof(ModelProto).Assembly.Location);psi.ArgumentList.Add(source);psi.ArgumentList.Add(quantized);
    using var process=Process.Start(psi)!;
    var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
    if(process.ExitCode!=0) throw new Exception(await stdout+await stderr);
    using var options=new SessionOptions{IntraOpNumThreads=1,InterOpNumThreads=1};
    using var fp32=new InferenceSession(source,options);using var q8=new InferenceSession(quantized,options);
    var errors=new List<double>();
    foreach(float[] values in new[]{new[]{-.75f,.5f,1.25f,-1.5f},new float[4],new[]{1f,1f,1f,1f}})
    {
        var input=new[]{NamedOnnxValue.CreateFromTensor("A",new DenseTensor<float>(values,new[]{1,4}))};
        using var expected=fp32.Run(input);using var actual=q8.Run(input);
        float[] a=expected.First().AsTensor<float>().ToArray(),b=actual.First().AsTensor<float>().ToArray();
        if(a.Length!=3 || b.Length!=3 || b.Any(x=>!float.IsFinite(x)) || b[2]!=0) throw new Exception("Invalid shape/value/zero-weight output");
        double error=a.Zip(b,(x,y)=>(double)Math.Abs(x-y)).Max();
        if(error>.05) throw new Exception("Quantization equation exceeded numeric tolerance: "+error);
        errors.Add(error);
    }
    if(!original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source)))) throw new Exception("Original graph modified");
    string result=JsonSerializer.Serialize(new{passed=true,absoluteErrors=errors,tolerance=.05,originalUnchanged=true,scope="Independent FP32-versus-derived integer MatMul checks: mixed signed inputs, zero activations, constant positive activations, per-column scaling and zero-weight column. Does not certify speech quality."},new JsonSerializerOptions{WriteIndented=true});
    Console.WriteLine(result);if(args.Length==1) File.WriteAllText(args[0],result);
}
finally { Directory.Delete(root,true); }
static ValueInfoProto Info(string name,params long[] dimensions)
{
    var result=new ValueInfoProto{Name=name,Type=new TypeProto{TensorType=new TypeProto.Types.Tensor{ElemType=1,Shape=new TensorShapeProto()}}};
    foreach(long dimension in dimensions) result.Type.TensorType.Shape.Dim.Add(new TensorShapeProto.Types.Dimension{DimValue=dimension});
    return result;
}
