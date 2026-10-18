# Personal Memoirs Reader v0.1.33 — F5 repair candidate

Windows .NET 8 desktop application for voice recording and memoir reading with Windows SAPI, F5-TTS, or an existing local Chatterbox installation.

## Reference handling

The application no longer imposes an 11-second (or one-second minimum) reference duration restriction. Full recordings are accepted and passed to F5 by default, with their supplied transcript. Start/end controls remain optional: leave both at zero to use the whole recording. The application does not guess where sentences end or silently substitute the first two transcript sentences.

F5's own preprocessing is unchanged and may shorten long reference audio internally. Accepting the complete file at the application boundary does not bypass that library behavior or guarantee repetition-free output.

The reference transcript is optional. Leave it blank to let F5 transcribe its own processed reference audio, after any internal trimming, so transcription describes the audio F5 actually uses. Entering text remains a manual override. Narration text is still required.

For an existing installation, run **Install / Verify F5** once with internet access to acquire F5's automatic transcription model (`openai/whisper-large-v3-turbo`, a substantial additional download). Setup verifies speech and transcription model loading in a fresh process with offline flags. Subsequent generation stays offline. Existing speech-only readiness markers do not establish that the transcription model is cached. Missing transcription weights are reported with instructions to run setup or enter a manual transcript.

Transcription decodes WAV audio through SoundFile and sends a mono 16 kHz array to the ASR pipeline; it does not require FFmpeg to decode the reference. Automatic transcription may be slow on CPU and can make recognition errors. Its result is recorded in AI Log. The worker uses 32 inference steps. Input validation still rejects empty or silent recordings, nonfinite samples, and invalid optional ranges. Competing AI actions and input edits are disabled during generation.

## Run on Windows

Extract the entire `PersonalMemoirsReader_v0.1.33_app-only.zip` and run `PersonalMemoirsReader.exe`. Keep the Python workers beside it. Microsoft .NET 8 Desktop Runtime x64 must be installed separately. Python packages and model weights are also separate.

Existing F5 environments and caches under `%LOCALAPPDATA%\PersonalMemoirsReader` are reused. If needed, use **Install / Verify F5** with internet access. The existing installer looks for Python 3.11 at `C:\Program Files\Python311\python.exe` or the base interpreter of a detected Chatterbox environment. Python installed elsewhere is not yet selectable in the UI. Generation uses local models with offline flags. Recordings and narrations stay in `Documents\Personal Memoirs Reader`.

Reference audition needs only the F5 environment's NumPy and SoundFile packages and loads no neural models. Chatterbox requires its separate existing installation. FFmpeg is optional for non-WAV imports.

## Build and test

From the repository root with a .NET 8 SDK:

```sh
dotnet build PersonalMemoirsReader.sln -c Release -p:EnableWindowsTargeting=true
dotnet publish PersonalMemoirsReader/PersonalMemoirsReader.csproj -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -o artifacts/win-x64
```

Reference tests on Windows or Linux (Python 3.11+):

```sh
python -m pip install -r requirements-test.txt
python -m unittest discover -s tests -v
```

## Validation and remaining work

Linux cross-compilation and self-contained Windows packaging passed. Fifteen reference/setup tests passed: real WAV slicing, stereo conversion, transcript preservation, invalid inputs, model-free preview, and generation handoff with a simulated model. Tests do not validate neural speech quality.

Windows GUI, microphone capture, actual pretrained speech/transcription model acquisition and inference, and listening comparisons have not been verified here. Pipeline tests use simulated model responses. This is a repair candidate; F5 quality and the broader UI are not yet finalized.
