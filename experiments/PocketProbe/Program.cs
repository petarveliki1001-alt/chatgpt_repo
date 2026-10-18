using SherpaOnnx;
using MemoirsReader.Native;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY","1");
if(args.Length==1 && args[0]=="--native-info")
{
    Console.WriteLine("Native sherpa-onnx: "+Marshal.PtrToStringUTF8(Native.SherpaOnnxGetVersionStr()));
    Console.WriteLine("No models loaded or downloaded."); return;
}
if(args.Length>0 && args[0]=="download" && (args.Length!=4 || args[3]!="--approved")) throw new ArgumentException("Model download requires explicit user agreement and --approved.");
if(args.Length==4 && args[0]=="download" && args[3]=="--approved")
{
    // Explicit, separate command. Never called by inference or the desktop app.
    var files=JsonSerializer.Deserialize<Asset[]>(File.ReadAllText(args[1]))!;
    Directory.CreateDirectory(args[2]);
    using var client=new HttpClient{Timeout=TimeSpan.FromMinutes(15)};
    foreach(var entry in files)
    {
        string path=Path.Combine(args[2],entry.Path);
        if(File.Exists(path) && Verify(path,entry)) continue;
        Console.WriteLine("Downloading "+entry.Path);
        using var response=await client.GetAsync(entry.Url,HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        string partial=path+".partial";
        try
        {
            await using(var file=File.Create(partial)) await response.Content.CopyToAsync(file);
            if(!Verify(partial,entry)) throw new InvalidDataException("Checksum mismatch: "+entry.Path);
            File.Move(partial,path,true);
        }
        finally { if(File.Exists(partial)) File.Delete(partial); }
    }
    Console.WriteLine("Verified model assets."); return;
}
if(args.Length!=4) throw new ArgumentException("--native-info | download <manifest> <model-dir> --approved | <model-dir> <voice.wav> <text-file> <new-output-prefix>");
string root=Path.GetFullPath(args[0]),voice=Path.GetFullPath(args[1]),text=File.ReadAllText(args[2]),prefix=Path.GetFullPath(args[3]);
if(File.Exists(prefix+"-1.wav")||File.Exists(prefix+"-2.wav")) throw new IOException("Use a new output prefix.");
var config=new OfflineTtsConfig();
config.Model.Pocket.LmFlow=Path.Combine(root,"lm_flow.int8.onnx");
config.Model.Pocket.LmMain=Path.Combine(root,"lm_main.int8.onnx");
config.Model.Pocket.Encoder=Path.Combine(root,"encoder.onnx");
config.Model.Pocket.Decoder=Path.Combine(root,"decoder.int8.onnx");
config.Model.Pocket.TextConditioner=Path.Combine(root,"text_conditioner.onnx");
config.Model.Pocket.VocabJson=Path.Combine(root,"vocab.json");
config.Model.Pocket.TokenScoresJson=Path.Combine(root,"token_scores.json");
config.Model.NumThreads=2; config.Model.Debug=0; config.Model.Provider="cpu";
var (referenceSamples,referenceRate)=WaveAudio.Read(voice);
var generation=new OfflineTtsGenerationConfig{ReferenceAudio=referenceSamples,ReferenceSampleRate=referenceRate};
// Use the complete sample; do not copy the upstream example's arbitrary 12s cap.
generation.Extra["max_reference_audio_len"]=(float)referenceSamples.Length/referenceRate;
var timer=Stopwatch.StartNew();
using var tts=new OfflineTts(config);
double loadSeconds=timer.Elapsed.TotalSeconds;
var reports=new List<object>();
Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);
for(int run=1;run<=2;run++)
{
    timer.Restart();
    var callback=new OfflineTtsCallbackProgressWithArg((samples,n,progress,arg)=>1);
    var audio=tts.GenerateWithConfig(text,generation,callback);
    double seconds=timer.Elapsed.TotalSeconds;
    string path=prefix+$"-{run}.wav";
    if(!audio.SaveToWaveFile(path)) throw new IOException("WAV save failed.");
    var (samples,rate)=WaveAudio.Read(path);
    double durationSeconds=samples.Length/(double)rate;
    reports.Add(new{run,loadSeconds=run==1?loadSeconds:0,seconds,durationSeconds,realtimeFactor=seconds/durationSeconds,path});
}
File.WriteAllText(prefix+".json",JsonSerializer.Serialize(new{runtime=Marshal.PtrToStringUTF8(Native.SherpaOnnxGetVersionStr()),platform=RuntimeInformation.OSDescription,threads=2,reference=voice,text,reports,scope="Candidate benchmark only. No transcript, ASR or Python. Not integrated into the desktop app."},new JsonSerializerOptions{WriteIndented=true}));
static bool Verify(string path,Asset entry)
{
    if(new FileInfo(path).Length!=entry.Size) return false;
    using var file=File.OpenRead(path);
    if(entry.Sha256!=null) return Convert.ToHexString(SHA256.HashData(file)).Equals(entry.Sha256,StringComparison.OrdinalIgnoreCase);
    using var sha=IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
    sha.AppendData(Encoding.UTF8.GetBytes($"blob {entry.Size}\0"));
    byte[] buffer=new byte[1024*1024]; int count;
    while((count=file.Read(buffer))>0) sha.AppendData(buffer.AsSpan(0,count));
    return Convert.ToHexString(sha.GetHashAndReset()).Equals(entry.GitBlobSha1,StringComparison.OrdinalIgnoreCase);
}
record Asset(string Path,string Url,long Size,string? Sha256,string? GitBlobSha1);
static class Native
{
    [DllImport("sherpa-onnx-c-api",CallingConvention=CallingConvention.Cdecl)]
    internal static extern IntPtr SherpaOnnxGetVersionStr();
}
