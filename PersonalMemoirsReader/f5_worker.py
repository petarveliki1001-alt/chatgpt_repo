"""Offline F5-TTS worker with WAV decoding independent of TorchCodec/FFmpeg."""
import argparse
import os
from pathlib import Path
import tempfile

os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"

def prepare_wav(source, destination, full_transcript, start=0.0, end=None):
    """Convert the full recording, or an optional user-selected range, to mono PCM."""
    import math
    import numpy as np
    import soundfile as sf
    if not math.isfinite(start) or start < 0:
        raise ValueError("Reference start must be a finite, nonnegative time")
    with sf.SoundFile(str(source)) as recording:
        sample_rate = recording.samplerate
        duration = len(recording) / sample_rate
        if end is None:
            end = duration
        if not math.isfinite(end) or end <= start or end > duration + 1 / sample_rate:
            raise ValueError(f"Choose a reference range within the {duration:.2f}s recording")
        if int(end * sample_rate) <= int(start * sample_rate):
            raise ValueError("Reference recording or selected range is empty")
        recording.seek(int(start * sample_rate))
        data = recording.read(frames=int(end * sample_rate) - int(start * sample_rate),
                              dtype="float32", always_2d=True)
    transcript = full_transcript.strip()
    if not transcript:
        raise ValueError("Enter the exact transcript of the chosen excerpt")
    mono = data.mean(axis=1)
    if not np.isfinite(mono).all():
        raise ValueError("Reference contains invalid audio samples")
    if float(np.sqrt(np.mean(mono * mono))) < 1e-5:
        raise ValueError("Reference excerpt is silent; choose a section with clear speech")
    sf.write(str(destination), mono, sample_rate, subtype="PCM_16")
    print(f"REFERENCE: {start:.3f}–{end:.3f}s of {duration:.3f}s; transcript={transcript!r}", flush=True)
    return transcript

def install_wav_compatibility():
    import soundfile as sf
    import torch
    import torchaudio
    # torchaudio >= 2.9 routes load() through TorchCodec. For local PCM
    # WAV input, libsndfile is sufficient and needs no FFmpeg DLLs.
    def wav_load(uri, *args, **kwargs):
        if args or any(k not in ("frame_offset", "num_frames", "normalize", "channels_first", "format", "buffer_size", "backend") for k in kwargs):
            raise ValueError("Unsupported WAV load arguments")
        offset = int(kwargs.get("frame_offset", 0))
        frames = int(kwargs.get("num_frames", -1))
        with sf.SoundFile(str(uri)) as f:
            f.seek(offset)
            data = f.read(frames=frames, dtype="float32", always_2d=True)
            rate = f.samplerate
        audio = torch.from_numpy(data.copy())
        return (audio.T.contiguous() if kwargs.get("channels_first", True) else audio), rate
    torchaudio.load = wav_load
    print("PCM WAV decoder: soundfile (TorchCodec/FFmpeg not required)", flush=True)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--reference", required=True)
    parser.add_argument("--reference-text", required=True)
    parser.add_argument("--text-file", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--speed", type=float, default=1.0)
    parser.add_argument("--nfe-step", type=int, default=32)
    parser.add_argument("--reference-start", type=float, default=0.0)
    parser.add_argument("--reference-end", type=float)
    parser.add_argument("--prepare-only", action="store_true", help="Export the exact reference excerpt for audition")
    parser.add_argument("--cfg-strength", type=float, default=2.0)
    a = parser.parse_args()
    if not 0.70 <= a.speed <= 1.20:
        parser.error("--speed must be between 0.70 and 1.20")
    if not 8 <= a.nfe_step <= 64:
        parser.error("--nfe-step must be between 8 and 64")
    import re
    def normalized_words(text):
        return re.findall(r"[^\W_]+", text.casefold(), flags=re.UNICODE)
    reference_text = Path(a.reference_text).read_text(encoding="utf-8-sig").strip()
    target_text = Path(a.text_file).read_text(encoding="utf-8-sig").strip()
    ref_tokens = normalized_words(reference_text)
    target_tokens = normalized_words(target_text)
    if not ref_tokens or not target_tokens:
        raise ValueError("Reference transcript and narration text must not be empty")
    if ref_tokens == target_tokens:
        print("INFO: reference and narration text match; generation permitted", flush=True)
    print("Reference transcript: %d words; narration: %d words" % (len(ref_tokens), len(target_tokens)), flush=True)
    import soundfile as sf
    if a.prepare_only:
        prepare_wav(a.reference, a.output, reference_text, a.reference_start, a.reference_end)
        return
    with tempfile.TemporaryDirectory(prefix="pmr_f5_") as temp:
        reference = Path(temp) / "reference.wav"
        aligned_reference_text = prepare_wav(a.reference, reference, reference_text, a.reference_start, a.reference_end)
        install_wav_compatibility()
        from f5_tts.api import F5TTS
        print("Loading F5-TTS pretrained model", flush=True)
        model = F5TTS(device="cpu")
        print(f"FRESH F5 PROCESS; speed={a.speed:.2f}; nfe_step={a.nfe_step}; cfg_strength={a.cfg_strength:.2f}", flush=True)
        model.infer(ref_file=str(reference),
                    ref_text=aligned_reference_text,
                    gen_text=target_text,
                    file_wave=a.output,
                    speed=a.speed,
                    nfe_step=a.nfe_step,
                    cfg_strength=a.cfg_strength)
    result = Path(a.output)
    if not result.is_file() or result.stat().st_size < 44:
        raise RuntimeError("F5 did not produce a valid output WAV")
    info = sf.info(str(result))
    print(f"Saved: {result} ({info.duration:.2f}s, {info.samplerate} Hz)", flush=True)

if __name__ == "__main__":
    main()
