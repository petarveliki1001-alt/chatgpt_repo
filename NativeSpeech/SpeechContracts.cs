namespace MemoirsReader.Native;

public sealed record NarrationRequest(string Engine, string Reference, string ReferenceTranscript, string Text, string Output, double Speed = 1);
public sealed record NarrationReport(string Engine, int Threads, string Reference, string ReferenceTranscript, string Text,
    bool ReusedModel, bool ReusedReference, double LoadSeconds, double SynthesisSeconds, double DurationSeconds, double[] ChunkSeconds);
