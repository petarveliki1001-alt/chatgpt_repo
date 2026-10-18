# CPU performance investigation

The user's 2026-10-18 Windows generation log measured 17 seconds of startup/model loading, 38 seconds of reference preparation/transcription, and approximately 817 seconds of synthesis for 11.69 seconds of output. Synthesis, rather than transcription, dominated this run. The existing desktop worker explicitly uses CPU and does not configure PyTorch intra-op/inter-op thread counts. This does not prove that its default thread count caused the slowdown.

The replacement native F5 prototype now accepts an optional explicit thread count:

```
NativeProbe f5 <model-directory> <vocabulary> <reference-wav> <reference-transcript> <narration> <output-wav> [threads]
```

This command uses the supplied reference transcript and never invokes automatic transcription. The native prototype remains separate from the desktop app. Its report now separates model loading, reference preparation, transformer steps, and decoding. The existing 31 integration intervals and FP32 weights are unchanged.

A diagnostic command compares one, two, and four intra-op threads with one inter-op thread:

```
NativeProbe cpu-benchmark <model-directory> <captured-wav-prefix> <report.json>
```

First generate a native F5 output with the preceding command. It saves `.input.pcm16`, `.input.ids.i32`, `.input.noise.f32`, and `.input.json` alongside the WAV; supply that WAV path as the capture prefix. These capture files contain reference audio/text information; keep them local unless deliberately sharing them.

The diagnostic reuses identical captured reference PCM, text IDs, noise and the first integration index. Each setting gets one warmup and three timed transformer calls, in sequential 1/2/4-thread order. Loading and preprocessing are excluded from step timings. It checks finite outputs and records checksums. It does not generate a complete narration, assess sound quality, compare Python with native execution, or establish the best thread count for another computer. Shared-host load and fixed test order limit precision. No Python, Whisper, new model download, GPU, reduced steps, or quantization is involved in this benchmark.

Measured results are in `results/linux/cpu-thread-benchmark.json`. The host is an AMD EPYC 9V74 virtual machine exposing five logical CPUs with a cgroup quota equivalent to four CPUs. These results cannot be represented as measurements on the user's engineering workstation. Do not select a universal production thread count from this one short test. The earlier complete native Windows run also needed about 192 seconds for 4.04 seconds of speech, so native execution alone has not demonstrated acceptable interactive F5 CPU performance.

No new desktop release is made from this investigation. The next product decision requires timing a representative CPU path on the target hardware against an explicit acceptable generation time. Keeping sessions loaded addresses startup overhead; it does not eliminate the measured transformer computation.

## Result of this investigation

Median single-step times were 9.363 seconds (one thread), 6.222 seconds (two), and 2.687 seconds (four). Four was fastest among these settings on this host. A subsequent complete four-thread run measured 1.746 seconds model loading, 0.173 seconds preparation, 87.387 seconds transformer work, and 0.075 seconds decoding for 4.043 seconds of output. That is approximately 21.7 times playback duration for inference alone. See `results/linux/cpu-four-thread-full.json`.

These timings confirm that the transformer dominates this native CPU run as well. They do not demonstrate a repair of the user's Windows slowdown, nor a native-versus-Python speedup: the hardware, reference, target length and execution conditions differ from the user's run, and the existing native prototype already used four threads. No transcription or listening assessment was performed for this run. No model quality settings were changed. The useful changes are reproducible thread measurement and stage reporting, rather than a claimed application performance fix.
