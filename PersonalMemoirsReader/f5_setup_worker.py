"""Install-time model acquisition and offline load validation; called only by the GUI."""
import os
from pathlib import Path
import sys

def main():
    print("STAGE: Acquiring official F5-TTS pretrained weights (internet required)", flush=True)
    from f5_tts.api import F5TTS
    # The library obtains the model files using its official model repository.
    model = F5TTS(device="cpu")
    del model
    print("STAGE: Model acquired; validating fully offline load", flush=True)
    os.environ["HF_HUB_OFFLINE"] = "1"
    os.environ["TRANSFORMERS_OFFLINE"] = "1"
    os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"
    model = F5TTS(device="cpu")
    del model
    import soundfile as sf
    import numpy as np
    import tempfile
    with tempfile.TemporaryDirectory(prefix="pmr_f5_check_") as d:
        wav = Path(d) / "check.wav"
        sf.write(str(wav), np.zeros(2400, dtype="float32"), 24000, subtype="PCM_16")
        samples, rate = sf.read(str(wav), dtype="float32")
        if rate != 24000 or len(samples) != 2400:
            raise RuntimeError("Local WAV decoding verification failed")
    print("STAGE: Local WAV decoding verified without TorchCodec", flush=True)
    marker = Path(sys.argv[1]); marker.parent.mkdir(parents=True, exist_ok=True)
    marker.write_text("F5-TTS offline model load verified\n", encoding="utf-8")
    print("READY: F5-TTS loads locally without network access", flush=True)

if __name__ == "__main__":
    main()
