import numpy as np
import scipy.io.wavfile as wav
from scipy.signal import butter, lfilter
import csv
import os
import sys

def detect_fundamental_frequency_fft(signal, sample_rate):
    start_sample = min(int(sample_rate * 0.5), len(signal) // 4)
    end_sample = min(start_sample + int(sample_rate * 1.0), len(signal))
    analysis_block = signal[start_sample:end_sample]
    
    windowed_block = analysis_block * np.hanning(len(analysis_block))
    fft_data = np.abs(np.fft.rfft(windowed_block))
    fft_freqs = np.fft.rfftfreq(len(windowed_block), d=1.0/sample_rate)
    
    min_freq, max_freq = 50, 2000
    valid_indices = np.where((fft_freqs >= min_freq) & (fft_freqs <= max_freq))[0]
    
    if len(valid_indices) == 0:
        return 440
        
    bounded_fft = fft_data[valid_indices]
    bounded_freqs = fft_freqs[valid_indices]
    
    peak_index = np.argmax(bounded_fft)
    return int(round(bounded_freqs[peak_index]))

def butter_bandpass(lowcut, highcut, fs, order=5):
    nyq = 0.5 * fs
    low = lowcut / nyq
    high = highcut / nyq
    b, a = butter(order, [low, high], btype='band')
    return b, a

def butter_bandpass_filter(data, lowcut, highcut, fs, order=5):
    b, a = butter_bandpass(lowcut, highcut, fs, order=order)
    return lfilter(b, a, data)

def parse_recording_with_fft(wav_filename):
    script_dir = os.path.dirname(os.path.abspath(__file__))
    wav_path = os.path.join(script_dir, wav_filename)
    
    if not os.path.exists(wav_path):
        print(f"❌ Error: The file '{wav_filename}' was not found in {script_dir}")
        sys.exit(1)
        
    print(f"🎵 Loading Audio File: {wav_filename}")
    sample_rate, data = wav.read(wav_path)
    
    if len(data.shape) > 1:
        audio_signal = np.mean(data, axis=1)
    else:
        audio_signal = data
        
    if audio_signal.dtype == np.int16:
        audio_signal = audio_signal / 32768.0
    elif audio_signal.dtype == np.int32:
        audio_signal = audio_signal / 2147483648.0
        
    print("🔍 Performing FFT to isolate primary spectral peak...")
    fundamental_freq = detect_fundamental_frequency_fft(audio_signal, sample_rate)
    print(f"🎯 FFT Auto-Detected Fundamental Frequency (f0): {fundamental_freq} Hz")
    
    speed_of_sound = 343.0 
    
    lowcut = fundamental_freq * 0.85
    highcut = fundamental_freq * 1.15
    fundamental_signal = butter_bandpass_filter(audio_signal, lowcut, highcut, sample_rate, order=4)
    harmonics_signal = audio_signal - fundamental_signal
    
    window_size = int(sample_rate * 0.02) 
    step_size = int(sample_rate * 0.01)   
    
    # NEW: Strips the .wav extension and creates [original_name].csv
    base_name = os.path.splitext(wav_filename)[0]
    csv_name = f"{base_name}.csv"
    csv_path = os.path.join(script_dir, csv_name)
    
    with open(csv_path, "w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["Timestamp", "Radius", "Fundamental_RMS", "Harmonic_RMS", "Base_Frequency"])
        
        for i in range(0, len(audio_signal) - window_size, step_size):
            time = i / sample_rate
            radius = time * speed_of_sound
            
            window_fund = fundamental_signal[i : i + window_size]
            window_harm = harmonics_signal[i : i + window_size]
            
            rms_fundamental = np.sqrt(np.mean(window_fund**2))
            rms_harmonic = np.sqrt(np.mean(window_harm**2))
            
            writer.writerow([
                round(time, 4), 
                round(radius, 3), 
                round(rms_fundamental, 6), 
                round(rms_harmonic, 6), 
                fundamental_freq
            ])
            
    print(f"📊 Saved Data: {csv_name}")
    print(f"🚀 Success! Ready for automated pipeline discovery in Unity.")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("❌ Usage syntax error! Provide [filename.wav]")
        sys.exit(1)
    parse_recording_with_fft(sys.argv[1])
