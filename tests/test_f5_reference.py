import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import types

import numpy as np
import soundfile as sf

WORKER = Path(__file__).resolve().parents[1] / "PersonalMemoirsReader" / "f5_worker.py"
spec = importlib.util.spec_from_file_location("f5_worker", WORKER)
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class ReferenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "source.wav"
        self.output = self.root / "excerpt.wav"
        self.rate = 24000
        t = np.arange(15 * self.rate) / self.rate
        audio = (0.2 * np.sin(2 * np.pi * 220 * t)).astype("float32")
        sf.write(self.source, np.column_stack([audio, audio]), self.rate, subtype="PCM_16")

    def test_long_recording_is_not_silently_paired_with_guessed_transcript(self):
        with self.assertRaisesRegex(ValueError, "1–11 second"):
            worker.prepare_wav(self.source, self.output, "First sentence. Second sentence.")
        self.assertFalse(self.output.exists())

    def test_explicit_excerpt_keeps_exact_audio_and_full_supplied_transcript(self):
        transcript = "First sentence. Second sentence. Third sentence."
        result = worker.prepare_wav(self.source, self.output, transcript, 3.25, 8.75)
        actual, rate = sf.read(self.output)
        original, _ = sf.read(self.source)
        self.assertEqual(result, transcript)
        self.assertEqual(rate, self.rate)
        self.assertEqual(len(actual), int(5.5 * self.rate))
        np.testing.assert_allclose(actual, original[int(3.25*self.rate):int(8.75*self.rate), 0], atol=1/32768)

    def test_short_reference_uses_complete_recording(self):
        sf.write(self.source, np.ones(2 * self.rate) * .1, self.rate)
        self.assertEqual(worker.prepare_wav(self.source, self.output, "All of these words."), "All of these words.")
        self.assertEqual(sf.info(self.output).frames, 2 * self.rate)

    def test_invalid_ranges_are_rejected(self):
        for start, end in [(-1, 3), (3, 2), (0, 16), (0, .5), (float("nan"), 3), (0, float("inf"))]:
            with self.subTest(start=start, end=end), self.assertRaises(ValueError):
                worker.prepare_wav(self.source, self.output, "Words.", start, end)

    def test_silence_is_rejected(self):
        sf.write(self.source, np.zeros(2 * self.rate), self.rate)
        with self.assertRaisesRegex(ValueError, "silent"):
            worker.prepare_wav(self.source, self.output, "Words.")

    def test_empty_transcript_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "exact transcript"):
            worker.prepare_wav(self.source, self.output, " ", 0, 3)

    def test_preview_command_works_without_torch_or_models(self):
        transcript = self.root / "transcript.txt"
        transcript.write_text("Exactly these words.", encoding="utf-8")
        result = subprocess.run([sys.executable, str(WORKER), "--reference", str(self.source),
                                 "--reference-text", str(transcript), "--text-file", str(transcript),
                                 "--output", str(self.output), "--reference-start", "1.5",
                                 "--reference-end", "5.5", "--prepare-only"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(sf.info(self.output).frames, 4 * self.rate)
        self.assertIn("Exactly these words.", result.stdout)

    def test_generation_passes_the_auditioned_excerpt_and_transcript_to_f5(self):
        transcript = self.root / "transcript.txt"
        transcript.write_text("These are the exact reference words.", encoding="utf-8")
        target = self.root / "narration.txt"
        target.write_text("This is a different narration.", encoding="utf-8")
        calls = []

        class FakeF5:
            def __init__(self, **kwargs):
                pass

            def infer(self, **kwargs):
                info = sf.info(kwargs["ref_file"])
                calls.append((kwargs, info.frames))
                sf.write(kwargs["file_wave"], np.ones(24000) * .1, 24000)

        api = types.ModuleType("f5_tts.api")
        api.F5TTS = FakeF5
        argv = [str(WORKER), "--reference", str(self.source), "--reference-text", str(transcript),
                "--text-file", str(target), "--output", str(self.output),
                "--reference-start", "2", "--reference-end", "7"]
        with patch.dict(sys.modules, {"f5_tts.api": api}), patch.object(sys, "argv", argv), \
                patch.object(worker, "install_wav_compatibility"):
            worker.main()
        args, frames = calls[0]
        self.assertEqual(frames, 5 * self.rate)
        self.assertEqual(args["ref_text"], "These are the exact reference words.")
        self.assertEqual(args["gen_text"], "This is a different narration.")
        self.assertEqual(args["nfe_step"], 32)


if __name__ == "__main__":
    unittest.main()
