using System.Diagnostics;
namespace PersonalMemoirsReader;
public partial class MainForm
{
    private async void buttonVoiceColor_Click(object? sender, EventArgs e)
    {
        if (selectedFile == null || !File.Exists(selectedFile))
        { MessageBox.Show(this, "Select a reference voice recording first."); return; }
        using var picker = new OpenFileDialog { Title = "Choose generated WAV to color-match", Filter = "WAV audio (*.wav)|*.wav" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (!File.Exists(F5Python))
        { MessageBox.Show(this, "F5's local Python environment is required for audio processing. Use Install / Verify F5 first."); return; }
        var worker = Path.Combine(AppContext.BaseDirectory, "voice_color_worker.py");
        if (!File.Exists(worker)) { MessageBox.Show(this, "Missing voice_color_worker.py"); return; }
        var dir = Path.Combine(dataRoot, "Narrations"); Directory.CreateDirectory(dir);
        var output = Path.Combine(dir, "color_" + DateTime.Now.ToString("yyyyMMdd_HHmmssfff") + ".wav");
        buttonVoiceColor.Enabled = false;
        labelStatus.Text = "Matching broad spectral voice color...";
        try
        {
            var psi = new ProcessStartInfo(F5Python) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "-u", worker, "--reference", selectedFile, "--source", picker.FileName, "--output", output, "--strength", "0.55" }) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start local audio processor.");
            var stdout = proc.StandardOutput.ReadToEndAsync(); var stderr = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            var msg = await stdout + "\n" + await stderr;
            if (proc.ExitCode != 0 || !File.Exists(output)) throw new InvalidOperationException(msg);
            File.WriteAllText(output + ".txt", "Engine: Experimental spectral-color transfer (not neural cloning)\nReference: " + selectedFile + "\nSource: " + picker.FileName + "\nStrength: 0.55\n" + msg);
            labelStatus.Text = "Color transfer saved: " + Path.GetFileName(output);
            MessageBox.Show(this, "Created: " + Path.GetFileName(output) + "\n\nUse Compare Narrations to listen against the unmodified WAV. This is an experimental spectral-color test, not trained voice conversion.", "Voice color test");
        }
        catch (Exception ex) { labelStatus.Text = "Voice color test failed."; MessageBox.Show(this, ex.Message, "Voice color test"); }
        finally { buttonVoiceColor.Enabled = true; }
    }
}
