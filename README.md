# Memoirs Reader v0.1.37

Windows x64 WinForms application: select a voice recording, enter narration, and generate speech in that voice. This delivery is **editable source only**, under `MemoirsReader_v0.1.37/` in `MemoirsReader_v0.1.37.zip`. No compiled executables, DLLs, Python, Whisper, model weights, or research projects are bundled.

## Open in Visual Studio

Extract to a new folder and open **MemoirsReader.sln** in Visual Studio with the **.NET desktop development** workload and .NET 8 SDK. Set **MemoirsReader** as the startup project, then press F5. NuGet restores the pinned native runtime dependencies. Visual Studio compiles the C# programs on your PC.

To edit the form, right-click `PersonalMemoirsReader/MainForm.cs` and choose **View Designer**. Its controls and layout are in `MainForm.Designer.cs`; runtime model loading occurs after the form opens, outside Designer initialization. Interactive Visual Studio Designer operation has not been automated.

The solution contains three projects: MemoirsReader (the form), NativeSpeech (F5/Chatterbox and model management), and PocketWorker (the C# PocketTTS host). Building the startup project also builds PocketWorker and places it in `SpeechWorker` beside the application. It runs as a retained native worker to keep its runtime separate from the other engines; **it is not Python**. All its source is included. Building does not download speech models. The archive has no prebuilt application executable.

## What changed

- Added **PocketTTS** as an explicitly named CPU option alongside F5 and Chatterbox. It takes the sample audio and narration directly, with no reference transcript, transcription model, or GPU requirement. The full recording is passed without an application duration cutoff. Native model behavior and speaker similarity still require listening assessment; identical quality across these different engines is not claimed.
- PocketTTS keeps its worker and speech model loaded between requests. Stop interrupts its worker and discards incomplete output; the next request starts it again. The other engines retain their prior cancellation behavior: an active native operation finishes before stopping.
- Added **Use narration as sample script** for F5. Click it only when Narration contains exactly the words in the complete sample recording; it saves the script for reuse with that sample. It does not infer that unrelated narration matches a recording and does not transcribe anything.
- Changed delivery to source only, with a simple, matching versioned ZIP and folder.

## Speech engines and model locations

Choose **Set up PocketTTS**, **Set up Chatterbox**, or **Set up F5** once for the engine you want. Setup asks before downloading, verifies SHA-256 checksums against pinned revisions in `NativeSpeech/models.json`, and then generation works offline. Installing one engine does not require installing the others.

PocketTTS downloads about **198 MB** (English int8 ONNX export, native sherpa-onnx 1.13.8). Chatterbox downloads about 3.28 GB; F5 about 1.34 GB plus extraction space. Existing F5/Chatterbox model paths remain unchanged:

```
%LOCALAPPDATA%\MemoirsReader\NativeModels\pocket
%LOCALAPPDATA%\MemoirsReader\NativeModels\chatterbox
%LOCALAPPDATA%\MemoirsReader\NativeModels\f5
```

Existing recordings and generated audio remain under `Documents\Personal Memoirs Reader`. F5 scripts remain under `%LOCALAPPDATA%\MemoirsReader\Transcripts`, keyed by the recording's content. Versioned source folders coexist; recordings/models are shared. No previous Python installation or cache is removed or used.

Chatterbox uses the sample without a transcript, splits long narration at sentence/word boundaries, uses greedy decoding and fixed exaggeration 0.5. Its model and prepared reference are retained while it remains the active engine. F5 requires a matching reference script, remains CPU intensive, uses 31 integration intervals, and supports English in this export. Switching engines releases the previous engine to bound memory. Removing Python alone does not make those older models fast.

The Speech Log and each generated `.wav.json` record engine, threads, model/reference reuse, loading time, synthesis time, and output duration. Cancellation rejects unfinished WAVs. Use **Compare WAV Files** to compare generated speech against the source recording. Model speed measurements and valid WAV output do not establish pronunciation accuracy or voice fidelity.

## Validation and dependencies

The Windows release workflow packages the source, extracts that exact ZIP, builds it, loads the form, generates audio through desktop handlers, checks cold/warm PocketTTS and Chatterbox use, generates longer PocketTTS narration, interrupts/restarts PocketTTS, checks F5 script persistence and generation, and reads finalized WAVs. Results are saved separately in `verification/results/desktop-v0.1.37/` in the repository; they are not bundled research tools. No automatic transcription or human listening assessment is performed by this check.

The primary native dependencies are ONNX Runtime 1.22.0 for the existing engines and sherpa-onnx 1.13.8 for PocketTTS. Runtime packages are restored by NuGet. No Python operation is required to build, package, or run this version. The PocketTTS model export is pinned to `csukuangfj2/sherpa-onnx-pocket-tts-int8-2026-01-26`, revision `e715955cf50d18d919d37231513c0e914b83661a`; see the included model license and upstream model card before redistribution/commercial use (the card states non-commercial use). Credits: Kyutai PocketTTS, KevinAHM ONNX export, and k2-fsa sherpa-onnx. Licenses are under `NativeSpeech/licenses`.

To run the existing native checks: `dotnet run --project NativeSpeech.Tests -c Release -- NativeSpeech/models.json`. To create a fresh versioned source ZIP after changing the version: `pwsh tools/package-release.ps1`. The packager refuses to replace an existing delivery. No extra title suffixes are used.
