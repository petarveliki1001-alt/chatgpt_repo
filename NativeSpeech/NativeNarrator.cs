using System.Diagnostics;
using System.Text.Json;
namespace MemoirsReader.Native;

// One job at a time. Retains the active engine, releasing it when switching engines.
public sealed class NativeNarrator : IDisposable
{
    private readonly ModelStore models;
    private readonly int threads;
    private ChatterboxEngine? chatterbox;
    private F5Engine? f5;
    private PocketClient? pocket;
    public NativeNarrator(ModelStore models, int? threads = null)
    {
        this.models = models;
        this.threads = threads ?? Math.Clamp(Environment.ProcessorCount,1,4);
        if (this.threads < 1) throw new ArgumentOutOfRangeException(nameof(threads));
    }
    public NarrationReport Generate(NarrationRequest request, Action<string> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.Engine != "chatterbox" && request.Engine != "f5" && request.Engine != "pocket") throw new ArgumentException("Unknown speech engine.");
        if (string.IsNullOrWhiteSpace(request.Text)) throw new ArgumentException("Enter narration text.");
        if (!File.Exists(request.Reference)) throw new ArgumentException("Select a voice sample.");
        if (request.Engine == "f5" && string.IsNullOrWhiteSpace(request.ReferenceTranscript))
            throw new ArgumentException("F5 needs the words spoken in the sample. Use Chatterbox for audio-only voice conditioning.");
        WaveAudio.Read(request.Reference); // Reject invalid input before loading any large models.
        if (!models.IsPresent(request.Engine)) throw new InvalidOperationException("Set up " + request.Engine + " models first.");
        if (request.Engine == "pocket")
        {
            chatterbox?.Dispose(); chatterbox=null; f5?.Dispose(); f5=null;
            pocket ??= new PocketClient(Path.Combine(models.Root,"pocket"),Math.Min(threads,2));
            return pocket.Generate(request,progress,token);
        }
        pocket?.Dispose(); pocket=null;
        bool reusedModel = request.Engine == "chatterbox" ? chatterbox != null : f5 != null;
        var timer = Stopwatch.StartNew();
        progress($"CPU threads: {threads}; model {(reusedModel ? "reused" : "loading")}");
        if (request.Engine == "chatterbox")
        {
            f5?.Dispose(); f5=null;
            chatterbox ??= new ChatterboxEngine(Path.Combine(models.Root,"chatterbox"), threads, progress, token);
        }
        else
        {
            chatterbox?.Dispose(); chatterbox=null;
            f5 ??= new F5Engine(Path.Combine(models.Root,"f5"),Path.Combine(models.Root,"fixtures","f5-vocab.txt"),threads,progress,token);
        }
        double loadSeconds = timer.Elapsed.TotalSeconds;
        int preparationsBefore = request.Engine == "chatterbox" ? chatterbox!.ReferencePreparations : f5!.ReferencePreparations;
        timer.Restart();
        var audio = new List<float>();
        var timings = new List<double>();
        var chunks = request.Engine == "chatterbox" ? NarrationChunks.Split(request.Text) : new[]{request.Text};
        for (int i=0;i<chunks.Count;i++)
        {
            token.ThrowIfCancellationRequested();
            progress($"Narration section {i+1}/{chunks.Count}");
            var chunkTimer = Stopwatch.StartNew();
            float[] section = request.Engine == "chatterbox"
                ? chatterbox!.Generate(request.Reference,chunks[i],progress,token)
                : f5!.Generate(request.Reference,request.ReferenceTranscript,chunks[i],request.Speed,progress,token);
            timings.Add(chunkTimer.Elapsed.TotalSeconds);
            if (i > 0) audio.AddRange(new float[2400]); // 100 ms between sections, no overlapping words.
            audio.AddRange(section);
        }
        token.ThrowIfCancellationRequested();
        double synthesisSeconds = timer.Elapsed.TotalSeconds;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.Output))!);
        string partial = request.Output + ".partial";
        try
        {
            WaveAudio.Write(partial,audio.ToArray(),24000);
            token.ThrowIfCancellationRequested();
            File.Move(partial,request.Output,false);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
        int preparationsAfter = request.Engine == "chatterbox" ? chatterbox!.ReferencePreparations : f5!.ReferencePreparations;
        var report = new NarrationReport(request.Engine,threads,request.Reference,request.ReferenceTranscript,request.Text,reusedModel,
            preparationsBefore > 0 && preparationsBefore == preparationsAfter,loadSeconds,synthesisSeconds,audio.Count/24000.0,timings.ToArray());
        File.WriteAllText(request.Output+".json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(request.Output+".txt",$"Engine: {request.Engine} (native)\nReference: {request.Reference}\nReference transcript: {request.ReferenceTranscript}\nText: {request.Text}\nLoading: {loadSeconds:F2}s\nSynthesis: {synthesisSeconds:F2}s\nAudio duration: {report.DurationSeconds:F2}s\n");
        progress($"Saved {report.DurationSeconds:F2}s audio; loading {loadSeconds:F2}s; synthesis {synthesisSeconds:F2}s");
        return report;
    }
    public void Dispose() { chatterbox?.Dispose(); f5?.Dispose(); pocket?.Dispose(); }
}
