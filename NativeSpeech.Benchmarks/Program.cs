using MemoirsReader.Native;
using MemoirsReader.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using System.Diagnostics;
using System.Text.Json;
Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY","1");
OrtEnv.Instance().DisableTelemetryEvents();
if(args.Length==1 && args[0]=="--native-info")
{
    Console.WriteLine("ONNX Runtime "+OrtEnv.Instance().GetVersionString()+"; telemetry explicitly disabled; no models loaded.");
    return;
}
if(args.Length>=5 && args[0]=="speaker-scores")
{
    SpeakerDiagnostics.Compare(args[1],args[2],args[3],args.Skip(4).ToArray());
    return;
}
if(args.Length<4) throw new ArgumentException("<chatterbox|f5> <existing-model-root> <reference-wav> <output-directory> [--profile]");
string mode=args[0],models=Path.GetFullPath(args[1]),voice=Path.GetFullPath(args[2]),output=Path.GetFullPath(args[3]);
bool profile=args.Contains("--profile");
bool allowSpinning=!args.Contains("--no-spinning");
bool prepacking=!args.Contains("--no-prepacking");
string? languageModelOverride=args.Contains("--q8")?Path.Combine(models,"chatterbox","onnx","language_model.q8.onnx"):null;
string? decoderCache=args.Contains("--cache-decoder")?Path.Combine(models,"benchmark-cache","decoder.ort"):null;
Directory.CreateDirectory(output);
var timer=Stopwatch.StartNew();
var rows=new List<object>();
string[] traces;
int threads=Math.Clamp(Environment.ProcessorCount,1,4);
const string shortText="The small boat crossed the quiet lake.";
if(mode=="chatterbox")
{
    using var engine=new ProfiledChatterboxEngine(Path.Combine(models,mode),threads,Console.WriteLine,profileDirectory:profile?output:null,decoderCache:decoderCache,allowSpinning:allowSpinning,prepacking:prepacking,languageModelOverride:languageModelOverride);
    double loaded=timer.Elapsed.TotalSeconds;
    const string longerText="The small boat crossed the quiet lake. A gentle wind moved through the trees along the shore. We watched the sunlight fade behind the hills and listened to the water. Later that evening, everyone gathered around the table to share memories of the journey.";
    foreach(var test in (args.Contains("--short-only") ? new[]{("cold-short",shortText),("warm-short",shortText)} : new[]{("cold-short",shortText),("warm-short",shortText),("warm-long",longerText)}))
    {
        var parts=new List<ChatterboxTimings>();
        var audio=new List<float>();
        var tokens=new List<long[]>();
        int before=engine.ReferencePreparations;
        timer.Restart();
        foreach(string text in NarrationChunks.Split(test.Item2))
        {
            var samples=engine.Generate(voice,text,Console.WriteLine,CancellationToken.None);
            parts.Add(engine.LastTimings!); tokens.Add(engine.LastSpeechTokens);
            if(audio.Count>0) audio.AddRange(new float[2400]);
            audio.AddRange(samples);
        }
        double elapsed=timer.Elapsed.TotalSeconds;
        WaveAudio.Write(Path.Combine(output,test.Item1+".wav"),audio.ToArray(),24000);
        var row=new {name=test.Item1,text=test.Item2,loadSeconds=test.Item1=="cold-short"?loaded:0,modelLoadSeconds=engine.ModelLoadSeconds,
            referencePrepared=engine.ReferencePreparations>before,seconds=elapsed,audioSeconds=audio.Count/24000.0,parts,tokens};
        rows.Add(row); Console.WriteLine(JsonSerializer.Serialize(row));
    }
    traces=engine.FinishProfiling();
}
else if(mode=="f5-step")
{
    if(args.Length<5) throw new ArgumentException("f5-step requires an existing captured-input prefix as argument 5.");
    var measured=F5OperatorProfile.Run(Path.Combine(models,"f5"),args[4],output,threads);
    rows.Add(new{scope="Three identical first-step transformer calls with captured PCM/text/noise inputs; not full synthesis.",seconds=measured.Seconds});
    traces=measured.Traces;
}
else if(mode=="f5")
{
    using var engine=new ProfiledF5Engine(Path.Combine(models,mode),Path.Combine(models,"fixtures","f5-vocab.txt"),threads,Console.WriteLine,profileDirectory:profile?output:null);
    double loadSeconds=timer.Elapsed.TotalSeconds; timer.Restart();
    string referenceText;
    int transcriptArgument=Array.IndexOf(args,"--reference-text");
    if(transcriptArgument>=0 && transcriptArgument+1<args.Length) referenceText=File.ReadAllText(args[transcriptArgument+1]);
    else if(AudioIdentity.Key(voice).Equals("b0e22048e72414fcc1e6b6342e47a774d748a195ed34e4a5b3fcf416707f2b71",StringComparison.OrdinalIgnoreCase)) referenceText="Some call me nature. Others call me Mother Nature.";
    else throw new ArgumentException("Supply --reference-text <matching-transcript-file> for this F5 sample. No transcription is performed.");
    var audio=engine.Generate(voice,referenceText,shortText,1,Console.WriteLine,CancellationToken.None);
    double seconds=timer.Elapsed.TotalSeconds;
    WaveAudio.Write(Path.Combine(output,"f5.wav"),audio,24000);
    rows.Add(new {loadSeconds,seconds,audioSeconds=audio.Length/24000.0});
    traces=engine.FinishProfiling();
}
else throw new ArgumentException("Unknown engine");
var summaries=new List<object>();
foreach(string trace in traces)
{
    using var json=JsonDocument.Parse(File.ReadAllText(trace));
    var nodes=json.RootElement.EnumerateArray().Where(x=>x.TryGetProperty("cat",out var cat)&&cat.GetString()=="Node"&&x.GetProperty("name").GetString()!.EndsWith("_kernel_time")&&x.TryGetProperty("args",out var arg)&&arg.TryGetProperty("op_name",out _)).ToArray();
    var ops=nodes.GroupBy(x=>x.GetProperty("args").GetProperty("op_name").GetString()).Select(g=>new {operation=g.Key,calls=g.Count(),seconds=g.Sum(x=>x.GetProperty("dur").GetDouble())/1e6}).OrderByDescending(x=>x.seconds).Take(20).ToArray();
    var expensive=nodes.GroupBy(x=>x.GetProperty("name").GetString()).Select(g=>new {node=g.Key,operation=g.First().GetProperty("args").GetProperty("op_name").GetString(),calls=g.Count(),seconds=g.Sum(x=>x.GetProperty("dur").GetDouble())/1e6}).OrderByDescending(x=>x.seconds).Take(20).ToArray();
    summaries.Add(new {file=Path.GetFileName(trace),ops,expensive});
}
File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new {mode,profile,allowSpinning,prepacking,languageModelOverride,decoderCache,decoderCacheBytes=decoderCache!=null&&File.Exists(decoderCache)?new FileInfo(decoderCache).Length:0,threads,platform=System.Runtime.InteropServices.RuntimeInformation.OSDescription,runtime=OrtEnv.Instance().GetVersionString(),reference=voice,referenceHash=AudioIdentity.Key(voice),rows,summaries},new JsonSerializerOptions{WriteIndented=true}));
