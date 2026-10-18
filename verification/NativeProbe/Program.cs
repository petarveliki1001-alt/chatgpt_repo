using Microsoft.ML.OnnxRuntime;
using System.Security.Cryptography;
using System.Text.Json;

Console.WriteLine($"Native ONNX Runtime: {OrtEnv.Instance().GetVersionString()}");
Console.WriteLine($"Providers: {string.Join(", ", OrtEnv.Instance().GetAvailableProviders())}");
if (args.Length == 0) return;
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
