import os
os.environ['HF_HOME']='/workspace/scratch/native-verification/python-cache/huggingface'
os.environ['XDG_CACHE_HOME']='/workspace/scratch/native-verification/python-cache'
os.environ['HF_HUB_DISABLE_TELEMETRY']='1'
import importlib.util
import time
import torch
from pathlib import Path
spec=importlib.util.spec_from_file_location('original_worker','/workspace/scratch/memoirs/PersonalMemoirsReader_v0.1.30/PersonalMemoirsReader/f5_worker.py')
worker=importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)
# Importing the original worker sets offline flags; acquisition is explicitly online for this test.
os.environ['HF_HUB_OFFLINE']='0';os.environ['TRANSFORMERS_OFFLINE']='0'
worker.install_wav_compatibility()
torch.set_num_threads(4)
from f5_tts.api import F5TTS
start=time.perf_counter()
model=F5TTS(device='cpu')
loaded=time.perf_counter()
model.infer(ref_file='/workspace/scratch/f5-investigation/official_reference.wav',
            ref_text='Some call me nature, others call me mother nature.',
            gen_text='The small boat crossed the quiet lake.',
            file_wave='/workspace/scratch/native-verification/python-f5.wav',
            nfe_step=32,cfg_strength=2.0,speed=1.0,seed=9527)
print('LOAD_SECONDS',loaded-start,'INFERENCE_SECONDS',time.perf_counter()-loaded,flush=True)
