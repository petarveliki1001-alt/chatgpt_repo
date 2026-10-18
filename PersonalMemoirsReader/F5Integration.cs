using MemoirsReader.Native;
namespace PersonalMemoirsReader;
public partial class MainForm
{
    private ModelStore? modelStore;
    private NativeNarrator? narrator;
    private CancellationTokenSource? generationCancellation;
    private bool closeAfterJob;
    private string modelRoot = Environment.GetEnvironmentVariable("MEMOIRS_MODEL_ROOT") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MemoirsReader","NativeModels");
    private string TranscriptFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MemoirsReader","Transcripts");
    private void InitializeNative()
    {
        modelStore = new ModelStore(modelRoot,Path.Combine(AppContext.BaseDirectory,"models.json"));
        narrator = new NativeNarrator(modelStore);
    }
    private void LoadSavedTranscript()
    {
        textReference.Clear();
        if (selectedFile == null) return;
        try
        {
            string file = Path.Combine(TranscriptFolder,AudioIdentity.Key(selectedFile)+".txt");
            if (File.Exists(file)) textReference.Text=File.ReadAllText(file);
        }
        catch (IOException) { }
    }
    private void SaveTranscript()
    {
        if (selectedFile == null || string.IsNullOrWhiteSpace(textReference.Text)) return;
        Directory.CreateDirectory(TranscriptFolder);
        File.WriteAllText(Path.Combine(TranscriptFolder,AudioIdentity.Key(selectedFile)+".txt"),textReference.Text);
    }
    private void listSamples_Format(object? sender, ListControlConvertEventArgs e)
    {
        if (e.ListItem is string path) e.Value=Path.GetFileName(path);
    }
    private void SetAiBusy(bool busy)
    {
        generating=busy;
        buttonF5.Enabled=buttonF5Setup.Enabled=buttonChatterbox.Enabled=buttonChatterboxSetup.Enabled=!busy;
        textReference.Enabled=textMemoir.Enabled=textProfile.Enabled=!busy;
        buttonOpenText.Enabled=numericF5Speed.Enabled=listSamples.Enabled=buttonDelete.Enabled=buttonImport.Enabled=!busy;
        buttonRecord.Enabled=!busy && recorder==null;
        buttonGenerate.Enabled=!busy && speech!=null;
        buttonCancelChatterbox.Enabled=busy || generatedPlayer!=null;
    }
    private async void buttonChatterbox_Click(object? sender, EventArgs e) => await GenerateNativeAsync("chatterbox",true);
    private async void buttonF5_Click(object? sender, EventArgs e) => await GenerateNativeAsync("f5",true);
    private async void buttonF5Setup_Click(object? sender, EventArgs e) => await InstallModelsAsync("f5");
    private async void buttonChatterboxSetup_Click(object? sender, EventArgs e) => await InstallModelsAsync("chatterbox");
    private async Task InstallModelsAsync(string engine)
    {
        if (generating || modelStore==null) return;
        double gigabytes=modelStore.Files(engine).Sum(x=>x.Size)/1e9;
        if (MessageBox.Show(this,$"Download and verify the native {engine} speech models ({gigabytes:F2} GB) from Hugging Face?\n\nNo Python or transcription model is installed. Generation is offline after setup. Existing Python installations are untouched.","Speech model setup",MessageBoxButtons.YesNo)!=DialogResult.Yes) return;
        SetAiBusy(true); generationCancellation=new();
        try { BeginRun(engine+"_Setup.log"); await Task.Run(()=>modelStore.InstallAsync(engine,LogNative,generationCancellation.Token)); labelStatus.Text=engine+" models verified and ready."; }
        catch (OperationCanceledException) { LogNative("Setup canceled."); labelStatus.Text="Setup canceled. Verified downloads are retained."; }
        catch (Exception ex) { LogNative(ex.ToString()); labelStatus.Text="Setup failed. Open Speech Log."; MessageBox.Show(this,ex.Message,"Model setup"); }
        finally { FinishRun(); }
    }
    private void BeginRun(string filename)
    {
        generatedPlayer?.Stop(); StopSamplePlayback();
        try { speech?.Speak("",3); } catch { }
        string dir=Path.Combine(dataRoot,"Narrations"); Directory.CreateDirectory(dir);
        latestChatterboxLog=Path.Combine(dir,filename);
        File.WriteAllText(latestChatterboxLog,$"Memoirs Reader {Application.ProductVersion}; native CPU engine; no Python or ASR\n");
        chatterboxStarted=DateTime.Now; chatterboxPhase="Starting"; chatterboxTimer.Start();
    }
    private readonly object logLock=new();
    private void LogNative(string message)
    {
        lock(logLock)
        {
            if(latestChatterboxLog!=null) File.AppendAllText(latestChatterboxLog,$"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }
        if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(()=>chatterboxPhase=message));
    }
    private void FinishRun()
    {
        chatterboxTimer.Stop(); generationCancellation?.Dispose(); generationCancellation=null;
        SetAiBusy(false);
        if(closeAfterJob) Close();
    }
    private async Task<NarrationReport?> GenerateNativeAsync(string engine, bool play, bool showErrors=true)
    {
        if (generating || modelStore==null || narrator==null) return null;
        if (recorder!=null) { if(showErrors) MessageBox.Show(this,"Stop the recording before generating narration."); return null; }
        if (selectedFile==null || string.IsNullOrWhiteSpace(textMemoir.Text))
        { if(showErrors) MessageBox.Show(this,"Select a voice sample and enter narration text."); return null; }
        if(engine=="f5" && string.IsNullOrWhiteSpace(textReference.Text))
        { if(showErrors) MessageBox.Show(this,"F5 requires the words spoken in this sample. Enter them once below; they will be saved for reuse. Chatterbox needs no transcript. No automatic transcription is included."); return null; }
        if(!modelStore.IsPresent(engine))
        { if(showErrors) MessageBox.Show(this,"Use Set up "+engine+" to download its native models once."); return null; }
        string output=Path.Combine(dataRoot,"Narrations",engine+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmssfff")+"_"+Guid.NewGuid().ToString("N")[..6]+".wav");
        var request=new NarrationRequest(engine,selectedFile,textReference.Text,textMemoir.Text,output,(double)numericF5Speed.Value);
        SetAiBusy(true); generationCancellation=new();
        try
        {
            BeginRun(engine+"_LastRun.log");
            if(engine=="f5") SaveTranscript();
            var report=await Task.Run(()=>narrator.Generate(request,LogNative,generationCancellation.Token));
            if(play)
            {
                generatedPlayer?.Dispose(); generatedPlayer=new System.Media.SoundPlayer(output);
                generatedPlayer.Load(); generatedPlayer.Play();
            }
            labelStatus.Text=$"Saved {report.DurationSeconds:F1}s audio. Load {report.LoadSeconds:F1}s; synthesis {report.SynthesisSeconds:F1}s. Compare WAV Files to listen against the sample.";
            return report;
        }
        catch(OperationCanceledException) { LogNative("Canceled; no incomplete WAV accepted."); labelStatus.Text="Generation canceled."; if(!showErrors) throw; return null; }
        catch(Exception ex) { LogNative(ex.ToString()); labelStatus.Text="Generation failed. Open Speech Log."; if(!showErrors) throw; MessageBox.Show(this,ex.Message,"Speech generation"); return null; }
        finally { FinishRun(); }
    }
}
