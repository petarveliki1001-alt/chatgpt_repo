"""Control reference: publisher's CPU loop, with the native probe's identical captured inputs/noise."""
import json,time
from pathlib import Path
import numpy as np
import soundfile as sf
import onnxruntime as ort
root=Path('/workspace/scratch/native-verification/models/f5')
fixture=Path('/workspace/scratch/native-verification/results/native-f5-auto.wav')
info=json.loads(Path(str(fixture)+'.input.json').read_text())
options=ort.SessionOptions();options.intra_op_num_threads=4;options.inter_op_num_threads=1
start=time.perf_counter()
a=ort.InferenceSession(str(root/'F5_Preprocess.onnx'),options)
b=ort.InferenceSession(str(root/'F5_Transformer.onnx'),options)
c=ort.InferenceSession(str(root/'F5_Decode.onnx'),options)
audio=np.fromfile(str(fixture)+'.input.pcm16',dtype=np.int16).reshape(1,1,-1)
ids=np.fromfile(str(fixture)+'.input.ids.i32',dtype=np.int32).reshape(1,-1)
outputs=a.run(None,{'audio':audio,'text_ids':ids,'max_duration':np.array([info['duration']],np.int64)})
prepared=dict(zip([x.name for x in a.get_outputs()],outputs))
noise=np.fromfile(str(fixture)+'.input.noise.f32',np.float32).reshape(info['noiseShape'])
time_step=np.array([0],np.int32)
for i in range(31):
 feed={x.name:(noise if x.name=='noise' else time_step if x.name=='time_step.1' else prepared[x.name]) for x in b.get_inputs()}
 noise,time_step=b.run(None,feed)
 if (i+1)%8==0: print('STEP',i+1,'SECONDS',time.perf_counter()-start,flush=True)
wave=c.run(None,{'denoised':noise,'ref_signal_len':prepared['ref_signal_len']})[0].reshape(-1)
output=fixture.parent/'python-f5-onnx-controlled.wav'
sf.write(output,wave,24000,subtype='PCM_16')
reference,_=sf.read(fixture,dtype='int16')
report={'comparison':'same F5 ONNX graphs, same captured inputs/noise, .NET vs Python bindings',
 'samples':len(wave),'same_length':len(wave)==len(reference),'exact_pcm_match':np.array_equal(wave,reference),
 'max_pcm_difference':int(np.max(np.abs(wave.astype(np.int32)-reference.astype(np.int32)))) if len(wave)==len(reference) else None,
 'seconds':time.perf_counter()-start}
Path(str(output)+'.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2),flush=True)
