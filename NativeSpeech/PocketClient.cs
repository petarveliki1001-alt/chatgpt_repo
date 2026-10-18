using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace MemoirsReader.Native;

// One retained C# worker, not a Python interpreter. Called serially by NativeNarrator.
public sealed class PocketClient : IDisposable
{
    private readonly string models;
    private readonly int threads;
    private Process? worker;
    private readonly StringBuilder errors=new();
    public PocketClient(string models,int threads) { this.models=models; this.threads=threads; }
    public NarrationReport Generate(NarrationRequest request,Action<string> progress,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(File.Exists(request.Output)) throw new IOException("Output already exists.");
        EnsureStarted();
        using var cancellation=token.Register(()=>StopWorker());
        try
        {
            worker!.StandardInput.WriteLine(JsonSerializer.Serialize(request)); worker.StandardInput.Flush();
            while(true)
            {
                string? line=worker.StandardOutput.ReadLineAsync(token).AsTask().GetAwaiter().GetResult();
                token.ThrowIfCancellationRequested();
                if(line==null)
                {
                    string details; lock(errors) details=errors.ToString();
                    throw new InvalidOperationException("PocketTTS worker stopped. "+details);
                }
                using var json=JsonDocument.Parse(line);
                if(json.RootElement.TryGetProperty("progress",out var phase)) progress(phase.GetString()!);
                if(json.RootElement.TryGetProperty("error",out var error)) throw new InvalidOperationException(error.GetString());
                if(!json.RootElement.TryGetProperty("report",out var result)) continue;
                var report=result.Deserialize<NarrationReport>()!;
                WaveAudio.Read(request.Output);
                token.ThrowIfCancellationRequested();
                File.WriteAllText(request.Output+".json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
                File.WriteAllText(request.Output+".txt",$"Engine: pocket (native CPU)\nReference: {request.Reference}\nText: {request.Text}\nLoading: {report.LoadSeconds:F2}s\nSynthesis: {report.SynthesisSeconds:F2}s\nAudio duration: {report.DurationSeconds:F2}s\n");
                progress($"Saved {report.DurationSeconds:F2}s audio; loading {report.LoadSeconds:F2}s; synthesis {report.SynthesisSeconds:F2}s");
                return report;
            }
        }
        catch
        {
            StopWorker();
            if(token.IsCancellationRequested)
            {
                if(File.Exists(request.Output)) File.Delete(request.Output);
                token.ThrowIfCancellationRequested();
            }
            throw;
        }
        finally { if(File.Exists(request.Output+".partial")) File.Delete(request.Output+".partial"); }
    }
    private void EnsureStarted()
    {
        if(worker is {HasExited:false}) return;
        worker?.Dispose();
        string folder=Environment.GetEnvironmentVariable("MEMOIRS_POCKET_WORKER") ?? Path.Combine(AppContext.BaseDirectory,"SpeechWorker");
        string executable=Path.Combine(folder,OperatingSystem.IsWindows()?"PocketWorker.exe":"PocketWorker");
        if(!File.Exists(executable)) throw new FileNotFoundException("Build MemoirsReader.sln to create the PocketTTS worker.",executable);
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.StandardInputEncoding=new UTF8Encoding(false); start.StandardOutputEncoding=new UTF8Encoding(false);
        start.ArgumentList.Add(models); start.ArgumentList.Add(threads.ToString());
        start.Environment["ORT_DISABLE_TELEMETRY"]="1";
        lock(errors) errors.Clear();
        worker=new Process{StartInfo=start};
        worker.ErrorDataReceived+=(_,e)=>{if(e.Data!=null) lock(errors) { errors.AppendLine(e.Data); if(errors.Length>12000) errors.Remove(0,errors.Length-12000); }};
        worker.Start(); worker.BeginErrorReadLine();
    }
    private void StopWorker()
    {
        try { if(worker is {HasExited:false}) { worker.Kill(true); worker.WaitForExit(); } } catch(InvalidOperationException) { }
    }
    public void Dispose() { StopWorker(); worker?.Dispose(); worker=null; }
}
