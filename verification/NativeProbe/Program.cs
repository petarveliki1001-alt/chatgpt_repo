using Microsoft.ML.OnnxRuntime;
using System.Security.Cryptography;
using System.Text.Json;

Console.WriteLine($"Native ONNX Runtime: {OrtEnv.Instance().GetVersionString()}");
Console.WriteLine($"Providers: {string.Join(", ", OrtEnv.Instance().GetAvailableProviders())}");
if (args.Length == 0) return;
if (args[0] == "cpu-benchmark")
{
    if (args.Length != 4) throw new ArgumentException("cpu-benchmark <f5-model-directory> <captured-wav-prefix> <report.json>");
    CpuBenchmark.Run(args[1], args[2], args[3]);
    return;
}
if (args[0] == "f5")
{
    F5Probe.Generate(args[1], args[2], args[3], args[4], args[5], args[6], args.Length > 7 ? int.Parse(args[7]) : 4);
    return;
}
if (args[0] == "f5-auto")
{
    string transcript = await NativeTranscriber.Transcribe(args[3], args[4]);
    F5Probe.Generate(args[1], args[2], args[4], transcript, args[5], args[6]);
    return;
}
if (args[0] == "chatterbox")
{
    ChatterboxProbe.Generate(args[1], args[2], args[3], args[4], args.Length > 5 ? int.Parse(args[5]) : 256);
    return;
}
if (args[0] == "transcribe")
{
    await NativeTranscriber.Transcribe(args[1], args[2]);
    return;
}
if (args[0] == "verify-text")
{
    await NativeTranscriber.TranscribeMany(args[1], args.Skip(3).ToArray(), args[2]);
    return;
}
if (args[0] == "tokenize")
{
    Console.WriteLine(string.Join(',', new BpeTokenizer(args[1]).Encode(args[2])));
    return;
}
if (args[0] == "compare-wave")
{
    var (a, rateA) = WaveAudio.Read(args[1]);
    var (b, rateB) = WaveAudio.Read(args[2]);
    bool sameLength = a.Length == b.Length && rateA == rateB;
    double? rmse = sameLength ? Math.Sqrt(a.Zip(b, (x,y) => (double)(x-y)*(x-y)).Average()) : null;
    float? maxDifference = sameLength ? a.Zip(b, (x,y) => Math.Abs(x-y)).Max() : null;
    var report = JsonSerializer.Serialize(new {first=args[1],second=args[2],sameLength,firstSamples=a.Length,secondSamples=b.Length,rmse,maxDifference},new JsonSerializerOptions{WriteIndented=true});
    File.WriteAllText(args[3],report); Console.WriteLine(report);
    return;
}
if (args[0] == "speaker-scores")
{
    SpeakerScores.Compare(args[1], args[2], args[3], args.Skip(4).ToArray());
    return;
}
if (args[0] == "download")
{
    if (args.Length != 3) throw new ArgumentException("download <manifest.json> <model-directory>");
    var entries = JsonSerializer.Deserialize<ModelFile[]>(File.ReadAllText(args[1]))!;
    var root = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(root);
    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    foreach (var entry in entries)
    {
        var target = Path.GetFullPath(Path.Combine(root, entry.Path));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Invalid model path");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (!File.Exists(target))
        {
            Console.WriteLine($"Downloading {entry.Path}");
            using var response = await client.GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var partial = target + ".partial";
            try
            {
                await using (var output = File.Create(partial)) await response.Content.CopyToAsync(output);
                Verify(partial, entry);
                File.Move(partial, target);
            }
            catch { File.Delete(partial); throw; }
        }
        Verify(target, entry);
        Console.WriteLine($"Verified SHA-256: {entry.Path}");
    }
    return;
}
using var options = new SessionOptions { IntraOpNumThreads = 4, InterOpNumThreads = 1 };
foreach (var path in args)
{
    Console.WriteLine(path);
    using var session = new InferenceSession(path, options);
    foreach (var kv in session.InputMetadata) Console.WriteLine($"IN {kv.Key} {kv.Value.ElementType} [{string.Join(',', kv.Value.Dimensions)}]");
    foreach (var kv in session.OutputMetadata) Console.WriteLine($"OUT {kv.Key} {kv.Value.ElementType} [{string.Join(',', kv.Value.Dimensions)}]");
}

static void Verify(string path, ModelFile entry)
{
    if (new FileInfo(path).Length != entry.Size) throw new InvalidDataException($"Wrong size: {entry.Path}");
    using var file = File.OpenRead(path);
    var actual = Convert.ToHexString(SHA256.HashData(file));
    if (!actual.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Wrong SHA-256: {entry.Path}");
}
record ModelFile(string Path, string Url, long Size, string Sha256);
