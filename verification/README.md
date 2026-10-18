# Native F5 and Chatterbox verification

**Verified:** both engines generated the requested English narration on an actual Windows x64 runner through C# and native libraries, without invoking a Python worker. The test inputs were a reference WAV and narration text. F5's internal reference transcription also ran natively, through whisper.cpp / Whisper.net.

This verifies a working Python-free engine route. It does not claim complete feature/quality parity or a completed migration of the desktop app. The delivered desktop app remains unchanged.

## Actual Windows run

[Windows verification run 39150695591](https://github.com/petarveliki1001-alt/chatgpt_repo/actions/runs/39150695591) completed successfully against code commit `2492963251d01ce59bc9b870af3143ab1b7aee3c`. Its results were committed by the Windows runner, not synthesized from local build output.

- OS reported by the runner: `Microsoft Windows 10.0.26100`, x64, 4 logical CPUs.
- Runtime: .NET 8, ONNX Runtime 1.22.0, Whisper.net/native whisper.cpp 1.9.1, CPU execution.
- Input reference: F5's published English example WAV. Its words are “Some call me nature, others call me mother nature.”
- Narration: **The small boat crossed the quiet lake.**
- Both resulting WAVs transcribed to the exact requested words after case/punctuation normalization. The reference words were not present in those transcriptions.
- The model downloader checked all selected model file sizes and SHA-256 values before accepting them. `models.json` records the exact revisions and hashes.

| Engine | Windows output | Result |
|---|---|---|
| Chatterbox ONNX FP32 | [Listen/download](results/windows/chatterbox.wav) | 60 generated speech tokens; 2.44 s WAV; target text matched |
| F5 ONNX FP32 + native reference transcription | [Listen/download](results/windows/f5.wav) | 4.043 s WAV; target text matched |

Raw logs, timing JSON, transcriptions, OS information, and the runner's outcome are in [results/windows](results/windows). The test workflow uses .NET commands; it does not install or execute Python for inference.

## Comparisons and additional local checks

1. **Chatterbox C# vs Python ONNX reference:** all 60 generated speech tokens matched exactly. Tokenization also matched exactly. Each result transcribed correctly. Conditional decoding is stochastic, so independent WAVs are not sample-identical. The Windows C# run also produced the same 60 speech tokens as the Linux C# run.
2. **F5 C# vs Python ONNX bindings:** with identical captured PCM input, text IDs, and initial noise, all **97,024 PCM samples matched exactly**, maximum difference **0**. Independent Windows/Linux runs use different random noise and are not sample-identical; both produced the requested words.
3. **Official F5 PyTorch baseline:** F5-TTS 1.1.22, standard 32-step API inference, cfg=2, speed=1, seed=9527 also produced a WAV that transcribed correctly. The controlled ONNX comparison is not a claim that the export is numerically identical to every PyTorch configuration.
4. **Second reference voice:** Chatterbox C# generated the same narration using another reference. Its output transcribed correctly and produced 57 speech tokens instead of 60. Speaker-embedding diagnostics associated it more strongly with its intended reference (cosine 0.863) than the first output (0.636). The first output scored 0.794 against its intended reference. These are diagnostics, not perceptual quality ratings.
5. F5's local C# output scored 0.817 against the intended reference; the PyTorch output scored 0.766. An unrelated reference scored 0.538. These numbers do not establish that native speech is subjectively better.

See [results/linux](results/linux) and [baselines](baselines) for evidence and reference comparison scripts. Python was used only to run reference comparisons in the development environment; the native Windows pipelines do not depend on those scripts.

## A compatibility problem actually found

The selected published F5 archive has **32 time points / 31 integration intervals**. A 32nd transformer call fails with an out-of-range Gather index. The probe now uses the same 31-interval loop as the publisher's CPU reference. The current upstream exporter has a newer interface and cannot be substituted without adapting and validating the pipeline.

The original app's reference preparation was also exercised against real speech: native F5 preprocessing shortened a 16.00 s recording to 11.58 s while retaining the supplied full transcript. The new native probe automatically transcribes the audio it actually conditions on; it does not guess a two-sentence transcript or ask the user to align text manually.

## Performance: measured, not promised

Windows measurements for this one CPU test:

| Stage | Chatterbox | F5 |
|---|---:|---:|
| Model/session loading | 70.4 s | 1.8 s |
| Generation | 16.7 s | 191.5 s |
| Internal reference transcription | Not needed | 34.0 s, plus transcription-model loading |
| Output duration | 2.44 s | 4.04 s |

These are cold command-line probe measurements on a shared hosted machine, not a controlled language-speed benchmark or a prediction for the user's PC. Native F5 remains slow on this CPU. Removing Python does not remove the neural computation. A production integration should retain sessions and prepared references rather than pay cold startup costs per narration. GPU acceleration has not been tested.

## Scope limits — full parity is not claimed

- Tested English narration, short outputs, one reference for F5 and two references for Chatterbox. Long memoirs, broad voice robustness, other languages, and listening quality are not comprehensively validated.
- The F5 probe implements English text preparation only. Chinese tokenization/pinyin handling still needs a native implementation and tests before claiming the original model's full language coverage.
- This F5 export fixes guidance and its integration schedule at conversion time. The probe uses speed 1.0. Other controls require implementation/validation or a suitable re-export.
- Chatterbox was checked against its Python **ONNX** reference with greedy decoding and exaggeration 0.5. Full PyTorch sampling/CFG feature parity and native watermark support are not established.
- Actual ASR and speaker-embedding checks do not replace human listening tests.
- This is a verified engine prototype, not a replacement desktop ZIP. The existing desktop versions still use their original Python backends.

## Reproduce without Python

With .NET 8, from the repository root:

```sh
dotnet build verification/NativeProbe -c Release
dotnet verification/NativeProbe/bin/Release/net8.0/NativeProbe.dll download verification/models.json /path/to/models
# Extract /path/to/models/f5/CPU_F32.zip into /path/to/models/f5.
dotnet verification/NativeProbe/bin/Release/net8.0/NativeProbe.dll chatterbox /path/to/models/chatterbox /path/to/models/fixtures/reference-en.wav "The small boat crossed the quiet lake." chatterbox.wav
dotnet verification/NativeProbe/bin/Release/net8.0/NativeProbe.dll f5-auto /path/to/models/f5 /path/to/models/fixtures/f5-vocab.txt /path/to/models/whisper/ggml-large-v3-turbo.bin /path/to/models/fixtures/reference-en.wav "The small boat crossed the quiet lake." f5.wav
dotnet verification/NativeProbe/bin/Release/net8.0/NativeProbe.dll verify-text /path/to/models/whisper/ggml-large-v3-turbo.bin "The small boat crossed the quiet lake." chatterbox.wav f5.wav
```

The first-time selected downloads are about 5.81 GiB; there are no model weights in Git. The original data/weights are public downloads; no user credential was needed. The earlier cloud CDN block has cleared and is not an outstanding blocker.

## Sources

- [DakeQQ/F5-TTS-ONNX](https://github.com/DakeQQ/F5-TTS-ONNX), linked FP32 export `H5N1AIDS/F5-TTS-ONNX` revision `d3af73ed2db92509a45e3edd4bf43d281306b013`.
- [onnx-community/chatterbox-ONNX](https://huggingface.co/onnx-community/chatterbox-ONNX), revision `3cab09af388d3f02bba43443fce88c1f4525ac43`.
- [TigreGotico/chatterbox-onnx](https://github.com/TigreGotico/chatterbox-onnx), Python reference revision `c40af1aa4f4c0973a2c69c9f408b0246cd46d3e8`.
- [ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp), model revision `5359861c739e955e79d9a303bcbc70fb988958b1`.

The Chatterbox Turbo C++ example that precomputes new voice embeddings in Python was not used as evidence of a complete Python-free pipeline.
