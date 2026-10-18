# Memoirs Reader v0.1.36

Windows x64 desktop app. Select a voice recording, enter narration text, and generate speech in the sample voice. The editable WinForms project and executable are delivered together in `MemoirsReader_v0.1.36.zip`, under the folder `MemoirsReader_v0.1.36/`.

## Open or run

- **Run:** open `MemoirsReader.exe`. Requires Microsoft .NET 8 Desktop Runtime x64. A dedicated GPU and Python are not required.
- **Edit:** open `MemoirsReader.sln` in Visual Studio 2022 17.8+ with the .NET desktop development workload. Right-click `MainForm.cs` → **View Designer**. Press F5 to build/run. Source files remain separate from Designer layout.
- **First speech use:** click **Set up Chatterbox**, then **Generate with Chatterbox**. Setup displays its native-model download size (about 3.27 GB) before downloading. Models are verified with pinned SHA-256 hashes. Generation subsequently runs offline. Models and environments from the old Python implementation are not used or changed.

## Engines and behavior

**Chatterbox** is the direct sample-plus-text path: no reference transcript is needed. English ONNX FP32 models run through C# and native ONNX Runtime. Longer text is split at sentence/word boundaries, with every section generated in order and a short pause between sections. A missing stop token fails the job rather than silently accepting truncated audio. The native decoder uses greedy generation and fixed exaggeration 0.5; full parity with the previous Python sampling controls is not claimed.

**F5** is optional and remains CPU intensive. It requires the exact words spoken in the sample. Enter a known script/manual transcript once; it is saved under a hash of that sample's content and reloaded when that sample is selected again. There is no automatic transcription, Whisper dependency or download. F5 setup downloads the verified native model archive and English vocabulary (about 1.34 GB, plus extraction disk space). This native export uses 31 integration intervals from 32 time points. Speaking speed controls generated duration; the native text path currently supports English. Full recordings are used without an application duration cutoff or silent internal clipping. Large references/narrations can be expensive; a matching transcript must describe the entire selected file.

The active engine stays loaded for the life of the app and caches the last sample's prepared conditioning. Switching engines releases the previous engine to limit memory use. CPU threads default to the available processor count capped at four, a conservative measured starting point rather than workstation-specific tuning. No GPU package is installed. No real-time CPU performance is promised: the earlier native F5 CPU test took about 88 seconds for 4 seconds of audio on the hosted test machine.

**Stop AI / Playback** requests cancellation between native model calls; the active call must finish first. Canceled generation does not publish an incomplete WAV. Closing while a job is active requests cancellation and waits for that call before releasing native resources. Setup downloads can also be canceled. The live Speech Log records failures and generation phases. Output JSON/text records model reuse, reference reuse, loading time, synthesis time, and output duration.

Recording and WAV import, installed Windows voices, sample playback, and comparisons remain available. The Windows voice is ordinary system speech, not voice cloning. WAV references must be PCM16 or float32; other formats require a separately selected FFmpeg executable for conversion. Voice sample names are displayed without their directory prefixes.

## Data and previous versions

All program files live in the versioned extraction folder. Existing recordings/narrations in `Documents\Personal Memoirs Reader` are retained. Native models and saved F5 transcripts live in `%LOCALAPPDATA%\MemoirsReader`. The app does not uninstall Python or delete previous models. Each changed release gets a new version; archives are never overwritten with different contents under the same version.

For development/automation, `MEMOIRS_MODEL_ROOT` overrides the native model directory and `MEMOIRS_DATA_ROOT` overrides the recording/output directory. Keep reference samples and generated input metadata private unless deliberately sharing them.

## Build and verification

```
dotnet build MemoirsReader.sln -c Release
dotnet run --project NativeSpeech.Tests -c Release -- NativeSpeech/models.json
dotnet publish PersonalMemoirsReader -c Release -r win-x64 --self-contained false -o artifacts/app
pwsh tools/package-release.ps1
```

Linux cross-builds require `-p:EnableWindowsTargeting=true`. No Python scripts are part of the current app or release workflow. Archived Python verification scripts and older ZIPs in the repository are historical and are not packaged.

`.github/workflows/desktop-release.yml` builds on Windows, runs the C# checks, downloads/verifies the two engines' models with no ASR, opens the actual form, generates through desktop handlers, verifies warm Chatterbox engine/reference reuse and F5 transcript persistence, validates WAV outputs, and packages only after these checks pass. Results distinguish pipeline verification from sound quality: neither automatic transcription nor a human listening assessment is performed. Interactive Visual Studio Designer behavior is prepared in source but is not exercised by that smoke test. Neural output can still mispronounce, repeat, or differ from a sample; this release does not claim universal voice-quality or performance parity.
