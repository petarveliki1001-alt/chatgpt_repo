import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[1] / 'PersonalMemoirsReader' / 'f5_setup_worker.py'
spec = importlib.util.spec_from_file_location('f5_setup_worker', SCRIPT)
setup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(setup)


class SetupTests(unittest.TestCase):
    def run_setup(self, marker, failure=None):
        infer = types.ModuleType('f5_tts.infer')
        infer.utils_infer = types.SimpleNamespace(asr_pipe=object())
        with patch.dict(sys.modules, {'f5_tts.infer': infer}), \
                patch.object(sys, 'argv', [str(SCRIPT), str(marker)]), \
                patch.object(setup, 'load_models'), \
                patch.object(setup.subprocess, 'run', side_effect=failure) as run:
            setup.main()
            self.assertIsNone(infer.utils_infer.asr_pipe)
            return run.call_args

    def test_ready_marker_requires_fresh_offline_verification(self):
        with tempfile.TemporaryDirectory() as d:
            marker = Path(d) / 'ready.txt'
            args, kwargs = self.run_setup(marker)
            self.assertEqual(args[0][-1], '--verify-offline')
            self.assertEqual(kwargs['env']['HF_HUB_OFFLINE'], '1')
            self.assertEqual(kwargs['env']['TRANSFORMERS_OFFLINE'], '1')
            self.assertTrue(kwargs['check'])
            self.assertIn('automatic reference transcription', marker.read_text())

    def test_failed_verification_removes_old_readiness(self):
        with tempfile.TemporaryDirectory() as d:
            marker = Path(d) / 'ready.txt'
            marker.write_text('old marker')
            with self.assertRaises(subprocess.CalledProcessError):
                self.run_setup(marker, subprocess.CalledProcessError(1, 'offline check'))
            self.assertFalse(marker.exists())

    def test_offline_child_loads_models_without_spawning_more_children(self):
        with patch.object(sys, 'argv', [str(SCRIPT), '--verify-offline']), \
                patch.object(setup, 'load_models') as load, \
                patch.object(setup.subprocess, 'run') as run:
            setup.main()
            load.assert_called_once()
            run.assert_not_called()
