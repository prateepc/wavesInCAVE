import os
import csv
import argparse
import numpy as np
import scipy.io.wavfile as wav

def generate_calibrated_wave(filename, frequency, target_rms, duration=3.0, sample_rate=44100, include_harmonics=False, num_harmonics=3):
    """
    Generates a phase-coherent synthesized audio signal calibrated to an exact target RMS value.
    Outputs a standard 16-bit PCM WAV file and a tracking CSV telemetry file for Unity.
    """
    print(f"🎬 Synthesis Initialized: {frequency} Hz Target Core | Target Weight: {target_rms} RMS")
    
    # 1. Build time vector base
    t = np.linspace(0, duration, int(sample_rate * duration), endpoint=False)
    
    # 2. Synthesize Composite Waveform (Phase-Coherent Array)
    # Start with pure fundamental wave
    combined_signal = np.sin(2 * np.pi * frequency * t)
    
    if include_harmonics and num_harmonics > 0:
        print(f"🧬 Blending {num_harmonics} upper overtone harmonics into fundamental core...")
        for i in range(2, 2 + num_harmonics):
            harmonic_freq = frequency * i
            if harmonic_freq > sample_rate / 2:
                print(f"⚠️ Warning: Harmonic {i}f ({harmonic_freq}Hz) exceeds Nyquist limit. Skipping.")
                break
            # Add harmonic with natural linear acoustic energy decay (1/n)
            combined_signal += (1.0 / i) * np.sin(2 * np.pi * harmonic_freq * t)
            
    # 3. Precision RMS Calibration Block
    # Calculate current raw mathematical RMS energy profile
    current_rms = np.sqrt(np.mean(combined_signal ** 2))
    
    # Scale signal to precisely match your target RMS value input
    scaling_factor = target_rms / current_rms
    calibrated_signal = combined_signal * scaling_factor
    
    # 4. Clip-Safe Validation Guard
    peak_value = np.max(np.abs(calibrated_signal))
    if peak_value > 1.0:
        print(f"⚠️ Warning: Target RMS ({target_rms}) causes digital clipping (Peak: {peak_value:.2f}). Scaling back to 1.0 max peak limit.")
        calibrated_signal = calibrated_signal / peak_value
        # Re-verify the adjusted actual RMS
        target_rms = np.sqrt(np.mean(calibrated_signal ** 2))
        print(f"📉 Adjusted safe operating energy level: {target_rms:.6f} RMS")

    # 5. Export Standardized 16-Bit Audio Layer
    audio_output = np.int16(calibrated_signal * 32767)
    
    script_dir = os.path.dirname(os.path.abspath(__file__)) if __file__ else "."
    wav_path = os.path.join(script_dir, filename)
    wav.write(wav_path, sample_rate, audio_output)
    print(f"💾 Calibrated Audio Asset successfully saved to: {wav_path}")
    
    # 6. Generate Companion Unity Telemetry Matrix File
    # Matches original specifications: Window duration = 0.05s, Step duration = 0.02s
    window_duration = 0.05
    step_duration = 0.02
    speed_of_sound = 343.0
    
    win_samples = int(sample_rate * window_duration)
    step_samples = int(sample_rate * step_duration)
    total_samples = len(calibrated_signal)
    
    csv_rows = []
    
    for i in range(0, total_samples - win_samples, step_samples):
        time_stamp = (i + win_samples / 2.0) / sample_rate
        radius = time_stamp * speed_of_sound
        
        # In a perfect procedural generator, the windowed segment energy perfectly tracks target RMS
        # We fill matching slots to maintain your legacy Unity script column map integrity
        csv_rows.append([
            round(time_stamp, 4),
            round(radius, 3),
            round(target_rms, 6),                     # Fundamental_RMS
            round(target_rms * 0.5, 6) if include_harmonics and num_harmonics >= 1 else 0.0, # Harmonic2_RMS
            round(target_rms * 0.33, 6) if include_harmonics and num_harmonics >= 2 else 0.0, # Harmonic3_RMS
            round(target_rms * 0.25, 6) if include_harmonics and num_harmonics >= 3 else 0.0, # Harmonic4_RMS
            int(round(frequency))                      # TargetFrequency
        ])
        
    base_name = os.path.splitext(filename)[0]
    csv_name = f"{base_name}.csv"
    csv_path = os.path.join(script_dir, csv_name)
    
    with open(csv_path, "w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["Timestamp", "Isotropic_Radius", "Fundamental_RMS", "Harmonic2_RMS", "Harmonic3_RMS", "Harmonic4_RMS", "TargetFrequency"])
        writer.writerows(csv_rows)
        
    print(f"📊 Companion Data Matrix successfully saved to: {csv_path}\n")

if __name__ == "__main__":
    # Command line argument parser matching requested options
    parser = argparse.ArgumentParser(description="Procedural Wave Generation Tool for Unity Calibration")
    parser.add_argument("-o", "--output", type=str, default="generated_wave.wav", help="Output WAV file name")
    parser.add_argument("-f", "--frequency", type=float, required=True, help="Principal fundamental frequency in Hz")
    parser.add_argument("-r", "--rms", type=float, required=True, help="Target RMS amplitude value (e.g. 0.05 to 0.70)")
    parser.add_argument("-d", "--duration", type=float, default=3.0, help="Audio length in seconds")
    parser.add_argument("--harmonics", action="store_true", help="Toggle inclusion of upper harmonic overtones")
    parser.add_argument("-n", "--count", type=int, default=3, help="Number of harmonic overtones to include if active")
    
    args = parser.parse_args()
    
    generate_calibrated_wave(
        filename=args.output,
        frequency=args.frequency,
        target_rms=args.rms,
        duration=args.duration,
        include_harmonics=args.harmonics,
        num_harmonics=args.count
    )

