import numpy as np
import scipy.io.wavfile as wav
import csv
import os
import sys

def orchestrate_frequency(frequency_hz, duration=3.0):
    # Automatically target the directory where this script sits
    output_dir = os.path.dirname(os.path.abspath(__file__))
    sample_rate = 44100
    speed_of_sound = 343.0 # Meters per second
    
    print(f"🎵 Processing Frequency: {frequency_hz} Hz...")
    
    # Task 4: Generate precise sine wave audio signal
    t = np.linspace(0, duration, int(sample_rate * duration), endpoint=False)
    audio_signal = np.sin(2 * np.pi * frequency_hz * t)
    
    # Save the uncompressed mono WAV audio file 
    wav_name = f"tone_{frequency_hz}Hz.wav"
    wav_path = os.path.join(output_dir, wav_name)
    wav.write(wav_path, sample_rate, (audio_signal * 32767).astype(np.int16))
    print(f"💾 Saved Audio: {wav_name}")
    
    # Extract time-series metrics into a CSV file
    window_size = int(sample_rate * 0.02) # 20ms analysis window
    step_size = int(sample_rate * 0.01)   # 10ms processing steps
    
    csv_name = f"telemetry_{frequency_hz}Hz.csv"
    csv_path = os.path.join(output_dir, csv_name)
    
    with open(csv_path, "w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["Timestamp", "Radius", "RMS", "Frequency"])
        
        for i in range(0, len(audio_signal) - window_size, step_size):
            time = i / sample_rate
            window = audio_signal[i : i + window_size]
            rms = np.sqrt(np.mean(window**2))
            radius = time * speed_of_sound
            writer.writerow([round(time, 4), round(radius, 3), round(rms, 6), frequency_hz])
            
    print(f"📊 Saved Data: {csv_name}\n🚀 Complete! Unity can now read this frequency.")

if __name__ == "__main__":
    # Default to 440Hz (A4) if no terminal argument is passed
    target_freq = 440
    if len(sys.argv) > 1:
        try:
            target_freq = int(sys.argv[1])
        except ValueError:
            print("❌ Please provide a valid integer for frequency.")
            sys.exit(1)
            
    orchestrate_frequency(target_freq)

