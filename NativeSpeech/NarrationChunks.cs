using System.Text.RegularExpressions;
namespace MemoirsReader.Native;
public static class NarrationChunks
{
    public static IReadOnlyList<string> Split(string text, int maxCharacters = 160)
    {
        if (maxCharacters < 16) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        var chunks = new List<string>();
        string remaining = Regex.Replace(text.Trim(), @"\s+", " ");
        while (remaining.Length > maxCharacters)
        {
            int end = remaining.LastIndexOfAny(new[]{'.','!','?'}, maxCharacters - 1, maxCharacters);
            if (end < maxCharacters / 3) end = remaining.LastIndexOf(' ', maxCharacters);
            if (end < 1) throw new ArgumentException("A narration word is too long. Add a space or punctuation.");
            if (remaining[end] != ' ') end++;
            chunks.Add(remaining[..end].Trim()); remaining = remaining[end..].TrimStart();
        }
        if (remaining.Length > 0) chunks.Add(remaining);
        return chunks;
    }
}
