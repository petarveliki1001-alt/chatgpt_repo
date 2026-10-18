"""Acquire speech/ASR models, then verify loading in a fresh offline process."""
import os
from pathlib import Path
import subprocess
import sys


def load_models():
    from f5_tts.api import F5TTS
    from f5_tts.infer.utils_infer import initialize_asr_pipeline
    print("STAGE: Loading F5-TTS pretrained speech model", flush=True)
    model = F5TTS(device="cpu")
    del model
    print("STAGE: Loading automatic reference transcription model (Whisper large-v3-turbo)", flush=True)
    initialize_asr_pipeline(device="cpu")


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "--verify-offline":
        load_models()
        print("READY: Speech and transcription models load in a fresh offline process", flush=True)
        return
    marker = Path(sys.argv[1])
    marker.unlink(missing_ok=True)
    print("STAGE: Acquiring official speech and transcription weights (internet required)", flush=True)
    load_models()
    # Release the parent ASR model before starting the offline verification process.
    from f5_tts.infer import utils_infer
    utils_infer.asr_pipe = None
    import gc
    gc.collect()
    offline = dict(os.environ)
    offline.update(HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1", HF_HUB_DISABLE_TELEMETRY="1")
    print("STAGE: Verifying both models in a new offline process", flush=True)
    subprocess.run([sys.executable, "-u", str(Path(__file__).resolve()), "--verify-offline"],
                   env=offline, check=True)
    marker.parent.mkdir(parents=True, exist_ok=True)
    marker.write_text("F5-TTS and automatic reference transcription offline load verified\n", encoding="utf-8")
    print("READY: F5-TTS with automatic reference transcription is available offline", flush=True)


if __name__ == "__main__":
    main()
