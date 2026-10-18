using System.Runtime.InteropServices;
using System.Text;

namespace PersonalMemoirsReader;
// Direct waveIn capture. Fixed 44,100 Hz, mono, signed 16-bit PCM.
// No automatic noise processing is asserted or applied.
internal sealed class WaveRecorder : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public ushort formatTag, channels;
        public uint samplesPerSec, avgBytesPerSec;
        public ushort blockAlign, bitsPerSample, cbSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr data;
        public uint bufferLength, bytesRecorded;
        public IntPtr user;
        public uint flags, loops;
        public IntPtr next, reserved;
    }
    [DllImport("winmm.dll")] private static extern uint waveInOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveInStop(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveInReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveInClose(IntPtr handle);
    private const uint Done = 1;
    private readonly uint headerSize = (uint)Marshal.SizeOf<WaveHeader>();
    private readonly List<IntPtr> headers = new();
    private IntPtr device;
    private Task? worker;
    private volatile bool running;
    private FileStream? stream;
    private long dataLength;
    private Exception? workerError;
    public void Start(string path)
    {
        if (device != IntPtr.Zero) throw new InvalidOperationException("Already recording.");
        var format = new WaveFormat { formatTag = 1, channels = 1, samplesPerSec = 44100, avgBytesPerSec = 88200, blockAlign = 2, bitsPerSample = 16 };
        uint result = waveInOpen(out device, 0xFFFFFFFF, ref format, IntPtr.Zero, IntPtr.Zero, 0);
        if (result != 0) throw new InvalidOperationException($"Cannot open Windows default microphone (waveIn error {result}).");
        try
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.Write(new byte[44]);
            for (int i = 0; i < 4; i++)
            {
                var h = new WaveHeader { data = Marshal.AllocHGlobal(17640), bufferLength = 17640 };
                var ptr = Marshal.AllocHGlobal((int)headerSize);
                Marshal.StructureToPtr(h, ptr, false);
                headers.Add(ptr);
                Check(waveInPrepareHeader(device, ptr, headerSize), "Prepare microphone buffer");
                Check(waveInAddBuffer(device, ptr, headerSize), "Queue microphone buffer");
            }
            running = true;
            worker = Task.Run(CaptureLoop);
            Check(waveInStart(device), "Start microphone");
        }
        catch { Stop(); throw; }
    }
    private static void Check(uint code, string action) { if (code != 0) throw new InvalidOperationException($"{action}: waveIn error {code}"); }
    private void CaptureLoop()
    {
        try
        {
            while (running)
            {
                bool processed = false;
                foreach (IntPtr ptr in headers)
                {
                    var h = Marshal.PtrToStructure<WaveHeader>(ptr);
                    if ((h.flags & Done) == 0) continue;
                    processed = true;
                    if (h.bytesRecorded > 0)
                    {
                        var bytes = new byte[(int)h.bytesRecorded];
                        Marshal.Copy(h.data, bytes, 0, bytes.Length);
                        stream!.Write(bytes);
                        dataLength += bytes.Length;
                    }
                    if (running) Check(waveInAddBuffer(device, ptr, headerSize), "Requeue microphone buffer");
                }
                if (!processed) Thread.Sleep(8);
            }
        }
        catch (Exception ex) { workerError = ex; running = false; }
    }
    public void Stop()
    {
        if (device == IntPtr.Zero) return;
        running = false;
        worker?.Wait(); worker = null;
        waveInStop(device); waveInReset(device);
        foreach (IntPtr ptr in headers)
        {
            var h = Marshal.PtrToStructure<WaveHeader>(ptr);
            if (h.bytesRecorded > 0 && (h.flags & Done) != 0 && stream != null)
            {
                byte[] bytes = new byte[(int)h.bytesRecorded]; Marshal.Copy(h.data, bytes, 0, bytes.Length);
                stream.Write(bytes); dataLength += bytes.Length;
            }
            waveInUnprepareHeader(device, ptr, headerSize);
            Marshal.FreeHGlobal(h.data); Marshal.FreeHGlobal(ptr);
        }
        headers.Clear(); waveInClose(device); device = IntPtr.Zero;
        if (stream != null)
        {
            stream.Position = 0;
            using (var w = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(checked((uint)(36 + dataLength)));
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16u); w.Write((ushort)1); w.Write((ushort)1);
                w.Write(44100u); w.Write(88200u); w.Write((ushort)2); w.Write((ushort)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(checked((uint)dataLength));
            }
            stream.Dispose(); stream = null;
        }
        if (workerError != null) throw new InvalidOperationException("Audio capture error", workerError);
    }
    public void Dispose() => Stop();
}
