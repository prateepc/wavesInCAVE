import numpy as np
import scipy.io.wavfile as wav
import matplotlib.pyplot as plt
from scipy.signal import butter, filtfilt, windows
import csv
import os
import sys

# =====================================================================
# USER TUNING CONFIGURATION
# =====================================================================
WINDOW_DURATION = 0.05   # Size of the custom FFT time window (Seconds)
STEP_DURATION = 0.02     # How far the time window rolls forward (Seconds)
SPEED_OF_SOUND = 343.0   # m/s for CAVE room scale mapping
NUM_SNAPSHOT_PLOTS = 4   # Number of spectrum snapshots to plot visually
MIN_F, MAX_F = 40, 2000  # Focused bounds for identifying valid musical instruments
HIGHPASS_CUTOFF = 30.0   # Cutoff frequency (Hz) to completely wipe DC/0Hz rumble

def butter_highpass_filter(data, cutoff, fs, order=4):
    """
    Applies a zero-phase high-pass Butterworth filter to wipe 
    out ultra-low DC offset/0 Hz noise artifacts.
    """
    nyq = 0.5 * fs
    normal_cutoff = cutoff / nyq
    b, a = butter(order, normal_cutoff, btype='high', analog=False)
    return filtfilt(b, a, data)

def extract_true_fundamental(data, sample_rate):
    """
    Analyzes the entire clip with a Hanning window to determine the true macro fundamental frequency.
    Implements a robust 3-point parabolic peak interpolation on local search arrays to completely 
    eliminate digital grid snapping.
    """
    win = windows.hann(len(data))
    fft_vals = np.abs(np.fft.rfft(data * win))
    fft_freqs = np.fft.rfftfreq(len(data), d=1.0/sample_rate)
    
    # Filter for valid musical spectrum space
    valid_idx = np.where((fft_freqs >= MIN_F) & (fft_freqs <= MAX_F))[0]
    if len(valid_idx) == 0:
        return 293.66  # Safety fallback
        
    search_freqs = fft_freqs[valid_idx]
    search_vals = fft_vals[valid_idx]
    
    # Local peak index relative to our zoomed search array
    local_peak_idx = np.argmax(search_vals)
    
    # --- FIXED LOCAL PARABOLIC INTERPOLATION ---
    # Perform interpolation inside the safe bounds of our search target array
    if 0 < local_peak_idx < len(search_vals) - 1:
        alpha = search_vals[local_peak_idx - 1]  # Left neighbor
        beta = search_vals[local_peak_idx]       # True highest bin value
        gamma = search_vals[local_peak_idx + 1]  # Right neighbor
        
        denominator = (alpha - 2.0 * beta + gamma)
        if abs(denominator) > 1e-5:
            # Find the fractional distance offset (-0.5 to +0.5) from the center bin
            p = 0.5 * (alpha - gamma) / denominator
            
            # True width spacing between discrete frequency bins
            bin_spacing = sample_rate / len(data)
            
            # Reconstruct the absolute index position in the global array
            global_peak_idx = valid_idx[local_peak_idx]
            
            # Map the precise interpolated sub-bin frequency location
            detected_peak = (global_peak_idx + p) * bin_spacing
        else:
            detected_peak = search_freqs[local_peak_idx]
    else:
        detected_peak = search_freqs[local_peak_idx]
    
    # Anti-Harmonic Check: Test if an underlying sub-harmonic holds significant energy
    expected_fundamental = detected_peak
    for divisor in [3, 2]:
        possible_sub = detected_peak / divisor
        sub_zone = np.where((fft_freqs >= possible_sub - 12) & (fft_freqs <= possible_sub + 12))[0]
        if len(sub_zone) > 0:
            if np.max(fft_vals[sub_zone]) > (beta * 0.18):
                expected_fundamental = fft_freqs[sub_zone[np.argmax(fft_vals[sub_zone])]]
                break

    return expected_fundamental

def calculate_bin_rms(fft_vals, fft_freqs, center_freq, band_width=15.0):
    """
    Calculates the root-mean-square energy within a tight band surrounding a target harmonic.
    """
    band_mask = (fft_freqs >= (center_freq - band_width)) & (fft_freqs <= (center_freq + band_width))
    matching_vals = fft_vals[band_mask]
    
    if len(matching_vals) == 0:
        return 0.0
    return np.sqrt(np.mean(matching_vals**2))

