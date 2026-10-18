using System.Runtime.CompilerServices;
namespace MemoirsReader.Diagnostics;
internal static class TelemetryPolicy
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY","1");
    }
}
