import os
import csv
import argparse
import numpy as np
import scipy.io.wavfile as wav
import matplotlib.pyplot as plt

def generate_calibrated_wave(filename, frequency, target_rms, duration=3.0, sample_rate=44100, include_harmonics=False, num_harmonics=3, decay_power=1.5, step_multiplier=2):
    """
    Generates a phase-coherent synthesized audio signal calibrated to an exact target RMS value.
    Enforces absolute mirror symmetry across crests and troughs using a cosine alignment configuration.
    """
    print(f"🎬 Synthesis Initialized: {frequency} Hz Target Core | Target Weight: {target_rms} RMS")
    print(f"📊 Harmonic Energy Decay Exponent: {decay_power}")
    
    # 1. Build time vector base
    t = np.linspace(0, duration, int(sample_rate * duration), endpoint=False)
    
    # 2. Synthesize Composite Waveform (Symmetric Phase-Coherent Array)
    combined_signal = np.sin(2 * np.pi * frequency * t)
    
    active_harmonics_list = [frequency]
    if include_harmonics and num_harmonics > 0:
        print(f"🧬 Blending {num_harmonics} upper overtone harmonics into fundamental core...")
        
        for idx in range(1, num_harmonics + 1):
            # Calculate the harmonic multiplier based on the step configuration
            # If step_multiplier = 2 (Default), it generates ODD numbers: 3f, 5f, 7f (Perfect Symmetry)
            # If step_multiplier = 1, it generates SEQUENTIAL numbers: 2f, 3f, 4f (Asymmetric tilting)
            i = 1 + step_multiplier * idx  
            harmonic_freq = frequency * i
            
            if harmonic_freq > sample_rate / 2:
                print(f"⚠️ Warning: Harmonic {i}f ({harmonic_freq}Hz) exceeds Nyquist limit. Skipping.")
                break
                
            # Fourier alternating sign modifier combined with Cosine phase alignment 
            # guarantees flawless mirrored symmetry across the zero-axis for odd series.
            sign_modifier = (-1) ** idx
            combined_signal += (sign_modifier / (i ** decay_power)) * np.cos(2 * np.pi * harmonic_freq * t)
            active_harmonics_list.append(harmonic_freq)
            
    # 3. Precision RMS Calibration Block
    current_rms = np.sqrt(np.mean(combined_signal ** 2))
    scaling_factor = target_rms / current_rms
    calibrated_signal = combined_signal * scaling_factor
    
    # 4. Clip-Safe Validation Guard
    peak_value = np.max(np.abs(calibrated_signal))
    if peak_value > 1.0:
        print(f"⚠️ Warning: Target RMS ({target_rms}) causes digital clipping (Peak: {peak_value:.2f}). Scaling back to 1.0 max peak limit.")
        calibrated_signal = calibrated_signal / peak_value
        target_rms = np.sqrt(np.mean(calibrated_signal ** 2))
        print(f"📉 Adjusted safe operating energy level: {target_rms:.6f} RMS")

    # 5. Export Standardized 16-Bit Audio Layer
    audio_output = np.int16(calibrated_signal * 32767)
    
    script_dir = os.path.dirname(os.path.abspath(__file__)) if __file__ else "."
    wav_path = os.path.join(script_dir, filename)
    wav.write(wav_path, sample_rate, audio_output)
    print(f"💾 Calibrated Audio Asset successfully saved to: {wav_path}")
    
    # 6. Generate Companion Unity Telemetry Matrix File
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
        csv_rows.append([
            round(time_stamp, 4),
            round(radius, 3),
            round(target_rms, 6),                     
            round(target_rms * 0.5, 6) if include_harmonics and num_harmonics >= 1 else 0.0, 
            round(target_rms * 0.33, 6) if include_harmonics and num_harmonics >= 2 else 0.0, 
            round(target_rms * 0.25, 6) if include_harmonics and num_harmonics >= 3 else 0.0, 
            int(round(frequency))                      
        ])
        
    base_name = os.path.splitext(filename)[0]
    csv_name = f"{base_name}.csv"
    csv_path = os.path.join(script_dir, csv_name)
    with open(csv_path, "w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["Timestamp", "Isotropic_Radius", "Fundamental_RMS", "Harmonic2_RMS", "Harmonic3_RMS", "Harmonic4_RMS", "TargetFrequency"])
        writer.writerows(csv_rows)
    print(f"📊 Companion Data Matrix successfully saved to: {csv_path}\n")

    # =====================================================================
    # FEATURE 1: MULTI-WINDOW PLOT FFT (6 MID-TIMELINE SNAPSHOTS)
    # =====================================================================
    print("📈 Plotting 6 mid-timeline FFT spectrum snapshots...")
    fig_fft, axes = plt.subplots(6, 1, figsize=(11, 14), sharex=True)
    time_boundaries = np.linspace(0, total_samples, 8, dtype=int)
    fft_window_size = 4096 
    
    for idx, segment_i in enumerate(range(1, 7)):
        center_sample = time_boundaries[segment_i]
        start_w = center_sample - (fft_window_size // 2)
        end_w = center_sample + (fft_window_size // 2)
        
        chunk = calibrated_signal[start_w:end_w]
        t_center = center_sample / sample_rate
        
        hanning_win = np.hanning(len(chunk))
        fft_vals = np.abs(np.fft.rfft(chunk * hanning_win))
        fft_freqs = np.fft.rfftfreq(len(chunk), d=1.0/sample_rate)
        
        ax = axes[idx]
        ax.plot(fft_freqs, fft_vals, color='darkcyan', linewidth=1.5, label=f"Window Snapshot at {t_center:.3f}s")
        
        colors_map = ['crimson', 'forestgreen', 'darkorange', 'darkorchid', 'teal']
        for h_i, h_freq in enumerate(active_harmonics_list):
            if h_freq <= sample_rate / 2:
                lbl = f"Layer {h_i+1}f ({int(h_freq)}Hz)" if idx == 0 else ""
                ax.axvline(h_freq, color=colors_map[h_i % len(colors_map)], linestyle='--', alpha=0.8, label=lbl)
                
        ax.set_xlim(0, max(frequency * (step_multiplier * num_harmonics + 3), 1500))
        ax.set_ylabel("Amplitude")
        ax.grid(True, alpha=0.3)
        ax.legend(loc="upper right")
        
    plt.xlabel("Frequency (Hz)")
    plt.suptitle(f"6-Window FFT Mapping Analysis\nBase Core: {frequency}Hz", fontsize=12, fontweight='bold')
    plt.tight_layout()

    # =====================================================================
    # FEATURE 2: TIME-DOMAIN WAVEFORM PLOT
    # =====================================================================
    print("📉 Plotting time-domain continuous wave layout...")
    plt.figure(figsize=(11, 4))
    
    one_cycle_duration = 1.0 / frequency
    zoom_duration = one_cycle_duration * 5.0
    zoom_samples_limit = int(sample_rate * zoom_duration)
    
    plt.plot(t[:zoom_samples_limit], calibrated_signal[:zoom_samples_limit], color='royalblue', linewidth=2, label="Complex Wave")
    plt.axhline(0, color='black', linestyle='-', alpha=0.3)
    
    plt.title(f"Continuous Signal Waveform over Time (First 5 Cycles Shown)\nTarget Pitch: {frequency}Hz | Decay Power: {decay_power}", fontsize=11, fontweight='bold')
    plt.xlabel("Time (Seconds)")
    plt.ylabel("Signal Amplitude Pressures")
    plt.grid(True, alpha=0.3)
    plt.legend(loc="upper right")
    plt.tight_layout()
    
    plt.show()

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Procedural Wave Generation Tool for Unity Calibration")
    parser.add_argument("-o", "--output", type=str, default="generated_wave.wav", help="Output WAV file name")
    parser.add_argument("-f", "--frequency", type=float, required=True, help="Principal fundamental frequency in Hz")
    parser.add_argument("-r", "--rms", type=float, required=True, help="Target RMS amplitude value (e.g. 0.05 to 0.70)")
    parser.add_argument("-d", "--duration", type=float, default=3.0, help="Audio length in seconds")
    parser.add_argument("--harmonics", action="store_true", help="Toggle inclusion of upper harmonic overtones")
    parser.add_argument("-n", "--count", type=int, default=3, help="Number of harmonic overtones to include if active")
    parser.add_argument("-p", "--power", type=float, default=1.5, help="Energy decay exponent power factor for overtones")
    parser.add_argument("-s", "--step", type=int, default=2, choices=[1, 2], help="Harmonic step index strategy (1 = Sequential 2f,3f; 2 = Symmetric Odd 3f,5f)")
    
    args = parser.parse_args()
    
    generate_calibrated_wave(
        filename=args.output,
        frequency=args.frequency,
        target_rms=args.rms,
        duration=args.duration,
        include_harmonics=args.harmonics,
        num_harmonics=args.count,
        decay_power=args.power,
        step_multiplier=args.step
    )

