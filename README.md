# Personal Memoirs Reader v0.1.31 — F5 repair candidate

Windows .NET 8 desktop application for voice recording and memoir reading with Windows SAPI, F5-TTS, or an existing local Chatterbox installation.

## F5 repetition repair

Version 0.1.30 guessed where two sentences ended in a long recording using word counts and a nearby quiet point. This can pair a partial or different utterance with the supplied transcript. This version removes that guess and uses an explicitly chosen excerpt. It fixes a source of conditioning errors; it does not guarantee perfect generated speech.

1. Select a clean recording. Enter the exact words of the excerpt in **Reference transcript (F5)**.
2. For a recording longer than 11 seconds, set **Reference start** and **End** to a complete phrase of 1–11 seconds (prefer 3–10 seconds). Choose boundaries between words. For an already short recording, leave both at zero to use the whole recording.
3. Click **Listen to F5 reference**. Confirm that every spoken word matches the transcript. Adjust the range or transcript until they agree.
4. Enter narration text. Set speed to **1.00** for the baseline and click **Generate with F5-TTS**.
5. If repetition persists, retain the reference WAV, transcript/range, narration text, output WAV, and **AI Log** for diagnosis.

The worker uses the standard 32 inference steps instead of 16. It rejects invalid, silent, or overly long reference excerpts before model loading. Selecting another recording clears its transcript and range. Input edits and competing AI actions are disabled during generation. Install / Verify can now reverify an existing installation.

## Run on Windows

Extract the entire `PersonalMemoirsReader_v0.1.31_win-x64.zip` and run `PersonalMemoirsReader.exe`. Keep the Python workers beside it. The .NET desktop runtime is included; Python packages and model weights are separate.

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

Linux cross-compilation and self-contained Windows packaging passed. Eight reference tests passed: real WAV slicing, stereo conversion, transcript preservation, invalid inputs, model-free preview, and generation handoff with a simulated model. Tests do not validate neural speech quality.

Windows GUI, microphone capture, actual pretrained F5 synthesis, and listening comparisons have not been verified here. This is a repair candidate; F5 quality and the broader UI are not yet finalized.