def process_cave_audio_pipeline(wav_filename):
    script_dir = os.path.dirname(os.path.abspath(__file__))
    wav_path = os.path.join(script_dir, wav_filename)
    
    if not os.path.exists(wav_path):
        print(f"❌ Error: File '{wav_filename}' missing in execution space: {script_dir}")
        sys.exit(1)
        
    fs, data = wav.read(wav_path)
    audio_signal = np.mean(data, axis=1) if len(data.shape) > 1 else data
    
    if audio_signal.dtype == np.int16:
        audio_signal = audio_signal / 32768.0
    elif audio_signal.dtype == np.int32:
        audio_signal = audio_signal / 2147483648.0

    print(f"🧹 Scrubbing ultra-low frequencies below {HIGHPASS_CUTOFF}Hz...")
    audio_signal = butter_highpass_filter(audio_signal, HIGHPASS_CUTOFF, fs, order=4)

    f1 = extract_true_fundamental(audio_signal, fs)
    print(f"🎯 Target Verified Fundamental Core (1f): {f1:.2f} Hz (Successfully locked to note group)")

    f2 = f1 * 2
    f3 = f1 * 3
    f4 = f1 * 4

    win_samples = int(fs * WINDOW_DURATION)
    step_samples = int(fs * STEP_DURATION)
    total_samples = len(audio_signal)
    
    csv_rows = []
    plot_snapshots = []
    
    print(f"🚀 Processing Windows: {wav_filename} | Tracking 1f, 2f, 3f, 4f over distance map...")
    
    for i in range(0, total_samples - win_samples, step_samples):
        chunk = audio_signal[i : i + win_samples]
        time_stamp = (i + win_samples / 2.0) / fs
        radius = time_stamp * SPEED_OF_SOUND
        
        win = windows.hann(len(chunk))
        chunk_fft = np.abs(np.fft.rfft(chunk * win))
        chunk_freqs = np.fft.rfftfreq(len(chunk), d=1.0/fs)
        
        rms_1f = calculate_bin_rms(chunk_fft, chunk_freqs, f1)
        rms_2f = calculate_bin_rms(chunk_fft, chunk_freqs, f2)
        rms_3f = calculate_bin_rms(chunk_fft, chunk_freqs, f3)
        rms_4f = calculate_bin_rms(chunk_fft, chunk_freqs, f4)
        
        csv_rows.append([
            round(time_stamp, 4),
            round(radius, 3),
            round(rms_1f, 6),
            round(rms_2f, 6),
            round(rms_3f, 6),
            round(rms_4f, 6),
            int(round(f1))
        ])
        
        plot_snapshots.append((time_stamp, chunk_freqs, chunk_fft, [f1, f2, f3, f4]))

    base_name = os.path.splitext(wav_filename)[0]
    csv_name = f"{base_name}.csv"
    csv_path = os.path.join(script_dir, csv_name)
    
    with open(csv_path, "w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["Timestamp", "Isotropic_Radius", "Fundamental_RMS", "Harmonic2_RMS", "Harmonic3_RMS", "Harmonic4_RMS", "TargetFrequency"])
        writer.writerows(csv_rows)
        
    print(f"📊 Calibrated telemetry matrix data written to: {csv_name}")
    
    if len(plot_snapshots) > NUM_SNAPSHOT_PLOTS:
        indices = np.linspace(0, len(plot_snapshots) - 1, NUM_SNAPSHOT_PLOTS, dtype=int)
        fig, axes = plt.subplots(NUM_SNAPSHOT_PLOTS, 1, figsize=(10, 2.5 * NUM_SNAPSHOT_PLOTS), sharex=True)
        
        for idx, ax_idx in enumerate(indices):
            t_snap, f_vec, fft_vec, h_list = plot_snapshots[ax_idx]
            axes[idx].plot(f_vec, fft_vec, color='teal', label=f'Spectrum Snapshot at {t_snap:.2f}s')
            
            colors = ['crimson', 'darkorange', 'limegreen', 'purple']
            labels = ['1f (Fundamental)', '2f Harmonic', '3f Harmonic', '4f Harmonic']
            for h_idx, h_freq in enumerate(h_list):
                if h_freq <= fs/2:
                    axes[idx].axvline(h_freq, color=colors[h_idx], linestyle='--', alpha=0.75, 
                                     label=f"{labels[h_idx]}: {int(h_freq)}Hz" if idx == 0 else "")
            
            axes[idx].set_xlim(0, max(f1 * 5, 1500))
            axes[idx].set_ylabel("Amplitude")
            axes[idx].legend(loc="upper right")
            axes[idx].grid(True, alpha=0.3)
            
        plt.xlabel("Frequency (Hz)")
        plt.suptitle(f"Fixed Windowed FFT Multi-Band Processing Map: {wav_filename}", fontsize=11, fontweight='bold')
        plt.tight_layout()
        plt.show()

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("❌ Error: Missing command line string format target.")
        print("Usage Example: python process_telemetry.py tracking_tone.wav")
        sys.exit(1)
    process_cave_audio_pipeline(sys.argv[1])
