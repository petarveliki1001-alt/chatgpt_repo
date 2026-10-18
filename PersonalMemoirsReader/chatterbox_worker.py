"""Offline Chatterbox memoir renderer with resumable WAV chunks."""
import os
os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"
import argparse
import hashlib
import json
import re
import wave
import tempfile
from pathlib import Path

MAX_CHARS = 230

def split_text(text):
    paragraphs = re.split(r"\n\s*\n", text.strip())
    result = []
    for paragraph in paragraphs:
        sentences = re.split(r"(?<=[.!?…])\s+", paragraph.strip())
        chunk = ""
        for sentence in sentences:
            # Split very long sentences at word boundaries.
            words = sentence.split()
            parts = []
            part = ""
            for word in words:
                if part and len(part) + len(word) + 1 > MAX_CHARS:
                    parts.append(part)
                    part = ""
                if len(word) > MAX_CHARS:
                    if part:
                        parts.append(part)
                        part = ""
                    parts.extend(word[i:i + MAX_CHARS] for i in range(0, len(word), MAX_CHARS))
                else:
                    part = (part + " " + word).strip()
            if part:
                parts.append(part)
            for piece in parts:
                if chunk and len(chunk) + len(piece) + 1 > MAX_CHARS:
                    result.append(chunk)
                    chunk = ""
                chunk = (chunk + " " + piece).strip()
        if chunk:
            result.append(chunk)
    return result

def validate_pcm(path):
    """Reject float/non-PCM, empty, truncated and malformed checkpoint WAVs."""
    if not path.is_file() or path.stat().st_size < 48:
        return False
    try:
        with wave.open(str(path), "rb") as reader:
            if reader.getcomptype() != "NONE" or reader.getnchannels() != 1 or reader.getsampwidth() != 2:
                return False
            if reader.getframerate() < 8000 or reader.getnframes() < reader.getframerate() // 5:
                return False
            expected = reader.getnframes() * reader.getnchannels() * reader.getsampwidth()
            actual = 0
            while True:
                data = reader.readframes(32768)
                if not data:
                    break
                actual += len(data)
            return actual == expected
    except (wave.Error, EOFError, OSError, ValueError):
        return False

def merge_wavs(files, output):
    for path in files:
        if not validate_pcm(path):
            raise RuntimeError(f"Invalid or incomplete PCM WAV section: {path}")
    tmp = output.with_suffix(".assembling.wav")
    expected = None
    try:
        with wave.open(str(tmp), "wb") as writer:
            for path in files:
                with wave.open(str(path), "rb") as reader:
                    params = (reader.getnchannels(), reader.getsampwidth(), reader.getframerate())
                    if expected is None:
                        expected = params
                        writer.setnchannels(params[0]); writer.setsampwidth(params[1]); writer.setframerate(params[2])
                    elif params != expected:
                        raise RuntimeError(f"Section WAV format differs: {path}")
                    while True:
                        data = reader.readframes(32768)
                        if not data:
                            break
                        writer.writeframesraw(data)
        if not validate_pcm(tmp):
            raise RuntimeError("Assembled WAV failed validation")
        tmp.replace(output)
    finally:
        if tmp.exists():
            tmp.unlink()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--text-file", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--reference")
    parser.add_argument("--resume", action="store_true", help="Reuse checkpoints only for an explicitly resumed job")
    parser.add_argument("--exaggeration", type=float, default=0.5)
    parser.add_argument("--cfg-weight", type=float, default=0.5)
    args = parser.parse_args()
    if not Path(args.model).is_dir():
        raise FileNotFoundError(args.model)
    text = Path(args.text_file).read_text(encoding="utf-8-sig")
    chunks = split_text(text)
    if not chunks:
        raise ValueError("Memoir text is empty")
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    # Resume identity includes voice recording content, not just its path.
    ref = Path(args.reference) if args.reference else None
    if ref and not ref.is_file():
        raise FileNotFoundError(ref)
    digest = hashlib.sha256()
    digest.update(json.dumps([text, str(Path(args.model).resolve()), args.exaggeration, args.cfg_weight], ensure_ascii=False).encode())
    if ref:
        with ref.open("rb") as stream:
            while True:
                data = stream.read(1024 * 1024)
                if not data: break
                digest.update(data)
    job_id = digest.hexdigest()[:24]
    # Fresh generation must never read or write reusable checkpoints.
    # Only an explicitly resumed job may inspect the persistent checkpoint folder.
    temporary = None
    if args.resume:
        cache = output.parent / "MemoirCheckpoints" / job_id
        cache.mkdir(parents=True, exist_ok=True)
        print("RESUME MODE: checking saved sections in " + str(cache), flush=True)
    else:
        temporary = tempfile.TemporaryDirectory(prefix="pmr_fresh_")
        cache = Path(temporary.name)
        print("FRESH GENERATION: temporary sections only; no checkpoint reuse", flush=True)
    (cache / "manifest.json").write_text(json.dumps({"chunks": chunks, "reference": str(ref) if ref else None, "job": job_id}, ensure_ascii=False, indent=2), encoding="utf-8")
    paths = [cache / f"part_{i:05d}.wav" for i in range(1, len(chunks) + 1)]
    pending = [i for i, path in enumerate(paths) if not validate_pcm(path)] if args.resume else list(range(len(chunks)))
    print(f"Memoir mode: {'RESUME' if args.resume else 'FRESH GENERATION'}; {len(chunks)} sections; {len(chunks)-len(pending)} reused; {len(pending)} to synthesize", flush=True)
    if pending:
        import torch
        import torchaudio
        from chatterbox.tts import ChatterboxTTS
        torch.set_num_threads(max(1, min(8, os.cpu_count() or 4)))
        print("Loading model locally", flush=True)
        model = ChatterboxTTS.from_local(args.model, device="cpu")
        kwargs = {"audio_prompt_path": str(ref)} if ref else {}
        for i in pending:
            print(f"SECTION {i+1}/{len(chunks)}: generating ({len(chunks[i])} characters)", flush=True)
            audio = model.generate(chunks[i], exaggeration=args.exaggeration, cfg_weight=args.cfg_weight, **kwargs)
            temp = paths[i].with_suffix(".tmp.wav")
            # Write ordinary PCM16 WAV. TorchAudio may otherwise create IEEE-float
            # WAVs, which Python's wave reader cannot assemble reliably.
            import numpy as np
            import soundfile as sf
            samples = audio.detach().cpu().float().numpy().reshape(-1)
            if samples.size < model.sr // 5 or not np.isfinite(samples).all():
                raise RuntimeError(f"Section {i+1} returned empty/nonfinite audio")
            sf.write(str(temp), np.clip(samples, -1.0, 1.0), model.sr, subtype="PCM_16")
            if not validate_pcm(temp):
                raise RuntimeError(f"Section {i+1} produced an invalid WAV")
            temp.replace(paths[i])
            print(f"SECTION {i+1}/{len(chunks)}: saved", flush=True)
    print("Joining completed sections into WAV", flush=True)
    merge_wavs(paths, output)
    print("Saved:", output, flush=True)
    if temporary is not None:
        temporary.cleanup()

if __name__ == "__main__":
    main()
