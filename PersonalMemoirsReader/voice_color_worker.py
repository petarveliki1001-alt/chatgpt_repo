"""Experimental spectral-color transfer. Not neural voice cloning."""
import argparse
import numpy as np
import soundfile as sf
from scipy.signal import resample_poly, stft, istft
from scipy.ndimage import gaussian_filter1d
from math import gcd

def read_mono(path, rate=24000):
    y, sr = sf.read(path, dtype="float32", always_2d=True)
    y = y.mean(axis=1)
    if sr != rate:
        d = gcd(sr, rate)
        y = resample_poly(y, rate // d, sr // d)
    return y

def envelope(y, rate=24000):
    # 1024-sample Hann frames; smooth over time and frequency to avoid copying phonemes.
    _, _, z = stft(y, rate, nperseg=1024, noverlap=768, boundary='zeros')
    mag = np.abs(z)
    power = np.mean(mag ** 2, axis=1)
    # Smooth broad vocal coloration, not individual harmonic peaks.
    return gaussian_filter1d(np.log(np.maximum(power, 1e-9)), sigma=11)

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--reference',required=True)
    ap.add_argument('--source',required=True)
    ap.add_argument('--output',required=True)
    ap.add_argument('--strength',type=float,default=0.55)
    a=ap.parse_args()
    if not 0 <= a.strength <= 1: raise ValueError('strength must be between 0 and 1')
    ref=read_mono(a.reference); src=read_mono(a.source)
    if len(ref)<24000 or len(src)<24000: raise ValueError('Both WAV files must contain at least one second of audio.')
    diff=np.clip((envelope(ref)-envelope(src))/2, -1.2, 1.2)
    # Normalize to avoid unintended overall loudness increase.
    diff-=np.mean(diff)
    gain=np.exp(a.strength*diff).astype('float32')
    _,_,z=stft(src,24000,nperseg=1024,noverlap=768,boundary='zeros')
    _,out=istft(z*gain[:,None],24000,nperseg=1024,noverlap=768,input_onesided=True,boundary=True)
    out=out[:len(src)]
    peak=np.max(np.abs(out))
    if peak>0.98: out=out*(0.98/peak)
    sf.write(a.output,out,24000,subtype='PCM_16')
    print(f'Wrote {a.output}; reference={len(ref)/24000:.2f}s, source={len(src)/24000:.2f}s, strength={a.strength:.2f}',flush=True)
if __name__=='__main__': main()
