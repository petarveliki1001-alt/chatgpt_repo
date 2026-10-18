# Native runtime verification — work in progress

Target: Windows x64 voice sample + narration text -> cloned-voice WAV, without a Python installation. The current delivered app is unchanged by this investigation.

## Evidence obtained

- Built and executed a C# .NET 8 probe using Microsoft.ML.OnnxRuntime 1.22.0. It loaded the native runtime and reported `CPUExecutionProvider`. No Python process was invoked.
- Cross-published the same probe for Windows x64 with its native ONNX Runtime DLL. This is a build result, not a Windows execution result.
- Read the model/pipeline sources and pinned the exported model downloads to repository revisions. `models.json` records sizes and SHA-256 values supplied by Hugging Face's LFS metadata. The downloader verifies both before accepting a file.
- The selected FP32 assets total 4,611,742,691 bytes (about 4.30 GiB). None of the neural assets has been downloaded or verified yet.

## Models and feature gaps to test

| Engine | Export source | What is represented | What remains unverified |
|---|---|---|---|
| F5 | DakeQQ/F5-TTS-ONNX; linked H5N1AIDS/F5-TTS-ONNX export, revision d3af73ed2db92509a45e3edd4bf43d281306b013 | Audio/text preprocessing, diffusion transformer, audio decoder | Graph compatibility; underlying checkpoint identity; complete native internal reference transcription; generated speech; quality and performance parity |
| Chatterbox | onnx-community/chatterbox-ONNX, revision 3cab09af388d3f02bba43443fce88c1f4525ac43 | Reference speech encoder, text embeddings, full-precision autoregressive language model, conditional decoder | Native tokenization parity; CFG/sampling controls; watermark support; complete generated speech; quality and performance parity |

A C++ Chatterbox Turbo example was rejected as proof of the complete workflow because it computes new speaker embeddings in Python. The Chatterbox ONNX reference implementation inspected is itself Python code; its ONNX components can be called from .NET, but their existence alone does not demonstrate a working complete C# port.

The linked F5 prebuilt export uses an older graph interface than the current export script. The graph schemas must be inspected after download; the current script cannot simply be assumed compatible. Some F5 export settings, including guidance and inference schedules, are fixed during conversion and need checking against the Python baseline.

## Current blocker

Small Hugging Face metadata and tokenizer files are accessible. Neural files redirect to `us.aws.cdn.hf.co`, where the environment's egress proxy returns `CONNECT 403 Forbidden`. This occurs before model loading. Required destinations were saved in the cloud environment configuration draft; saving the draft does not prove that the live network policy changed. Relevant environment settings must be applied before retrying the affected downloads.

This does **not** establish a missing credential, a model incompatibility, or a successful native speech engine. No API key is required for these public files.

## Commands

Use a .NET 8 SDK. From the repository root:

```sh
dotnet run --project verification/NativeProbe -c Release
dotnet run --project verification/NativeProbe -c Release -- download verification/models.json /workspace/scratch/native-verification/verified-models
# Extract the verified F5 CPU_F32.zip, then pass .onnx paths to inspect their actual I/O schemas:
dotnet run --project verification/NativeProbe -c Release -- /path/to/model.onnx
dotnet publish verification/NativeProbe -c Release -r win-x64 --self-contained false
```

The probe currently downloads/verifies artifacts and inspects model schemas. It does not yet implement either complete speech pipeline. There is no new application release in this investigation.

## Completion criteria

1. Download and verify the actual graphs and external data; record compatible source/checkpoint versions.
2. Execute each complete native pipeline with standard reference recordings and narration texts; inspect nonempty, finite audio and content, not just an exit code.
3. Compare native output to its Python baseline with the same preprocessing, precision, parameters, and deterministic inputs where possible. Test reference changes, longer narration, repetition, and repeated requests.
4. Exercise the Windows x64 build on Windows, including CPU execution and available hardware acceleration. Report measured timings and memory; do not infer a speedup from changing languages.
5. Only then integrate a verified native engine into the desktop app. Keep versioned release directories and the two-input user workflow.

Quality, feature parity, and Windows inference remain **unverified**.
