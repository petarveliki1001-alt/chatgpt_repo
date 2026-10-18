using System.Security.Cryptography;
namespace MemoirsReader.Native;
public static class AudioIdentity
{
    public static string Key(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
