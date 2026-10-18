using System.Text;

namespace MemoirsReader.Native;

public static class WaveAudio
{
    public static (float[] Samples, int Rate) Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new InvalidDataException("Use a PCM16 or float32 WAV recording (convert other audio through Import).");
        reader.ReadUInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new InvalidDataException("Expected WAVE");
        ushort format = 0, channels = 0, bits = 0;
        int rate = 0;
        byte[]? data = null;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadUInt32();
            var next = reader.BaseStream.Position + size + size % 2;
            if (id == "fmt ")
            {
                format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                reader.ReadInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
                if (format == 65534 && size >= 40) { reader.ReadUInt16(); reader.ReadUInt16(); reader.ReadUInt32(); format = reader.ReadUInt16(); }
            }
            else if (id == "data") data = reader.ReadBytes(checked((int)size));
            reader.BaseStream.Position = next;
        }
        if (data == null || channels == 0 || rate == 0 || !((format == 1 && bits == 16) || (format == 3 && bits == 32)))
            throw new InvalidDataException("Use PCM16 or float32 WAV; other encodings can be converted with FFmpeg.");
        var sampleBytes = bits / 8;
        var mono = new float[data.Length / sampleBytes / channels];
        for (int i = 0; i < mono.Length; i++)
            for (int c = 0; c < channels; c++)
            {
                int offset = (i * channels + c) * sampleBytes;
                mono[i] += (format == 1 ? BitConverter.ToInt16(data, offset) / 32768f : BitConverter.ToSingle(data, offset)) / channels;
            }
        if (mono.Length == 0 || mono.Any(x => !float.IsFinite(x))) throw new InvalidDataException("Recording is empty or contains invalid samples.");
        if (mono.Max(x => Math.Abs(x)) < 1e-5f) throw new InvalidDataException("Recording is silent. Select a recording containing clear speech.");
        return (mono, rate);
    }

    public static float[] Resample(float[] source, int from, int to)
    {
        if (from == to) return source;
        var result = new float[(int)Math.Round(source.Length * (double)to / from)];
        // Windowed sinc low-pass resampling; no Python/audio runtime dependency.
        double ratio = Math.Min(1.0, (double)to / from);
        const int radius = 24;
        for (int i = 0; i < result.Length; i++)
        {
            double position = i * (double)from / to, value = 0, weight = 0;
            int center = (int)position;
            for (int j = Math.Max(0, center - radius); j <= Math.Min(source.Length - 1, center + radius); j++)
            {
                double x = position - j;
                if (Math.Abs(x) >= radius) continue;
                double sx = x * ratio;
                double sinc = Math.Abs(sx) < 1e-10 ? 1 : Math.Sin(Math.PI * sx) / (Math.PI * sx);
                double w = ratio * sinc * (0.5 + 0.5 * Math.Cos(Math.PI * x / radius));
                value += source[j] * w; weight += w;
            }
            result[i] = (float)(value / weight);
        }
        return result;
    }

    public static void Write(string path, float[] samples, int rate)
    {
        if (samples.Length == 0 || samples.Any(x => !float.IsFinite(x))) throw new InvalidDataException("Empty or nonfinite audio");
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write(rate); writer.Write(rate * 2); writer.Write((ushort)2); writer.Write((ushort)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
        foreach (float sample in samples) writer.Write((short)Math.Clamp((int)Math.Round(sample * 32768), short.MinValue, short.MaxValue));
    }
}
