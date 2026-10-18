using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
namespace MemoirsReader.Native;

public sealed record ModelFile(string Path, string Url, long Size, string Sha256);
public sealed class ModelStore
{
    public string Root { get; }
    private readonly ModelFile[] manifest;
    public ModelStore(string root, string manifestPath)
    {
        Root = System.IO.Path.GetFullPath(root);
        manifest = JsonSerializer.Deserialize<ModelFile[]>(File.ReadAllText(manifestPath))!;
    }
    public ModelFile[] Files(string engine) => manifest.Where(x => x.Path.StartsWith(engine + "/", StringComparison.Ordinal) || (engine == "f5" && x.Path == "fixtures/f5-vocab.txt")).ToArray();
    public bool IsPresent(string engine)
    {
        if (Files(engine).Length == 0) return false;
        if (!Files(engine).All(x => File.Exists(Target(x.Path)) && new FileInfo(Target(x.Path)).Length == x.Size)) return false;
        return engine != "f5" || new[]{"F5_Preprocess.onnx","F5_Transformer.onnx","F5_Decode.onnx"}.All(x => File.Exists(Target("f5/"+x)));
    }
    private string Target(string relative)
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relative));
        if (!path.StartsWith(Root + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Invalid model path.");
        return path;
    }
    public async Task InstallAsync(string engine, Action<string> progress, CancellationToken token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        foreach (var entry in Files(engine))
        {
            string target = Target(entry.Path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            if (File.Exists(target) && await Matches(target, entry, token)) { progress("Verified " + entry.Path); continue; }
            progress("Downloading " + entry.Path);
            string partial = target + ".partial";
            try
            {
                using var response = await http.GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                await using (var source = await response.Content.ReadAsStreamAsync(token))
                await using (var destination = File.Create(partial))
                {
                    byte[] buffer = new byte[1024*1024];
                    long downloaded = 0, lastReported = 0;
                    int count;
                    while ((count = await source.ReadAsync(buffer, token)) > 0)
                    {
                        await destination.WriteAsync(buffer.AsMemory(0,count),token); downloaded += count;
                        if (downloaded-lastReported >= 16*1024*1024) { progress($"{entry.Path}: {downloaded/1048576} / {entry.Size/1048576} MB"); lastReported=downloaded; }
                    }
                }
                if (!await Matches(partial, entry, token)) throw new InvalidDataException("Model checksum mismatch: " + entry.Path);
                File.Move(partial,target,true);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
        if (engine == "f5")
        {
            progress("Extracting F5 model");
            // The downloaded archive was verified above. Never extract outside the cache.
            using var zip = ZipFile.OpenRead(Target("f5/CPU_F32.zip"));
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (entry.Name.Length == 0) continue;
                string destination = Target("f5/" + entry.FullName);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination,true);
            }
        }
        if (!IsPresent(engine)) throw new InvalidDataException("Model installation is incomplete.");
        progress("Models verified and ready offline");
    }
    private static async Task<bool> Matches(string file, ModelFile entry, CancellationToken token)
    {
        if (new FileInfo(file).Length != entry.Size) return false;
        await using var stream = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream,token)).Equals(entry.Sha256,StringComparison.OrdinalIgnoreCase);
    }
}
