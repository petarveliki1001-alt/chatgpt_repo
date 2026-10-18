using System.Diagnostics;
using System.Globalization;
using System.Media;

namespace PersonalMemoirsReader;

public partial class MainForm
{
    private string? referencePreviewPath;

    private void AddReferenceRange(ProcessStartInfo psi)
    {
        psi.ArgumentList.Add("--reference-start");
        psi.ArgumentList.Add(referenceStart.Value.ToString(CultureInfo.InvariantCulture));
        if (referenceEnd.Value > 0)
        {
            psi.ArgumentList.Add("--reference-end");
            psi.ArgumentList.Add(referenceEnd.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private async void PreviewReference_Click(object? sender, EventArgs e)
    {
        if (generating) return;
        if (selectedFile == null || !File.Exists(F5Python))
        {
            MessageBox.Show(this, "Install F5 and select a reference recording first.");
            return;
        }
        string transcript = Path.GetTempFileName();
        string output = Path.Combine(Path.GetTempPath(), "pmr_preview_" + Guid.NewGuid().ToString("N") + ".wav");
        SetAiBusy(true);
        try
        {
            File.WriteAllText(transcript, textReference.Text);
            var psi = new ProcessStartInfo(F5Python) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "-u", Path.Combine(AppContext.BaseDirectory, "f5_worker.py"), "--reference", selectedFile, "--reference-text", transcript, "--text-file", transcript, "--output", output, "--prepare-only" }) psi.ArgumentList.Add(arg);
            AddReferenceRange(psi);
            SetF5Environment(psi, true);
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not prepare reference.");
            chatterboxProcess = proc;
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            string details = await stdout + "\n" + await stderr;
            if (proc.ExitCode != 0) throw new InvalidOperationException(details);
            StopSamplePlayback();
            if (referencePreviewPath != null) File.Delete(referencePreviewPath);
            referencePreviewPath = output;
            samplePlayer = new SoundPlayer(output);
            samplePlayer.Load(); samplePlayer.Play();
            buttonStopListen.Enabled = true;
            labelStatus.Text = "Listening to the exact F5 excerpt. A blank transcript will be filled automatically during generation.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "F5 reference"); }
        finally
        {
            chatterboxProcess = null;
            SetAiBusy(false);
            File.Delete(transcript);
            if (referencePreviewPath != output && File.Exists(output)) File.Delete(output);
        }
    }

    private void SetAiBusy(bool busy)
    {
        generating = busy;
        buttonF5.Enabled = !busy;
        buttonF5Setup.Enabled = !busy;
        buttonChatterbox.Enabled = !busy;
        buttonVoiceColor.Enabled = !busy;
        previewReference.Enabled = !busy;
        referenceStart.Enabled = referenceEnd.Enabled = !busy;
        textReference.Enabled = textMemoir.Enabled = textProfile.Enabled = !busy;
        buttonOpenText.Enabled = numericF5Speed.Enabled = !busy;
        listSamples.Enabled = buttonDelete.Enabled = buttonImport.Enabled = !busy;
        buttonRecord.Enabled = !busy && recorder == null;
        buttonCancelChatterbox.Enabled = busy || generatedPlayer != null;
    }
}
