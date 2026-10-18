using MemoirsReader.Native;
using SherpaOnnx;
using System.Diagnostics;
using System.Text.Json;

// A separate native process keeps sherpa's runtime apart from the older engines' runtime.
// Stdout is a line-delimited JSON protocol. Native diagnostics remain on stderr.
if (args.Length != 2) throw new ArgumentException("Expected model directory and CPU thread count.");
Console.InputEncoding=new System.Text.UTF8Encoding(false); Console.OutputEncoding=new System.Text.UTF8Encoding(false);
string root=Path.GetFullPath(args[0]);
int threads=Math.Clamp(int.Parse(args[1]),1,2);
OfflineTts? tts=null;
string? referenceKey=null;
float[]? reference=null;
int referenceRate=0;
try
{
    while (Console.ReadLine() is { } line)
    {
        string? partial=null;
        try
        {
            var request=JsonSerializer.Deserialize<NarrationRequest>(line.TrimStart('\uFEFF')) ?? throw new InvalidDataException("Missing request.");
            if (request.Engine!="pocket" || string.IsNullOrWhiteSpace(request.Text)) throw new ArgumentException("Invalid narration request.");
            if (File.Exists(request.Output)) throw new IOException("Output already exists.");
            bool reusedModel=tts!=null;
            var timer=Stopwatch.StartNew();
            if (tts==null)
            {
                Emit(new {progress="Loading PocketTTS CPU model"});
                var config=new OfflineTtsConfig();
                config.Model.Pocket.LmFlow=Path.Combine(root,"lm_flow.int8.onnx");
                config.Model.Pocket.LmMain=Path.Combine(root,"lm_main.int8.onnx");
                config.Model.Pocket.Encoder=Path.Combine(root,"encoder.onnx");
                config.Model.Pocket.Decoder=Path.Combine(root,"decoder.int8.onnx");
                config.Model.Pocket.TextConditioner=Path.Combine(root,"text_conditioner.onnx");
                config.Model.Pocket.VocabJson=Path.Combine(root,"vocab.json");
                config.Model.Pocket.TokenScoresJson=Path.Combine(root,"token_scores.json");
                config.Model.NumThreads=threads; config.Model.Provider="cpu"; config.Model.Debug=0;
                tts=new OfflineTts(config);
            }
            double loadSeconds=timer.Elapsed.TotalSeconds;
            timer.Restart();
            string key=AudioIdentity.Key(request.Reference);
            bool reusedReference=referenceKey==key;
            if(!reusedReference)
            {
                (reference,referenceRate)=WaveAudio.Read(request.Reference);
                referenceKey=key;
            }
            var generation=new OfflineTtsGenerationConfig{ReferenceAudio=reference!,ReferenceSampleRate=referenceRate};
            generation.Extra["max_reference_audio_len"]=(float)reference!.Length/referenceRate;
            Emit(new {progress="PocketTTS generating narration on CPU"});
            var callback=new OfflineTtsCallbackProgressWithArg((samples,n,progress,arg)=>1);
            var audio=tts.GenerateWithConfig(request.Text,generation,callback);
            double synthesisSeconds=timer.Elapsed.TotalSeconds;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.Output))!);
            partial=request.Output+".partial";
            if(!audio.SaveToWaveFile(partial)) throw new IOException("WAV save failed.");
            var (samples,rate)=WaveAudio.Read(partial);
            File.Move(partial,request.Output,false);
            var report=new NarrationReport("pocket",threads,request.Reference,"",request.Text,reusedModel,reusedReference,
                loadSeconds,synthesisSeconds,samples.Length/(double)rate,new[]{synthesisSeconds});
            Emit(new {report});
        }
        catch(Exception ex) { Emit(new {error=ex.ToString()}); }
        finally { if(partial!=null && File.Exists(partial)) File.Delete(partial); }
    }
}
finally { tts?.Dispose(); }
static void Emit(object message) { Console.WriteLine(JsonSerializer.Serialize(message)); Console.Out.Flush(); }
