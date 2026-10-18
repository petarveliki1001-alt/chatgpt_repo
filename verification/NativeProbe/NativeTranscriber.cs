using Whisper.net;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

static class NativeTranscriber
{
    public static async Task<string> Transcribe(string model, string audio)
    {
        var result = await TranscribeMany(model, new[] {audio});
        return result[0];
    }
    public static async Task<List<string>> TranscribeMany(string model, string[] audioFiles, string? expected = null)
    {
        using var factory = WhisperFactory.FromPath(model);
        using var processor = factory.CreateBuilder().WithLanguage("en").WithThreads(4).Build();
        var results = new List<string>();
        bool mismatch = false;
        foreach (string audio in audioFiles)
        {
            var watch = Stopwatch.StartNew();
            var (samples, rate) = WaveAudio.Read(audio);
            samples = WaveAudio.Resample(samples, rate, 16000);
            var parts = new List<string>();
            await foreach (var part in processor.ProcessAsync(samples)) parts.Add(part.Text.Trim());
            string text = string.Join(" ", parts);
            results.Add(text);
            bool? matches = expected == null ? null : Normalize(text) == Normalize(expected);
            mismatch |= matches == false;
            Console.WriteLine($"NATIVE TRANSCRIPT ({watch.Elapsed.TotalSeconds:F2}s): {text}");
            File.WriteAllText(audio + ".asr.json", JsonSerializer.Serialize(new {audio,text,expected,matches,seconds=watch.Elapsed.TotalSeconds},new JsonSerializerOptions{WriteIndented=true}));
        }
        if (mismatch) throw new InvalidDataException("At least one generated WAV did not transcribe to the expected narration; see .asr.json results.");
        return results;
    }
    private static string Normalize(string text) => Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
}
