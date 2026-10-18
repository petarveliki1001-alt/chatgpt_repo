using MemoirsReader.Native;
using System.Text.Json;
namespace PersonalMemoirsReader;
public partial class MainForm
{
    internal async Task RunDesktopSmokeAsync(string reference, string results)
    {
        Directory.CreateDirectory(results);
        if (Text != "Memoirs Reader v0.1.37" || buttonChatterbox.Text != "Generate with Chatterbox")
            throw new InvalidOperationException("Form layout/title did not initialize.");
        using (var bitmap = new Bitmap(Width,Height))
        {
            DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));
            bitmap.Save(Path.Combine(results,"form.png"),System.Drawing.Imaging.ImageFormat.Png);
        }
        listSamples.Items.Add(reference); listSamples.SelectedItem=reference;
        if (selectedFile!=reference) throw new InvalidOperationException("Sample selection did not reach the form.");
        textMemoir.Text="The small boat crossed the quiet lake.";
        var pocketFirst=await GenerateNativeAsync("pocket",false,false) ?? throw new InvalidOperationException("PocketTTS generation did not start.");
        var pocketSecond=await GenerateNativeAsync("pocket",false,false) ?? throw new InvalidOperationException("PocketTTS warm generation did not start.");
        if(pocketFirst.ReusedModel || pocketFirst.ReusedReference || !pocketSecond.ReusedModel || !pocketSecond.ReusedReference)
            throw new InvalidOperationException("PocketTTS warm reuse failed.");
        if(pocketFirst.ReferenceTranscript!="" || textReference.Text!="") throw new InvalidOperationException("PocketTTS unexpectedly required a transcript.");
        textMemoir.Text="The small boat crossed the quiet lake. On the far shore, a little house stood among the trees. Its windows shone in the evening light. We followed the narrow path and listened to the birds as the sun began to set.";
        var pocketLong=await GenerateNativeAsync("pocket",false,false) ?? throw new InvalidOperationException("Long PocketTTS narration failed.");
        textMemoir.Text=string.Join(" ",Enumerable.Repeat("The small boat crossed the quiet lake.",100));
        int beforeCancel=Directory.GetFiles(Path.Combine(dataRoot,"Narrations"),"*.wav").Length;
        var interrupted=GenerateNativeAsync("pocket",false,false);
        await Task.Delay(200);
        generationCancellation?.Cancel();
        bool cancellationObserved=false;
        try { await interrupted.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch(OperationCanceledException) { cancellationObserved=true; }
        if(!cancellationObserved || Directory.GetFiles(Path.Combine(dataRoot,"Narrations"),"*.wav").Length!=beforeCancel)
            throw new InvalidOperationException("PocketTTS cancellation retained output or was not observed.");
        textMemoir.Text="The small boat crossed the quiet lake.";
        var pocketRestart=await GenerateNativeAsync("pocket",false,false) ?? throw new InvalidOperationException("PocketTTS restart failed.");
        if(pocketRestart.ReusedModel) throw new InvalidOperationException("Canceled PocketTTS worker was not restarted.");
        textProfile.Focus(); // Disabling this field must not refresh/clear an unchanged profile.
        var first=await GenerateNativeAsync("chatterbox",false,false) ?? throw new InvalidOperationException("First generation did not start.");
        if(selectedFile!=reference) throw new InvalidOperationException("Voice selection changed during generation.");
        var second=await GenerateNativeAsync("chatterbox",false,false) ?? throw new InvalidOperationException("Second generation did not start.");
        if (first.ReusedModel || first.ReusedReference || !second.ReusedModel || !second.ReusedReference)
            throw new InvalidOperationException("Warm engine/reference reuse failed.");
        textMemoir.Text="Some call me nature. Others call me Mother Nature.";
        buttonUseNarration.PerformClick();
        if(textReference.Text!=textMemoir.Text) throw new InvalidOperationException("Use narration as sample script failed.");
        textMemoir.Text="The small boat crossed the quiet lake.";
        SaveTranscript(); textReference.Clear(); LoadSavedTranscript();
        if(textReference.Text!="Some call me nature. Others call me Mother Nature.") throw new InvalidOperationException("Transcript persistence failed.");
        var f5=await GenerateNativeAsync("f5",false,false) ?? throw new InvalidOperationException("F5 generation did not start.");
        if (new[]{first,second,f5}.Any(x=>x.DurationSeconds<=0 || x.SynthesisSeconds<=0)) throw new InvalidOperationException("Invalid generation result.");
        foreach (string file in Directory.GetFiles(Path.Combine(dataRoot,"Narrations"),"*.wav"))
            WaveAudio.Read(file); // Verify actual finalized output WAVs, not just task completion.
        File.WriteAllText(Path.Combine(results,"desktop-smoke.json"),JsonSerializer.Serialize(new
        {
            passed=true,version=Application.ProductVersion,platform=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            formControls=Controls.Count,pocketFirst,pocketSecond,pocketLong,pocketRestart,cancellationObserved,first,second,f5,
            scope="Actual form load, voice selection, generation through desktop handlers, warm model/reference reuse, PocketTTS long narration/cancellation/restart, explicit script copy, saved transcript reload, finalized WAV decoding. No ASR or listening assessment."
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
