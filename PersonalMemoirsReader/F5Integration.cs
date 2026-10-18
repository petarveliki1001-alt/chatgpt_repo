using System.Diagnostics;
using System.Media;
using System.Text;

namespace PersonalMemoirsReader;

public partial class MainForm
{
    private string F5Environment => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalMemoirsReader", "f5-venv");
    private string F5Python => Path.Combine(F5Environment, "Scripts", "python.exe");
    private string F5ReadyMarker => Path.Combine(F5Environment, "f5-offline-ready.txt");
    private string F5Cache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalMemoirsReader", "F5ModelCache");
    private void SetF5Environment(ProcessStartInfo psi, bool offline)
    {
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["HF_HOME"] = F5Cache;
        psi.Environment["HF_HUB_DISABLE_TELEMETRY"] = "1";
        if (offline)
        {
            psi.Environment["HF_HUB_OFFLINE"] = "1";
            psi.Environment["TRANSFORMERS_OFFLINE"] = "1";
        }
    }
    private async void buttonF5Setup_Click(object? sender, EventArgs e)
    {
        if (generating) return;
        if (MessageBox.Show(this,
            "F5-TTS requires its own Python packages and pretrained model weights.\n\n" +
            "The application will create a SEPARATE environment, install F5-TTS, download its speech and automatic-transcription models, and test loading them offline.\n" +
            "This may take considerable time and disk space; internet is required for setup only.\n" +
            "Your existing Chatterbox installation will not be modified.\n\nContinue?",
            "F5-TTS installation and verification", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SetAiBusy(true);
        buttonCancelChatterbox.Enabled = true;
        var outputDir = Path.Combine(dataRoot, "Narrations"); Directory.CreateDirectory(outputDir);
        latestChatterboxLog = Path.Combine(outputDir, "F5_Setup.log");
        var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Log(string message)
        {
            lines.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
            try { File.AppendAllText(latestChatterboxLog!, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}"); } catch { }
            if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => chatterboxPhase = message.Length > 95 ? message[..95] : message));
        }
        File.WriteAllText(latestChatterboxLog, "F5-TTS setup started" + Environment.NewLine);
        chatterboxStarted = DateTime.Now; chatterboxPhase = "Preparing F5 installation"; chatterboxTimer.Start();
        try
        {
            var systemPython = @"C:\Program Files\Python311\python.exe";
            if (!File.Exists(systemPython))
            {
                var chatterbox = FindChatterboxPython();
                if (chatterbox == null) throw new FileNotFoundException("Python 3.11 installation was not found.");
                // A venv has Scripts/python.exe; its pyvenv.cfg records the base interpreter.
                var config = Path.Combine(Directory.GetParent(Directory.GetParent(chatterbox)!.FullName)!.FullName, "pyvenv.cfg");
                if (!File.Exists(config)) throw new FileNotFoundException("Cannot locate Python environment configuration: " + config);
                var home = File.ReadLines(config).FirstOrDefault(x => x.TrimStart().StartsWith("home =", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1].Trim();
                systemPython = Path.Combine(home ?? "", "python.exe");
                if (!File.Exists(systemPython)) throw new FileNotFoundException("Base Python executable not found: " + systemPython);
            }
            async Task Run(string stage, string exe, bool offline, params string[] args)
            {
                Log("STAGE: " + stage);
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in args) psi.ArgumentList.Add(arg);
                SetF5Environment(psi, offline);
                using var process = new Process { StartInfo = psi };
                chatterboxProcess = process;
                process.OutputDataReceived += (_, x) => { if (x.Data != null) Log(x.Data); };
                process.ErrorDataReceived += (_, x) => { if (x.Data != null) Log("DETAIL: " + x.Data); };
                if (!process.Start()) throw new InvalidOperationException("Unable to start " + stage);
                Log("Process ID " + process.Id);
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                await process.WaitForExitAsync(); process.WaitForExit();
                Log("Exit code " + process.ExitCode);
                if (process.ExitCode != 0) throw new InvalidOperationException(stage + " failed (exit " + process.ExitCode + "). See AI Log.");
                chatterboxProcess = null;
            }
            if (!File.Exists(F5Python)) await Run("Creating separate Python environment", systemPython, false, "-m", "venv", F5Environment);
            await Run("Installing F5-TTS dependencies", F5Python, false, "-m", "pip", "install", "f5-tts");
            var setupWorker = Path.Combine(AppContext.BaseDirectory, "f5_setup_worker.py");
            if (!File.Exists(setupWorker)) throw new FileNotFoundException("Setup worker is missing", setupWorker);
            Directory.CreateDirectory(F5Cache);
            await Run("Downloading pretrained model and checking offline load", F5Python, false, "-u", setupWorker, F5ReadyMarker);
            if (!File.Exists(F5ReadyMarker)) throw new InvalidOperationException("Offline model verification did not complete.");
            Log("READY: F5-TTS installation and offline model verification succeeded.");
            labelStatus.Text = "F5-TTS ready: pretrained model verified offline.";
            MessageBox.Show(this, "F5-TTS is ready for offline generation. Choose a recording and enter narration text. Leave the reference transcript blank for automatic transcription.", "F5-TTS ready");
        }
        catch (Exception ex)
        {
            try { File.Delete(F5ReadyMarker); } catch { }
            Log("FAILURE: " + ex);
            labelStatus.Text = "F5-TTS not ready. Open AI Log for installation details.";
            MessageBox.Show(this, ex.Message, "F5-TTS setup failed");
        }
        finally
        {
            chatterboxTimer.Stop(); chatterboxProcess = null; generating = false;
            buttonF5Setup.Enabled = true; buttonF5.Enabled = File.Exists(F5ReadyMarker);
            SetAiBusy(false);
        }
    }

    private async void buttonF5_Click(object? sender, EventArgs e)
    {
        if (generating) return;
        if (!File.Exists(F5Python) || !File.Exists(F5ReadyMarker)) { MessageBox.Show(this, "F5-TTS is not ready. Click Install / Verify F5 to install its model and check offline operation."); return; }
        if (selectedFile == null || !File.Exists(selectedFile)) { MessageBox.Show(this, "Select a voice reference recording."); return; }
        if (string.IsNullOrWhiteSpace(textMemoir.Text)) { MessageBox.Show(this, "Enter narration text."); return; }
        // Identical reference and target words are valid for controlled voice tests.
        // Do not block synthesis based on transcript overlap.
        var speed = numericF5Speed.Value;
        var dir = Path.Combine(dataRoot, "Narrations"); Directory.CreateDirectory(dir);
        var output = Path.Combine(dir, "f5_" + DateTime.Now.ToString("yyyyMMdd_HHmmssfff") + ".wav");
        var textPath = Path.Combine(Path.GetTempPath(), "pmr_f5_" + Guid.NewGuid().ToString("N") + ".txt");
        var refPath = Path.Combine(Path.GetTempPath(), "pmr_ref_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(textPath, textMemoir.Text, Encoding.UTF8); File.WriteAllText(refPath, textReference.Text, Encoding.UTF8);
        latestChatterboxLog = Path.Combine(dir, "F5_LastRun.log");
        SetAiBusy(true);
        chatterboxStarted = DateTime.Now; chatterboxPhase = "F5-TTS starting"; chatterboxTimer.Start();
        var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Log(string message) { lines.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}"); if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => chatterboxPhase = message.Length > 80 ? message[..80] : message)); }
        try
        {
            var worker = Path.Combine(AppContext.BaseDirectory, "f5_worker.py");
            var psi = new ProcessStartInfo(F5Python) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "-u", worker, "--reference", selectedFile, "--reference-text", refPath, "--text-file", textPath, "--output", output, "--speed", speed.ToString(System.Globalization.CultureInfo.InvariantCulture), "--nfe-step", "32", "--cfg-strength", "2.0" }) psi.ArgumentList.Add(a);
            AddReferenceRange(psi);
            // Offline-only generation; model must have passed setup verification.
            SetF5Environment(psi, true);
            using var proc = new Process { StartInfo = psi };
            chatterboxProcess = proc;
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) Log(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Log(e.Data); };
            if (!proc.Start()) throw new InvalidOperationException("F5 engine could not start.");
            Log("F5 process started (PID " + proc.Id + "); speaking speed " + speed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
            await proc.WaitForExitAsync(); proc.WaitForExit();
            Log("Exit code " + proc.ExitCode);
            if (proc.ExitCode != 0 || !File.Exists(output))
            {
                string? reason = lines.LastOrDefault(x => x.Contains("RuntimeError:") || x.Contains("ValueError:"));
                throw new InvalidOperationException(reason ?? "F5 generation failed. Open AI Log for details.");
            }
            generatedPlayer?.Stop(); generatedPlayer?.Dispose(); generatedPlayer = new SoundPlayer(output); generatedPlayer.Load(); generatedPlayer.Play();
            File.WriteAllText(output + ".txt", $"Engine: F5-TTS\nSpeed: {speed.ToString(System.Globalization.CultureInfo.InvariantCulture)}\nReference: {selectedFile}\nReference input: full recording by default; optional user range; F5 may shorten it internally\nReference transcript: {textReference.Text}\nText: {textMemoir.Text}\n");
            labelStatus.Text = "F5 WAV saved and playing: " + output;
        }
        catch (Exception ex) { Log("FAILURE: " + ex); labelStatus.Text = "F5 failed. Open AI Log."; MessageBox.Show(this, ex.Message); }
        finally { chatterboxTimer.Stop(); try { File.WriteAllLines(latestChatterboxLog, lines.ToArray()); } catch { } chatterboxProcess = null; generating = false; buttonF5.Enabled = File.Exists(F5ReadyMarker); SetAiBusy(false); try { File.Delete(textPath); File.Delete(refPath); } catch { } }
    }
}
