using System.Diagnostics;
using System.Media;
using System.Text;

namespace PersonalMemoirsReader;
public partial class MainForm : Form
{
    private readonly string dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Personal Memoirs Reader");
    private WaveRecorder? recorder;
    private string? pendingRecording;
    private string? selectedFile;
    private bool generating;
    private Process? chatterboxProcess;
    private SoundPlayer? generatedPlayer;
    private string? latestChatterboxLog;
    private readonly System.Windows.Forms.Timer chatterboxTimer = new() { Interval = 1000 };
    private DateTime chatterboxStarted;
    private string chatterboxPhase = "Starting";
    private SoundPlayer? samplePlayer;
    private dynamic? speech;
    public MainForm() { InitializeComponent(); RefreshSamples(); InitializeSpeech(); chatterboxTimer.Tick += (_, _) => { if (generating) labelStatus.Text = $"AI engine: {chatterboxPhase} ({(int)(DateTime.Now - chatterboxStarted).TotalSeconds}s). Details: AI Log"; }; }
    private string ProfileFolder
    {
        get
        {
            string name = textProfile.Text.Trim();
            if (name.Length == 0) name = "My Voice";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return Path.Combine(dataRoot, "Voices", name);
        }
    }
    private void RefreshSamples(string? preferred = null)
    {
        string? previously = preferred ?? selectedFile;
        listSamples.Items.Clear();
        if (Directory.Exists(ProfileFolder))
            foreach (string file in Directory.GetFiles(ProfileFolder, "*.wav").OrderBy(Path.GetFileName))
                listSamples.Items.Add(file);
        if (previously != null && listSamples.Items.Contains(previously)) listSamples.SelectedItem = previously;
        else if (listSamples.Items.Count > 0) listSamples.SelectedIndex = 0;
        else selectedFile = null;
        UpdateSelection();
    }
    private void UpdateSelection()
    {
        StopSamplePlayback();
        string? previousSelection = selectedFile;
        selectedFile = listSamples.SelectedItem as string;
        if (previousSelection != selectedFile && referenceStart != null)
        {
            referenceStart.Value = referenceEnd.Value = 0;
            textReference.Clear();
        }
        labelSelected.Text = selectedFile == null ? "Selected: none" : "Selected: " + Path.GetFileName(selectedFile);
        buttonListen.Enabled = selectedFile != null;
        buttonStopListen.Enabled = false;
        buttonDelete.Enabled = selectedFile != null;
    }
    private void listSamples_SelectedIndexChanged(object? sender, EventArgs e) => UpdateSelection();
    private void buttonRecord_Click(object? sender, EventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ProfileFolder);
            pendingRecording = Path.Combine(ProfileFolder, $"sample_{DateTime.Now:yyyyMMdd_HHmmssfff}.wav");
            recorder = new WaveRecorder();
            recorder.Start(pendingRecording);
            buttonRecord.Enabled = false;
            buttonStop.Enabled = true;
            labelStatus.Text = "Recording 44.1 kHz, 16-bit mono PCM from Windows default recording device.";
        }
        catch (Exception ex)
        {
            recorder?.Dispose(); recorder = null;
            buttonRecord.Enabled = true; buttonStop.Enabled = false;
            MessageBox.Show(this, ex.Message, "Recording failed");
        }
    }
    private void buttonStop_Click(object? sender, EventArgs e)
    {
        try
        {
            recorder?.Stop(); recorder?.Dispose(); recorder = null;
            RefreshSamples(pendingRecording);
            labelStatus.Text = "Recording saved. Compare its sound with Windows Sound Recorder.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Recording failed"); }
        finally { buttonRecord.Enabled = true; buttonStop.Enabled = false; }
    }
    private async void buttonImport_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Filter = "Audio (*.wav;*.mp3;*.m4a;*.flac;*.aac;*.ogg)|*.wav;*.mp3;*.m4a;*.flac;*.aac;*.ogg|All files (*.*)|*.*", Title = "Import voice sample" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Directory.CreateDirectory(ProfileFolder);
            string target = Path.Combine(ProfileFolder, $"import_{DateTime.Now:yyyyMMdd_HHmmssfff}.wav");
            if (Path.GetExtension(dialog.FileName).Equals(".wav", StringComparison.OrdinalIgnoreCase)) File.Copy(dialog.FileName, target);
            else
            {
                string ffmpeg = textFfmpeg.Text.Trim();
                if (!File.Exists(ffmpeg)) throw new InvalidOperationException("For non-WAV imports, select your local ffmpeg.exe first. No download is performed by this application.");
                buttonImport.Enabled = false;
                var psi = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                foreach (string arg in new[] { "-y", "-i", dialog.FileName, "-vn", "-ac", "1", "-ar", "44100", "-c:a", "pcm_s16le", target }) psi.ArgumentList.Add(arg);
                using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start FFmpeg.");
                string errors = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();
                if (proc.ExitCode != 0) throw new InvalidOperationException("FFmpeg import failed: " + errors[^Math.Min(errors.Length, 700)..]);
            }
            RefreshSamples(target); labelStatus.Text = "Imported locally: " + Path.GetFileName(dialog.FileName);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Import failed"); }
        finally { buttonImport.Enabled = true; }
    }
    private void buttonFfmpeg_Click(object? sender, EventArgs e)
    {
        using var d = new OpenFileDialog { Filter = "FFmpeg executable (ffmpeg.exe)|ffmpeg.exe" };
        if (d.ShowDialog(this) == DialogResult.OK) textFfmpeg.Text = d.FileName;
    }
    private void StopSamplePlayback()
    {
        try { samplePlayer?.Stop(); samplePlayer?.Dispose(); } catch { }
        samplePlayer = null;
        buttonStopListen.Enabled = false;
    }
    private void buttonListen_Click(object? sender, EventArgs e)
    {
        if (selectedFile == null) return;
        try
        {
            StopSamplePlayback();
            samplePlayer = new SoundPlayer(selectedFile);
            samplePlayer.Load();
            samplePlayer.Play(); // One playback only; never PlayLooping or PlaySync.
            buttonStopListen.Enabled = true;
        }
        catch (Exception ex) { StopSamplePlayback(); MessageBox.Show(this, ex.Message, "Playback failed"); }
    }
    private void buttonStopListen_Click(object? sender, EventArgs e) => StopSamplePlayback();
    private void buttonDelete_Click(object? sender, EventArgs e)
    {
        if (selectedFile == null) return;
        if (MessageBox.Show(this, "Delete selected sample?\n" + Path.GetFileName(selectedFile), "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try { StopSamplePlayback(); File.Delete(selectedFile); selectedFile = null; RefreshSamples(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Delete failed"); }
    }
    private void buttonOpenText_Click(object? sender, EventArgs e)
    {
        using var d = new OpenFileDialog { Filter = "Text files (*.txt)|*.txt" };
        if (d.ShowDialog(this) == DialogResult.OK) textMemoir.Text = File.ReadAllText(d.FileName);
    }
    private void buttonSaveText_Click(object? sender, EventArgs e)
    {
        using var d = new SaveFileDialog { Filter = "Text files (*.txt)|*.txt", FileName = "memoir.txt" };
        if (d.ShowDialog(this) == DialogResult.OK) File.WriteAllText(d.FileName, textMemoir.Text);
    }
    private void textProfile_Leave(object? sender, EventArgs e) { selectedFile = null; RefreshSamples(); }
    // Windows SAPI 5 desktop voice engine. Offline speech, NOT voice cloning.
    private void InitializeSpeech()
    {
        try
        {
            Type type = Type.GetTypeFromProgID("SAPI.SpVoice") ?? throw new InvalidOperationException("Windows SAPI 5 is not installed.");
            speech = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Could not create SAPI voice engine.");
            dynamic tokens = speech.GetVoices();
            int count = (int)tokens.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic token = tokens.Item(i);
                comboVoices.Items.Add((string)token.GetDescription());
            }
            if (comboVoices.Items.Count > 0) comboVoices.SelectedIndex = 0;
            labelStatus.Text = count > 0 ? "Windows offline speech ready. This is not voice cloning." : "No Windows desktop SAPI voices installed.";
            buttonGenerate.Enabled = count > 0;
        }
        catch (Exception ex)
        {
            buttonGenerate.Enabled = false;
            labelStatus.Text = "Speech engine unavailable: " + ex.Message;
        }
    }
    private void buttonGenerate_Click(object? sender, EventArgs e)
    {
        if (speech == null || comboVoices.SelectedIndex < 0) { MessageBox.Show(this, "No Windows SAPI voice available."); return; }
        if (string.IsNullOrWhiteSpace(textMemoir.Text)) { MessageBox.Show(this, "Enter text to read."); return; }
        try
        {
            StopSamplePlayback();
            // Purge previous asynchronous speech before starting another reading.
            speech!.Speak("", 3);
            dynamic voices = speech.GetVoices();
            speech.Voice = voices.Item(comboVoices.SelectedIndex);
            speech.Speak(textMemoir.Text, 1); // SVSFlagsAsync, no looping
            labelStatus.Text = "Reading using installed Windows voice: " + comboVoices.SelectedItem;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Speech failed"); }
    }
    private void buttonStopSpeech_Click(object? sender, EventArgs e)
    {
        try { if (speech != null) speech.Speak("", 3); } catch { }
        labelStatus.Text = "Reading stopped.";
    }
    private async void buttonChatterbox_Click(object? sender, EventArgs e)
    {
        if (generating) return;
        if (string.IsNullOrWhiteSpace(textMemoir.Text)) { MessageBox.Show(this, "Enter memoir text first."); return; }
        string? python = FindChatterboxPython();
        if (python == null) { MessageBox.Show(this, "Chatterbox Python installation was not found.", "Local engine missing"); return; }
        string model = Path.Combine(Directory.GetParent(Directory.GetParent(Directory.GetParent(python)!.FullName)!.FullName)!.FullName, "Models", "English");
        if (!Directory.Exists(model)) { MessageBox.Show(this, "Local model directory not found: " + model); return; }
        string worker = Path.Combine(AppContext.BaseDirectory, "chatterbox_worker.py");
        if (!File.Exists(worker)) { MessageBox.Show(this, "Bundled speech worker is missing: " + worker); return; }
        string outputDir = Path.Combine(dataRoot, "Narrations");
        Directory.CreateDirectory(outputDir);
        string output = Path.Combine(outputDir, "narration_" + DateTime.Now.ToString("yyyyMMdd_HHmmssfff") + ".wav");
        string textPath = Path.Combine(Path.GetTempPath(), "pmr_" + Guid.NewGuid().ToString("N") + ".txt");
        latestChatterboxLog = Path.Combine(outputDir, "Chatterbox_LastRun.log");
        File.WriteAllText(textPath, textMemoir.Text, Encoding.UTF8);
        SetAiBusy(true);
        chatterboxStarted = DateTime.Now; chatterboxPhase = "starting Python engine";
        chatterboxTimer.Start();
        labelStatus.Text = "Chatterbox: starting local engine...";
        var logLines = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void AddLog(string message)
        {
            logLines.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => {
                chatterboxPhase = message.Length > 85 ? message[..85] : message;
            }));
        }
        try
        {
            AddLog("Python: " + python);
            AddLog("Model: " + model);
            AddLog("Worker: " + worker);
            AddLog("Output: " + output);
            AddLog("Fresh Chatterbox generation: uses temporary sections, never reads MemoirCheckpoints.");
            var psi = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("-u"); psi.ArgumentList.Add(worker);
            psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(model);
            psi.ArgumentList.Add("--text-file"); psi.ArgumentList.Add(textPath);
            psi.ArgumentList.Add("--output"); psi.ArgumentList.Add(output);
            // Original, successful Chatterbox baseline. Experimental presets removed.
            var voiceStyle = (0.5, 0.5);
            psi.ArgumentList.Add("--exaggeration"); psi.ArgumentList.Add("0.5");
            psi.ArgumentList.Add("--cfg-weight"); psi.ArgumentList.Add("0.5");
            AddLog("Chatterbox original baseline: exaggeration=0.5, cfg_weight=0.5");
            if (selectedFile != null && File.Exists(selectedFile)) { psi.ArgumentList.Add("--reference"); psi.ArgumentList.Add(selectedFile); AddLog("Reference: " + selectedFile); }
            psi.Environment["HF_HUB_OFFLINE"] = "1"; psi.Environment["TRANSFORMERS_OFFLINE"] = "1";
            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            chatterboxProcess = proc;
            proc.OutputDataReceived += (_, args) => { if (args.Data != null) AddLog(args.Data); };
            proc.ErrorDataReceived += (_, args) => { if (args.Data != null) AddLog(args.Data.Contains("Sampling:") ? "PROGRESS: " + args.Data : args.Data.Contains("Warning:") ? "WARNING: " + args.Data : "STDERR: " + args.Data); };
            if (!proc.Start()) throw new InvalidOperationException("Could not start the Chatterbox process.");
            AddLog("Engine process started (PID " + proc.Id + ")");
            proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();
            proc.WaitForExit(); // ensure asynchronous output handlers have drained
            AddLog("Engine exited with code " + proc.ExitCode);
            if (proc.ExitCode != 0) throw new InvalidOperationException("Chatterbox exited with code " + proc.ExitCode + ". Open AI Log for details.");
            if (!File.Exists(output)) throw new InvalidOperationException("No WAV was generated. Open AI Log for details.");
            generatedPlayer?.Stop(); generatedPlayer?.Dispose();
            generatedPlayer = new SoundPlayer(output); generatedPlayer.Load(); generatedPlayer.Play();
            var metadata = output + ".txt";
            File.WriteAllText(metadata, $"Style: Chatterbox original baseline\nExaggeration: {voiceStyle.Item1.ToString(System.Globalization.CultureInfo.InvariantCulture)}\nCFG weight: {voiceStyle.Item2.ToString(System.Globalization.CultureInfo.InvariantCulture)}\nReference: {selectedFile ?? "Default"}\nText: {textMemoir.Text}\n", Encoding.UTF8);
            labelStatus.Text = "Chatterbox WAV saved and playing: " + output;
            buttonCancelChatterbox.Enabled = true;
        }
        catch (Exception ex)
        {
            AddLog("FAILURE: " + ex);
            labelStatus.Text = "Chatterbox failed. Click AI Log to see why.";
            MessageBox.Show(this, ex.Message, "Local speech generation");
        }
        finally
        {
            chatterboxTimer.Stop();
            try { File.WriteAllLines(latestChatterboxLog!, logLines.ToArray()); } catch { }
            chatterboxProcess?.Dispose(); chatterboxProcess = null;
            SetAiBusy(false);
            buttonCancelChatterbox.Enabled = generatedPlayer != null;
            try { File.Delete(textPath); } catch { }
        }
    }
    private void buttonHistory_Click(object? sender, EventArgs e)
    {
        string dir = Path.Combine(dataRoot, "Narrations");
        Directory.CreateDirectory(dir);
        using var dialog = new Form { Text = "Compare Audio Recordings", Width = 900, Height = 500, StartPosition = FormStartPosition.CenterParent };
        var recordings = new List<string>();
        var list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, SelectionMode = SelectionMode.One };
        void AddRecording(string file)
        {
            if (!File.Exists(file) || !file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return;
            if (recordings.Any(x => string.Equals(x, file, StringComparison.OrdinalIgnoreCase))) return;
            recordings.Add(file);
            string description = "";
            try
            {
                if (File.Exists(file + ".txt"))
                    description = File.ReadLines(file + ".txt").FirstOrDefault(x => x.StartsWith("Style:", StringComparison.OrdinalIgnoreCase) || x.StartsWith("Engine:", StringComparison.OrdinalIgnoreCase)) ?? "";
            }
            catch { }
            list.Items.Add($"{Path.GetFileName(file)}  |  {description}  |  {Path.GetDirectoryName(file)}");
        }
        foreach (string file in Directory.EnumerateFiles(dir, "*.wav").OrderByDescending(File.GetLastWriteTime)) AddRecording(file);
        // Include existing voice references, so originals and synthesized audio can be compared.
        string voicesDir = Path.Combine(dataRoot, "Voices");
        if (Directory.Exists(voicesDir))
            foreach (string file in Directory.EnumerateFiles(voicesDir, "*.wav", SearchOption.AllDirectories).OrderBy(Path.GetFileName)) AddRecording(file);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52 };
        var play = new Button { Text = "Play selected", Width = 140 };
        var stop = new Button { Text = "Stop", Width = 100 };
        var add = new Button { Text = "Add WAV files...", Width = 150 };
        var open = new Button { Text = "Open narrations folder", Width = 180 };
        play.Click += (_, _) => {
            if (list.SelectedIndex < 0) return;
            try { generatedPlayer?.Stop(); generatedPlayer?.Dispose(); generatedPlayer = new SoundPlayer(recordings[list.SelectedIndex]); generatedPlayer.Load(); generatedPlayer.Play(); }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Playback"); }
        };
        stop.Click += (_, _) => { generatedPlayer?.Stop(); };
        add.Click += (_, _) => {
            using var picker = new OpenFileDialog { Title = "Select WAV recordings to compare", Filter = "WAV audio (*.wav)|*.wav", Multiselect = true };
            if (picker.ShowDialog(dialog) != DialogResult.OK) return;
            foreach (string file in picker.FileNames) AddRecording(file);
        };
        open.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        controls.Controls.Add(play); controls.Controls.Add(stop); controls.Controls.Add(add); controls.Controls.Add(open);
        dialog.Controls.Add(list); dialog.Controls.Add(controls);
        dialog.ShowDialog(this);
    }
    private void buttonAiLog_Click(object? sender, EventArgs e)
    {
        if (latestChatterboxLog == null || !File.Exists(latestChatterboxLog))
        { MessageBox.Show(this, "No completed AI run log yet."); return; }
        using var dialog = new Form { Text = "Chatterbox AI Log", Width = 850, Height = 540, StartPosition = FormStartPosition.CenterParent };
        var text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9), Text = File.ReadAllText(latestChatterboxLog) };
        dialog.Controls.Add(text);
        dialog.ShowDialog(this);
    }
    private static string? FindChatterboxPython()
    {
        var folders = new List<string>();
        string? current = AppContext.BaseDirectory;
        for (int i = 0; i < 7 && current != null; i++, current = Directory.GetParent(current)?.FullName) folders.Add(current);
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        folders.Add(Path.Combine(documents, "Visual Studio 2026", "VS Projects", "Personal Memoirs Reader"));
        foreach (string folder in folders)
        {
            foreach (string candidate in new[] { Path.Combine(folder, "chatterbox", ".venv", "Scripts", "python.exe"), Path.Combine(folder, ".venv", "Scripts", "python.exe") })
                if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
    private void buttonCancelChatterbox_Click(object? sender, EventArgs e)
    {
        try { if (chatterboxProcess is { HasExited: false }) chatterboxProcess.Kill(entireProcessTree: true); } catch { }
        generatedPlayer?.Stop();
        generatedPlayer?.Dispose(); generatedPlayer = null;
        if (generating) labelStatus.Text = "AI engine stopping...";
        else { buttonCancelChatterbox.Enabled = false; labelStatus.Text = "Chatterbox stopped."; }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        buttonCancelChatterbox_Click(null, EventArgs.Empty);
        generatedPlayer?.Dispose();
        StopSamplePlayback();
        try { if (referencePreviewPath != null) File.Delete(referencePreviewPath); } catch { }
        buttonStopSpeech_Click(null, EventArgs.Empty);
        if (speech != null) { try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(speech); } catch { } speech = null; }
        try { recorder?.Stop(); recorder?.Dispose(); } catch { }
        base.OnFormClosing(e);
    }
}
