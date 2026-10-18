using System.Text.Json;
using System.Text.RegularExpressions;

namespace MemoirsReader.Diagnostics;

sealed class DiagnosticBpeTokenizer
{
    private readonly Dictionary<string, long> vocabulary;
    private readonly Dictionary<(string, string), int> ranks = new();
    public DiagnosticBpeTokenizer(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var model = json.RootElement.GetProperty("model");
        vocabulary = model.GetProperty("vocab").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetInt64());
        int rank = 0;
        foreach (var pair in model.GetProperty("merges").EnumerateArray())
        {
            var pieces = pair.ValueKind == JsonValueKind.Array ? pair.EnumerateArray().Select(x => x.GetString()!).ToArray() : pair.GetString()!.Split(' ');
            if (pieces.Length != 2) throw new InvalidDataException("Unsupported merge encoding");
            ranks[(pieces[0], pieces[1])] = rank++;
        }
    }
    public long[] Encode(string text)
    {
        var pieces = Regex.Replace(text, @"\s+", " ").EnumerateRunes().Select(x => x.ToString()).ToList();
        while (pieces.Count > 1)
        {
            int best = int.MaxValue, at = -1;
            for (int i = 0; i < pieces.Count - 1; i++)
                if (ranks.TryGetValue((pieces[i], pieces[i+1]), out int rank) && rank < best) { best = rank; at = i; }
            if (at < 0) break;
            pieces[at] += pieces[at + 1]; pieces.RemoveAt(at + 1);
        }
        return new long[] { 6563, 255 }.Concat(pieces.Select(x => vocabulary.GetValueOrDefault(x, 1))).Concat(new long[] { 0, 6561, 6561 }).ToArray();
    }
}
