# Native CPU study — 18 October 2026

This study is separate from Memoirs Reader v0.1.36. Production application code and its model manifest are unchanged. No Python or Whisper was used, and no additional pretrained model weights were downloaded. A language-model quantization experiment was derived locally from the existing Chatterbox weights. PocketTTS was researched and a native-library harness was compiled, but its synthesis has NOT been tested: its model download requires the user's agreement.

## Measurements on the hosted Linux CPU

The host exposes five logical processors with a cgroup quota equivalent to four CPUs. Tests use four inference threads, existing FP32 weights, and the same public 5.33-second reference recording from the official F5 examples. These are sequential exploratory measurements on a shared host, not randomized controlled trials or measurements of the user's workstation. Operator profiling also adds overhead. Decoder randomness means independently generated WAVs are not expected to be identical.

| Chatterbox variant | Loading | First synthesis | Repeated synthesis | Audio |
|---|---:|---:|---:|---:|
| Original weights, saving an optional optimized cache | 37.21 s | 11.56 s | 11.80 s | 2.44 s |
| Reloading that optimized cache | 34.31 s | 11.03 s | 10.22 s | 2.44 s |
| Thread spinning disabled | 37.06 s | 11.83 s | 11.08 s | 2.44 s |
| Decoder prepacking disabled | 31.48 s | 11.62 s | 9.47 s | 2.44 s |
| Locally quantized language model | 35.34 s | 11.28 s | 9.04 s | 2.52 s |
| Newer ONNX Runtime 1.31.0, original weights | 72.55 s | 12.08 s | 10.12 s | 2.44 s |

The cached decoder used approximately 520 MiB of extra local storage without eliminating initialization time. The small differences between runtime flags do not demonstrate a reliable large improvement; the number of samples and host variability do not justify recommending one as a universal setting. No extra cache, alternate runtime, or runtime-flag change has been added to the app.

The profiled original-weight repeated short run spent 3.70 s in the language model, 6.76 s in the audio decoder, 0.045 s embedding tokens, and less than 0.001 s checking the already prepared reference. A longer three-section request generated 15.52 s of audio in 60.71 s with the model kept loaded and the voice preparation reused. Decoder work totaled 36.40 s and language-model work 23.96 s. Matrix operations dominate both parts; text handling and repeatedly reading the sample are not the main costs.

The quantized language stage fell to 1.54–1.57 s, but decoding still took 7.45–9.05 s. It generated 62 speech tokens instead of the original 60, so its acoustic sequence changed. This does not prove that the spoken words are wrong, but it does not establish unchanged output or quality parity. Native speaker-embedding diagnostics overlap with baseline variation (baseline 0.762–0.803, quantized 0.771–0.786); these scores do not assess wording or naturalness. No ASR or listening assessment was performed. The approximation is not enabled in the app.

F5 full synthesis took 103.50 s for 4.04 s of audio with profiling enabled. A separate three-call transformer profile, using identical captured first-step inputs, attributed 8.81 of 10.35 measured seconds (about 85%) to MatMul/FusedMatMul. This confirms expensive neural computation; it does not establish that another wrapper language can remove that cost.

Raw timing summaries, generated token sequences, operator summaries, and similarity diagnostics are in `results/cpu-study/linux/`. Full operator traces, derived weights and WAVs remain outside the source package. The first profiling pass had a filename collision for the tiny embedder and language-model sessions; only the surviving language-model trace is included, with the duplicate explicitly removed. Later source uses separate session options and trace names. Stage stopwatch measurements are unaffected.

## Reproduce from source

Open `CpuStudy.sln` in Visual Studio 2022 with .NET 8. These are console development tools, not a replacement desktop application. Building/restoring uses pinned NuGet dependencies. No executable is supplied. No model is downloaded merely by building or running the information commands.

```
dotnet run --project NativeSpeech.Benchmarks -c Release -- --native-info
dotnet run --project NativeSpeech.Benchmarks -c Release -- chatterbox <existing-model-root> <reference.wav> <new-results-directory> --profile
dotnet run --project NativeSpeech.Benchmarks -c Release -- chatterbox <existing-model-root> <reference.wav> <new-results-directory> --no-spinning --short-only
dotnet run --project NativeSpeech.Benchmarks -c Release -- chatterbox <existing-model-root> <reference.wav> <new-results-directory> --no-prepacking --short-only
```

`<existing-model-root>` defaults in the app to `%LOCALAPPDATA%\MemoirsReader\NativeModels`. Supply it explicitly to these tools. F5 full profiling needs its known reference transcript; the default F5 example text is allowed only with the exact official reference fixture. For another recording, supply `--reference-text <text-file>`.

The isolated quantizer requires a NEW destination graph next to the original so unchanged external initializers can still be resolved. It never overwrites the original graph or weight file. The experimental derived graph alone does not replace the app's normal graph.

```
dotnet run --project experiments/QuantizeOnnx -c Release -- <model-root>/chatterbox/onnx/language_model.onnx <model-root>/chatterbox/onnx/language_model.q8.onnx
dotnet run --project NativeSpeech.Benchmarks -c Release -- chatterbox <model-root> <reference.wav> <new-results-directory> --q8 --short-only
```

For an optional runtime comparison, pass `-p:OrtVersion=1.31.0`; the production project's dependency remains 1.22.0. The newer runtime emitted a telemetry-initialization notice during its first test. Later harness code explicitly opts out using the environment setting and API. For initial opt-out before loading native libraries, set `ORT_DISABLE_TELEMETRY=1` in the launching process as well. The newer runtime is not adopted by the app. No evidence about audio transmission is inferred from a device-ID notice.

## Candidate awaiting permission: PocketTTS

This is a candidate, NOT a verified solution. Its upstream design targets CPU voice cloning without reference transcription. The maintained sherpa-onnx project includes a native C# zero-shot example. The isolated `experiments/PocketProbe` pins sherpa-onnx 1.13.8, builds under .NET 8, and can query its native version without loading any model.

The pinned candidate manifest identifies seven assets totaling 198,291,829 bytes (about 198 MB), from `csukuangfj2/sherpa-onnx-pocket-tts-int8-2026-01-26` revision `e715955cf50d18d919d37231513c0e914b83661a`. Graphs have published SHA-256 checksums; the small tokenizer JSON files use their pinned Git blob hashes. No test voices need to be downloaded. No candidate asset has been downloaded in this study so far.

The download command is separate and requires an explicit `--approved` argument. It must not be run before the user's agreement. Inference takes a voice WAV and narration text; it has no transcript input. It passes the full recording length rather than copying an arbitrary excerpt cap from the upstream example. Actual inference, voice quality, Windows performance and behavior with long references remain unverified until the weights are approved and tested.

References: [PocketTTS design](https://github.com/kyutai-labs/pocket-tts), [native C# zero-shot example](https://github.com/k2-fsa/sherpa-onnx/tree/master/dotnet-examples/pocket-tts-zero-shot), [pinned model files](https://huggingface.co/csukuangfj2/sherpa-onnx-pocket-tts-int8-2026-01-26/tree/e715955cf50d18d919d37231513c0e914b83661a).
