using MemoirsReader.Native;
using System.Text.Json;
if (args.Length == 4 && args[0] == "install")
{
    var installer = new ModelStore(args[2],args[3]);
    await installer.InstallAsync(args[1],Console.WriteLine,CancellationToken.None);
    return;
}
var root=Path.Combine(Path.GetTempPath(),"memoirs-tests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed=0;
void Check(bool condition,string name) { if(!condition) throw new Exception(name); passed++; Console.WriteLine("PASS: "+name); }
void Reject(Action action,string name) { try { action(); } catch(Exception ex) when(ex is ArgumentException or InvalidDataException or OperationCanceledException) { passed++; Console.WriteLine("PASS: "+name); return; } throw new Exception("Did not reject: "+name); }
try
{
    var text="First sentence is spoken completely. Then comes a second sentence, followed by a third sentence. End.";
    var chunks=NarrationChunks.Split(text,40);
    Check(string.Join(" ",chunks)==text && chunks.All(x=>x.Length<=40),"Narration chunks preserve every word in order");
    Reject(()=>NarrationChunks.Split(new string('x',200)),"Unbroken oversized token rejected");
    var path=Path.Combine(root,"sample.wav");
    WaveAudio.Write(path,Enumerable.Range(0,2400).Select(i=>(float)(.2*Math.Sin(i*.1))).ToArray(),24000);
    var (audio,rate)=WaveAudio.Read(path);
    Check(audio.Length==2400 && rate==24000,"Real PCM WAV roundtrip");
    Check(WaveAudio.Resample(audio,24000,16000).Length==1600,"Resampling produces expected sample count");
    string hash=AudioIdentity.Key(path);
    WaveAudio.Write(path,new[]{.1f,.2f,.3f},24000);
    Check(hash!=AudioIdentity.Key(path),"Changed reference content invalidates identity");
    var invalid=Path.Combine(root,"silence.wav"); WaveAudio.Write(invalid,new float[100],24000);
    Reject(()=>WaveAudio.Read(invalid),"Silent reference rejected before model loading");
    Reject(()=>WaveAudio.Write(path,new[]{float.NaN},24000),"Nonfinite generated output rejected");
    string manifest=Path.Combine(root,"models.json");
    File.WriteAllText(manifest,"[]");
    var store=new ModelStore(root,manifest);
    using var narrator=new NativeNarrator(store,1);
    var request=new NarrationRequest("f5",path,"","New narration.",Path.Combine(root,"output.wav"));
    Reject(()=>narrator.Generate(request,_=>{},CancellationToken.None),"F5 cannot silently transcribe a missing transcript");
    using var canceled=new CancellationTokenSource(); canceled.Cancel();
    Reject(()=>narrator.Generate(request,_=>{},canceled.Token),"Cancellation honored before model loading");
    Check(!File.Exists(request.Output),"Rejected jobs create no output WAV");
    var actual=JsonSerializer.Deserialize<ModelFile[]>(File.ReadAllText(args[0]))!;
    Check(actual.All(x=>!x.Path.Contains("whisper",StringComparison.OrdinalIgnoreCase)),"Release manifest excludes Whisper");
    Console.WriteLine($"{passed} checks passed.");
}
finally { Directory.Delete(root,true); }
