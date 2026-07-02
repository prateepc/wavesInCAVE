import numpy as np
import scipy.io.wavfile as wav
import matplotlib.pyplot as plt
from scipy.signal import butter, filtfilt, find_peaks
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
MIN_F, MAX_F = 40, 4000  # Bounds for identifying valid physical waves
HIGHPASS_CUTOFF = 30.0   # Cutoff frequency (Hz) to completely wipe DC/0Hz rumble

def butter_highpass_filter(data, cutoff, fs, order=4):
    """
    Applies a zero-phase high-pass Butterworth filter to wipe 
    out ultra-low DC offset/0 Hz noise artifacts.
    """
    nyq = 0.5 * fs
    normal_cutoff = cutoff / nyq
    b, a = butter(order, normal_cutoff, btype='high', analog=False)
    return filtfilt(b, a, data) # filtfilt ensures zero phase distortion

def extract_harmonics_at_timestamp(signal_chunk, fs):
    """
    Performs windowed FFT and uses local peak detection to find the 
    FIRST spectral peak as f0, then extracts subsequent integer harmonics.
    """
    n = len(signal_chunk)
    windowed = signal_chunk * np.hanning(n)
    
    # Calculate Real FFT and normalize values
    fft_vals = np.abs(np.fft.rfft(windowed)) * (2.0 / n)
    freqs = np.fft.rfftfreq(n, d=1.0/fs)
    
    # Restrict tracking to valid user frequency bounds
    valid_idx = np.where((freqs >= MIN_F) & (freqs <= MAX_F))[0]
    if len(valid_idx) == 0:
        return 0, [0.0, 0.0, 0.0, 0.0], freqs, fft_vals

    bounded_fft = fft_vals[valid_idx]
    bounded_freqs = freqs[valid_idx]
    
    # Use peak detection to identify distinct local maxima
    # Prominence prevents tracking tiny grass-noise artifacts
    peaks, properties = find_peaks(bounded_fft, prominence=np.max(bounded_fft) * 0.05)
    
    if len(peaks) == 0:
        # Fallback to absolute max if peak detection finds nothing distinct
        f0_idx = np.argmax(bounded_fft)
    else:
        # STAGE 2 CHANGE: Always choose the absolute FIRST chronological peak as f0
        f0_idx = peaks[0]
        
    f0_actual = bounded_freqs[f0_idx]
    
    # Build list of strict integer harmonics based on the identified f0
    target_frequencies = [f0_actual, f0_actual * 2, f0_actual * 3, f0_actual * 4]
    pressures = []
    
    for target_f in target_frequencies:
        if target_f > fs / 2: # Stop if it goes past Nyquist limit
            pressures.append(0.0)
            continue
        # Find closest bin matching the integer step multiplier
        idx = np.argmin(np.abs(freqs - target_f))
        pressures.append(float(fft_vals[idx]))
        
    return int(round(f0_actual)), pressures, freqs, fft_vals

def process_cave_audio_pipeline(wav_filename):
    script_dir = os.path.dirname(os.path.abspath(__file__))
    wav_path = os.path.join(script_dir, wav_filename)
    
    if not os.path.exists(wav_path):
        print(f"❌ Error: File '{wav_filename}' missing in {script_dir}")
        sys.exit(1)
        
    fs, data = wav.read(wav_path)
    audio_signal = np.mean(data, axis=1) if len(data.shape) > 1 else data
    
    # Normalize inputs to clean engineering decimals
    if audio_signal.dtype == np.int16:
        audio_signal = audio_signal / 32768.0
    elif audio_signal.dtype == np.int32:
        audio_signal = audio_signal / 2147483648.0

    # STAGE 1 CHANGE: Scrub DC signal using high-pass filtering before processing FFTs
    print(f"🧹 Scrubbing ultra-low frequencies below {HIGHPASS_CUTOFF}Hz...")
    audio_signal = butter_highpass_filter(audio_signal, HIGHPASS_CUTOFF, fs, order=4)

    win_samples = int(fs * WINDOW_DURATION)
    step_samples = int(fs * STEP_DURATION)
    total_samples = len(audio_signal)
    
    csv_rows = []
    plot_snapshots = []
    
    print(f"🚀 Processing: {wav_filename} ({fs} Hz) | Window: {WINDOW_DURATION}s")
    
    for i in range(0, total_samples - win_samples, step_samples):
        chunk = audio_signal[i : i + win_samples]
        time_stamp = (i + win_samples / 2.0) / fs
        radius = time_stamp * SPEED_OF_SOUND
        
        f0, pressures, freqs, fft_vals = extract_harmonics_at_timestamp(chunk, fs)
        
        if f0 == 0: continue
        
        csv_rows.append([
            round(time_stamp, 4),  # Time Variable
            round(radius, 3),      # Isotropic Wave Front Radius
            f0,                    # Evolving First-Peak Fundamental (Hz)
            round(pressures[0], 6),# P0 Sound Pressure
            round(pressures[1], 6),# H1 Sound Pressure (2f0)
            round(pressures[2], 6),# H2 Sound Pressure (3f0)
            round(pressures[3], 6) # H3 Sound Pressure (4f0)
        ])
        
        plot_snapshots.append((time_stamp, freqs, fft_vals, f0, [f0, f0*2, f0*3, f0*4]))

    # Export CSV Dataset
    base_name = os.path.splitext(wav_filename)[0]
    csv_name = f"{base_name}.csv"
    csv_path = os.path.join(script_dir, csv_name)
    
    with open(csv_path, "w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["Timestamp", "Isotropic_Radius", "Principal_Frequency", "P0_Pressure", "H1_Pressure", "H2_Pressure", "H3_Pressure"])
        writer.writerows(csv_rows)
        
    print(f"📊 Clean data written to: {csv_name}")
    
    # Generate Multi-Snapshot Plots Matrix
    if len(plot_snapshots) > NUM_SNAPSHOT_PLOTS:
        indices = np.linspace(0, len(plot_snapshots) - 1, NUM_SNAPSHOT_PLOTS, dtype=int)
        fig, axes = plt.subplots(NUM_SNAPSHOT_PLOTS, 1, figsize=(10, 2.5 * NUM_SNAPSHOT_PLOTS), sharex=True)
        
        for idx, ax_idx in enumerate(indices):
            t_snap, f_vec, fft_vec, f0_val, h_list = plot_snapshots[ax_idx]
            axes[idx].plot(f_vec, fft_vec, color='steelblue', label=f'Spectrum at {t_snap:.2f}s')
            
            colors = ['crimson', 'darkorange', 'limegreen', 'purple']
            labels = ['f0 (First Peak)', 'H1 (2f0)', 'H2 (3f0)', 'H3 (4f0)']
            for h_idx, h_freq in enumerate(h_list):
                if h_freq <= fs/2:
                    axes[idx].axvline(h_freq, color=colors[h_idx], linestyle='--', alpha=0.75, 
                                      label=f"{labels[h_idx]}: {int(h_freq)}Hz" if idx == 0 else "")
            
            axes[idx].set_xlim(0, max(f0_val * 5, 1500))
            axes[idx].set_ylabel("Sound Pressure")
            axes[idx].legend(loc="upper right")
            axes[idx].grid(True, alpha=0.3)
            
        plt.xlabel("Frequency (Hz)")
        plt.suptitle(f"FFT Spectrum (DC Filtered & First Peak Tracked): {wav_filename}", fontsize=12, fontweight='bold')
        plt.tight_layout()
        plt.show()

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("❌ Usage: python script.py file.wav")
        sys.exit(1)
    process_cave_audio_pipeline(sys.argv[1])

