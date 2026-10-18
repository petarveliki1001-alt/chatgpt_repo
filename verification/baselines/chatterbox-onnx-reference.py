import importlib.util
from pathlib import Path
import time, json
import onnxruntime as ort
from tokenizers import Tokenizer
import numpy as np
spec=importlib.util.spec_from_file_location('reference_chatterbox','/workspace/scratch/native-verification/chatterbox/chatterbox_onnx/__init__.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
root=Path('/workspace/scratch/native-verification/models/chatterbox')
start=time.perf_counter()
options=ort.SessionOptions();options.intra_op_num_threads=4;options.inter_op_num_threads=1
s=module.ChatterboxOnnx.__new__(module.ChatterboxOnnx)
s.tokenizer=Tokenizer.from_file(str(root/'tokenizer.json'))
s.repetition_processor=module.RepetitionPenaltyLogitsProcessor(1.2)
s.num_hidden_layers=30;s.num_key_value_heads=16;s.head_dim=64
s.speech_encoder_session=ort.InferenceSession(str(root/'onnx/speech_encoder.onnx'),options)
s.embed_tokens_session=ort.InferenceSession(str(root/'onnx/embed_tokens.onnx'),options)
s.llama_with_past_session=ort.InferenceSession(str(root/'onnx/language_model.onnx'),options)
decoder=ort.InferenceSession(str(root/'onnx/conditional_decoder.onnx'),options)
class RecordingDecoder:
 def run(self, names, inputs):
  Path('/workspace/scratch/native-verification/results/python-chatterbox-all-tokens.json').write_text(json.dumps(inputs['speech_tokens'].tolist()))
  return decoder.run(names,inputs)
s.cond_decoder_session=RecordingDecoder()
loaded=time.perf_counter()
s.synthesize('The small boat crossed the quiet lake.',
 target_voice_path='/workspace/scratch/f5-investigation/official_reference.wav',
 output_file_name='/workspace/scratch/native-verification/results/python-chatterbox-onnx.wav',
 apply_watermark=False)
print('LOAD_SECONDS',loaded-start,'INFERENCE_SECONDS',time.perf_counter()-loaded,flush=True)
