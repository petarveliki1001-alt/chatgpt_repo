# Reference comparisons (development only)

These Python scripts record the comparison commands executed in the verification workspace. They are not application runtime components and are not intended for installation on the user's PC. Paths are the actual investigation workspace paths; adjust them for another machine.

- `f5-torch-reference.py`: official F5-TTS 1.1.22 / PyTorch baseline, CPU, 32 steps, cfg=2, speed=1, seed=9527. It reuses the original uploaded worker's SoundFile WAV compatibility shim. Its output transcribed to the requested narration.
- `chatterbox-onnx-reference.py`: calls the inspected TigreGotico/chatterbox-onnx Python reference against the same pinned FP32 graphs, audio, tokenizer, and greedy sampling parameters. The 60 generated speech tokens exactly match the C# implementation. The conditional decoder is stochastic; independently generated WAVs are not sample-identical. Both transcribed correctly.
- `f5-onnx-controlled.py`: uses the publisher's 31-interval CPU inference loop with the exact PCM, text IDs, and starting noise captured by the C# probe. It produces the same 97,024 PCM samples, with maximum difference 0. This checks the bindings and tensor flow; it does not establish that every ONNX export is identical to the upstream PyTorch model.

Inputs for F5 controlled comparisons (`*.input.*`) are generated automatically by the C# probe and intentionally excluded from committed results. Model files stay outside Git and have pinned SHA-256 values in `verification/models.json`.
