"""Original periodic suction-fan loop. No sampled third-party audio."""
from pathlib import Path
import wave
import numpy as np

sr = 22050
n = sr * 8
t = np.arange(n) / sr
rng = np.random.default_rng(257)
freq = np.fft.rfftfreq(n, 1 / sr)
# Circular spectral filtering produces periodic broadband airflow without a seam.
shaping = (freq / (freq + 100)) ** 2 / (1 + (freq / 1700) ** 4)
air = np.fft.irfft(np.fft.rfft(rng.normal(size=n)) * shaping, n=n)
air /= np.std(air)
tone = sum(a * np.sin(2*np.pi*f*t) for f, a in [(75,.33),(150,.19),(300,.09),(600,.035)])
signal = tone + .18 * air * (1 + .08*np.sin(2*np.pi*25*t))
signal *= .76 / np.max(np.abs(signal))
out = Path(__file__).resolve().parents[1] / 'Assets/DriveRoadster/Audio/Surface_Fan_Loop.wav'
with wave.open(str(out), 'wb') as stream:
    stream.setnchannels(1)
    stream.setsampwidth(2)
    stream.setframerate(sr)
    stream.writeframes((signal*32767).astype('<i2').tobytes())
print(out)
