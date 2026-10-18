using Google.Protobuf;
using Onnx;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
if(args.Length!=2) throw new ArgumentException("<source.onnx> <new-output.onnx>; experimental per-output-channel dynamic integer MatMul quantization");
string source=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);
if(source==output || File.Exists(output) || File.Exists(output+"_data")) throw new IOException("Output must be new; original models are never overwritten.");
if(Path.GetDirectoryName(source)!=Path.GetDirectoryName(output)) throw new ArgumentException("Keep the derived graph beside the original to resolve unchanged external initializers.");
using var input=File.OpenRead(source);
var model=ModelProto.Parser.ParseFrom(input);
var constants=model.Graph.Initializer.ToDictionary(x=>x.Name);
var newNodes=new List<NodeProto>();
var newConstants=new List<TensorProto>();
var quantized=new Dictionary<string,(string Weight,string Scale,string Zero)>();
var dynamicInputs=new Dictionary<string,(string Value,string Scale,string Zero)>();
int serial=0,matmuls=0;
string weightsPath=output+"_data";
using(var data=File.Create(weightsPath))
{
    foreach(var node in model.Graph.Node)
    {
        if(node.OpType!="MatMul" || node.Domain.Length!=0 || node.Input.Count!=2 || !constants.TryGetValue(node.Input[1],out var weight) || weight.DataType!=1 || weight.Dims.Count!=2)
        { newNodes.Add(node.Clone()); continue; }
        if(!quantized.TryGetValue(weight.Name,out var q))
        {
            int k=checked((int)weight.Dims[0]),n=checked((int)weight.Dims[1]);
            float[] values=Read(weight,checked(k*n),Path.GetDirectoryName(source)!);
            var scales=new float[n];
            for(int row=0;row<k;row++) for(int col=0;col<n;col++)
            {
                float value=Math.Abs(values[row*n+col]);
                if(!float.IsFinite(value)) throw new InvalidDataException("Nonfinite source weight");
                scales[col]=Math.Max(scales[col],value);
            }
            // Reduced signed range avoids AVX2 U8*S8 intermediate saturation on older CPUs.
            for(int col=0;col<n;col++) scales[col]=scales[col]==0?1:scales[col]/63;
            var bytes=new byte[values.Length];
            for(int row=0;row<k;row++) for(int col=0;col<n;col++)
                bytes[row*n+col]=unchecked((byte)(sbyte)Math.Clamp((int)MathF.Round(values[row*n+col]/scales[col]),-63,63));
            string prefix="__memoirs_q8_w"+serial++;
            q=(prefix,prefix+"_scale",prefix+"_zero");
            long offset=data.Position; data.Write(bytes);
            var tensor=new TensorProto{Name=q.Weight,DataType=3,DataLocation=TensorProto.Types.DataLocation.External};
            tensor.Dims.Add(weight.Dims);
            tensor.ExternalData.Add(new StringStringEntryProto{Key="location",Value=Path.GetFileName(weightsPath)});
            tensor.ExternalData.Add(new StringStringEntryProto{Key="offset",Value=offset.ToString(CultureInfo.InvariantCulture)});
            tensor.ExternalData.Add(new StringStringEntryProto{Key="length",Value=bytes.Length.ToString(CultureInfo.InvariantCulture)});
            newConstants.Add(tensor);
            var scale=new TensorProto{Name=q.Scale,DataType=1,RawData=ByteString.CopyFrom(MemoryMarshal.AsBytes(scales.AsSpan()).ToArray())};
            scale.Dims.Add(n); newConstants.Add(scale);
            newConstants.Add(new TensorProto{Name=q.Zero,DataType=3,RawData=ByteString.CopyFrom(new byte[]{0})});
            quantized.Add(weight.Name,q);
        }
        if(!dynamicInputs.TryGetValue(node.Input[0],out var a))
        {
            string prefix="__memoirs_q8_a"+serial++;
            a=(prefix,prefix+"_scale",prefix+"_zero");
            newNodes.Add(Node(prefix,"DynamicQuantizeLinear",new[]{node.Input[0]},new[]{a.Value,a.Scale,a.Zero}));
            dynamicInputs.Add(node.Input[0],a);
        }
        string stem="__memoirs_q8_mm"+serial++;
        newNodes.Add(Node(stem,"MatMulInteger",new[]{a.Value,q.Weight,a.Zero,q.Zero},new[]{stem+"_int"}));
        var cast=Node(stem+"_cast","Cast",new[]{stem+"_int"},new[]{stem+"_float"});
        cast.Attribute.Add(new AttributeProto{Name="to",Type=AttributeProto.Types.AttributeType.Int,I=1}); newNodes.Add(cast);
        newNodes.Add(Node(stem+"_scale","Mul",new[]{a.Scale,q.Scale},new[]{stem+"_scale"}));
        newNodes.Add(Node(stem+"_output","Mul",new[]{stem+"_float",stem+"_scale"},node.Output.ToArray()));
        matmuls++;
        if(matmuls%25==0) Console.WriteLine($"Quantized {matmuls} constant MatMul nodes");
    }
}
if(matmuls==0) throw new InvalidDataException("No eligible MatMul weights found");
model.Graph.Node.Clear(); model.Graph.Node.Add(newNodes);
foreach(string name in quantized.Keys) if(!newNodes.Any(x=>x.Input.Contains(name))) model.Graph.Initializer.Remove(constants[name]);
model.Graph.Initializer.Add(newConstants);
using(var result=File.Create(output)) model.WriteTo(result);
using var graph=File.OpenRead(source);
File.WriteAllText(output+".json",JsonSerializer.Serialize(new{source,sourceGraphSha256=Convert.ToHexString(SHA256.HashData(graph)),output,matmuls,uniqueQuantizedWeights=quantized.Count,quantizedWeightBytes=new FileInfo(weightsPath).Length,scheme="Experimental dynamic U8 activations / per-output-channel reduced-range signed integer weights; FP32 outputs; no original file modified."},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Wrote {output}; {matmuls} MatMul nodes; {new FileInfo(weightsPath).Length} weight bytes");
static NodeProto Node(string name,string op,string[] inputs,string[] outputs)
{
    var result=new NodeProto{Name=name,OpType=op}; result.Input.Add(inputs); result.Output.Add(outputs); return result;
}
static float[] Read(TensorProto tensor,int count,string directory)
{
    var values=new float[count];
    if(tensor.DataLocation==TensorProto.Types.DataLocation.External)
    {
        var metadata=tensor.ExternalData.ToDictionary(x=>x.Key,x=>x.Value);
        using var file=File.OpenRead(Path.Combine(directory,metadata["location"]));
        file.Position=metadata.TryGetValue("offset",out var offset)?long.Parse(offset,CultureInfo.InvariantCulture):0;
        file.ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()));
    }
    else if(tensor.RawData.Length>0) tensor.RawData.Span.CopyTo(MemoryMarshal.AsBytes(values.AsSpan()));
    else { if(tensor.FloatData.Count!=count) throw new InvalidDataException("Invalid FP32 tensor"); tensor.FloatData.CopyTo(values,0); }
    return values;
}
