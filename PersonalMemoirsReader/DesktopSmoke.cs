using MemoirsReader.Native;
using System.Text.Json;
namespace PersonalMemoirsReader;
public partial class MainForm
{
    internal async Task RunDesktopSmokeAsync(string reference, string results)
    {
        Directory.CreateDirectory(results);
        if (Text != "Memoirs Reader v0.1.36" || buttonChatterbox.Text != "Generate with Chatterbox")
            throw new InvalidOperationException("Form layout/title did not initialize.");
        using (var bitmap = new Bitmap(Width,Height))
        {
            DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));
            bitmap.Save(Path.Combine(results,"form.png"),System.Drawing.Imaging.ImageFormat.Png);
        }
        listSamples.Items.Add(reference); listSamples.SelectedItem=reference;
        if (selectedFile!=reference) throw new InvalidOperationException("Sample selection did not reach the form.");
        textMemoir.Text="The small boat crossed the quiet lake.";
        textProfile.Focus(); // Disabling this field must not refresh/clear an unchanged profile.
        var first=await GenerateNativeAsync("chatterbox",false,false) ?? throw new InvalidOperationException("First generation did not start.");
        if(selectedFile!=reference) throw new InvalidOperationException("Voice selection changed during generation.");
        var second=await GenerateNativeAsync("chatterbox",false,false) ?? throw new InvalidOperationException("Second generation did not start.");
        if (first.ReusedModel || first.ReusedReference || !second.ReusedModel || !second.ReusedReference)
            throw new InvalidOperationException("Warm engine/reference reuse failed.");
        textReference.Text="Some call me nature. Others call me Mother Nature.";
        SaveTranscript(); textReference.Clear(); LoadSavedTranscript();
        if(textReference.Text!="Some call me nature. Others call me Mother Nature.") throw new InvalidOperationException("Transcript persistence failed.");
        var f5=await GenerateNativeAsync("f5",false,false) ?? throw new InvalidOperationException("F5 generation did not start.");
        if (new[]{first,second,f5}.Any(x=>x.DurationSeconds<=0 || x.SynthesisSeconds<=0)) throw new InvalidOperationException("Invalid generation result.");
        foreach (string file in Directory.GetFiles(Path.Combine(dataRoot,"Narrations"),"*.wav"))
            WaveAudio.Read(file); // Verify actual finalized output WAVs, not just task completion.
        File.WriteAllText(Path.Combine(results,"desktop-smoke.json"),JsonSerializer.Serialize(new
        {
            passed=true,version=Application.ProductVersion,platform=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            formControls=Controls.Count,first,second,f5,
            scope="Actual form load, voice selection, generation through desktop handlers, warm model/reference reuse, saved transcript reload, finalized WAV decoding. No ASR or listening assessment."
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
