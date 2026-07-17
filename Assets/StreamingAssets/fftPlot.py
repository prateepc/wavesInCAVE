import sys
import os
import time
import tkinter as tk
from tkinter import filedialog, messagebox
import matplotlib.pyplot as plt
from matplotlib.backends.backend_tkagg import FigureCanvasTkAgg
import numpy as np
import soundfile as sf
import sounddevice as sd

class AdvancedWavAnalyzerApp:
    def __init__(self, root, initial_file=None):
        self.root = root
        self.root.title("Real-Time Multi-Harmonic Peak & RMS Analyzer")
        self.root.geometry("1150x880")  # Expanded height slightly for metrics readability

        # Audio Data Variables
        self.sample_rate = None
        self.data = None
        self.duration = 0.0
        self.is_playing = False
        
        # Performance Tracking Handles
        self.playback_timer_id = None
        self.play_start_time = 0.0
        self.current_analysis_time = 0.0
        self.cached_win_len = 0.1
        self.cached_delta_t = 1.0
        self.last_pitch_update_time = 0.0

        # UI State Control Variables
        self.fft_window_var = tk.StringVar(value="0.1")
        self.window_func_var = tk.StringVar(value="Hann")
        self.db_scale_var = tk.BooleanVar(value=True)

        # Build UI layout
        self.create_widgets()
        self.setup_plots()

        self.root.protocol("WM_DELETE_WINDOW", self.on_closing)

        if initial_file:
            if isinstance(initial_file, list):
                target_path = initial_file[1] if len(initial_file) > 1 else None
            else:
                target_path = initial_file

            if isinstance(target_path, str) and os.path.exists(target_path):
                self.root.after(100, lambda: self.load_file_from_path(target_path))

    def create_widgets(self):
        """Creates an expanded control interface with advanced metrics blocks."""
        control_frame = tk.Frame(self.root)
        control_frame.pack(side=tk.TOP, fill=tk.X, padx=10, pady=5)

        # Row 1: File Loading, Playback Controls and Duration Metrics
        row1 = tk.Frame(control_frame)
        row1.pack(side=tk.TOP, fill=tk.X, pady=2)

        self.btn_load = tk.Button(row1, text="Load WAV/MP3 File", command=self.browse_file)
        self.btn_load.pack(side=tk.LEFT, padx=5)

        self.lbl_file = tk.Label(row1, text="No file loaded", fg="gray")
        self.lbl_file.pack(side=tk.LEFT, padx=10)

        self.btn_play = tk.Button(row1, text="▶ Play (Loop)", command=self.toggle_playback, bg="lightgreen", state=tk.DISABLED)
        self.btn_play.pack(side=tk.LEFT, padx=15)

        self.lbl_page = tk.Label(row1, text="Total Duration: 0.00s")
        self.lbl_page.pack(side=tk.RIGHT, padx=10)

        # Row 2: Live Analysis Parameters Controls
        row2 = tk.Frame(control_frame, bd=1, relief=tk.GROOVE, padx=5, pady=5)
        row2.pack(side=tk.TOP, fill=tk.X, pady=5)

        tk.Label(row2, text="FFT Window (X sec):").pack(side=tk.LEFT, padx=5)
        self.entry_fft = tk.Entry(row2, textvariable=self.fft_window_var, width=5)
        self.entry_fft.pack(side=tk.LEFT, padx=5)

        tk.Label(row2, text="Windowing:").pack(side=tk.LEFT, padx=(15, 5))
        windows = ["None (Rectangular)", "Hann", "Hamming", "Blackman"]
        self.menu_window = tk.OptionMenu(row2, self.window_func_var, *windows, command=lambda _: self.apply_ui_changes())
        self.menu_window.pack(side=tk.LEFT, padx=5)

        self.chk_db = tk.Checkbutton(row2, text="Use dB Scale", variable=self.db_scale_var, command=self.apply_ui_changes)
        self.chk_db.pack(side=tk.LEFT, padx=15)

        tk.Label(row2, text="Max Freq Zoom (Hz):").pack(side=tk.LEFT, padx=(15, 5))
        self.slider_freq = tk.Scale(row2, from_=500, to=22050, orient=tk.HORIZONTAL, length=180, command=lambda _: self.apply_ui_changes())
        self.slider_freq.set(22050)
        self.slider_freq.pack(side=tk.LEFT, padx=5)

        # Expanded Real-Time Feedback Multi-Line Metric Read-out Card Block
        self.lbl_pitch = tk.Label(row2, text="Live Dominant & Harmonic Peaks Profile Loading...", 
                                  font=("Courier", 10, "bold"), fg="darkgreen", justify=tk.LEFT, anchor="w")
        self.lbl_pitch.pack(side=tk.LEFT, padx=(25, 5), fill=tk.X, expand=True)

        self.btn_apply = tk.Button(row2, text="Apply Changes", command=self.apply_ui_changes, bg="lightblue", state=tk.DISABLED)
        self.btn_apply.pack(side=tk.RIGHT, padx=5)
    def setup_plots(self):
        """Initializes Time and FFT plots with padding settings."""
        self.fig, (self.ax_time, self.ax_fft) = plt.subplots(2, 1, figsize=(8, 6))
        self.fig.tight_layout(pad=4.0)

        self.ax_time.set_title("Time Series View")
        self.ax_time.grid(True)
        self.ax_fft.set_title("Frequency Domain (Live Delta T Step Spectrum)")
        self.ax_fft.grid(True)

        # Plot structural placeholders
        self.line_time, = self.ax_time.plot([], [], color='royalblue', lw=0.5)
        self.line_fft, = self.ax_fft.plot([], [], color='crimson', lw=1.2)
        
        self.live_time_marker = self.ax_time.axvline(x=0.0, color='darkgreen', linestyle='--', lw=2.0)

        self.canvas = FigureCanvasTkAgg(self.fig, master=self.root)
        self.canvas.get_tk_widget().pack(side=tk.TOP, fill=tk.BOTH, expand=1)

    def browse_file(self):
        file_path = filedialog.askopenfilename(filetypes=[("Audio files", "*.wav;*.mp3")])
        if file_path:
            self.load_file_from_path(file_path)

    def load_file_from_path(self, file_path):
        """Parses audio files safely, pre-converting compressed extensions to prevent system trace traps."""
        try:
            self.stop_audio()
            temp_fixed_path = os.path.expanduser("~/Documents/wavesInCAVE/Recordings/temp_normalized_playback.wav")
            
            if file_path.lower().endswith('.mp3'):
                import subprocess
                os.makedirs(os.path.dirname(temp_fixed_path), exist_ok=True)
                if os.path.exists(temp_fixed_path): os.remove(temp_fixed_path)
                cmd = ['ffmpeg', '-y', '-i', file_path, '-acodec', 'pcm_s16le', '-ar', '44100', '-ac', '1', temp_fixed_path]
                subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
                raw_data, sr_raw = sf.read(temp_fixed_path)
            else:
                try:
                    raw_data, sr_raw = sf.read(file_path)
                except Exception:
                    import subprocess
                    os.makedirs(os.path.dirname(temp_fixed_path), exist_ok=True)
                    if os.path.exists(temp_fixed_path): os.remove(temp_fixed_path)
                    cmd = ['ffmpeg', '-y', '-i', file_path, '-acodec', 'pcm_s16le', '-ar', '44100', '-ac', '1', temp_fixed_path]
                    subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
                    raw_data, sr_raw = sf.read(temp_fixed_path)
            
            if isinstance(sr_raw, np.ndarray):
                self.sample_rate = int(np.squeeze(sr_raw).item())
            else:
                self.sample_rate = int(sr_raw)

            data_converted = raw_data.astype(np.float32)
            if len(data_converted.shape) > 1:
                data_converted = data_converted.mean(axis=1, dtype=np.float32)
            
            data_converted -= np.mean(data_converted)
            max_val = np.max(np.abs(data_converted))
            if max_val > 0:
                data_converted = data_converted / max_val
                
            self.data = data_converted
            self.duration = float(len(self.data) / self.sample_rate)

            nyquist = int(self.sample_rate / 2)
            self.slider_freq.config(to=nyquist)
            self.slider_freq.set(nyquist if nyquist < 22050 else 22050)

            # Draw static waveform line elements once
            time_vector = np.linspace(0.0, self.duration, num=len(self.data))
            self.line_time.set_data(time_vector, self.data)
            self.ax_time.set_xlim(0.0, self.duration)
            self.ax_time.set_ylim(-1.1, 1.1)

            # Sync GUI Labels
            self.lbl_file.config(text=os.path.basename(file_path), fg="black")
            self.btn_apply.config(state=tk.NORMAL)
            self.btn_play.config(state=tk.NORMAL)
            self.lbl_page.config(text="Total Duration: " + str(round(self.duration, 2)) + "s")
            
            self.current_analysis_time = 0.0
            self.apply_ui_changes()

        except Exception as e:
            messagebox.showerror("Error", f"Could not decode or sanitize audio file:\n{str(e)}")
    def toggle_playback(self):
        if self.is_playing: self.stop_audio()
        else: self.start_audio()

    def start_audio(self):
        if self.data is None: return
        self.is_playing = True
        self.btn_play.config(text="⏹ Stop Loop", bg="salmon")
        
        self.play_start_time = time.time()
        self.current_analysis_time = 0.0
        
        sd.play(self.data, samplerate=self.sample_rate, loop=True, blocksize=4096)
        self.automated_playback_updater()

    def stop_audio(self):
        self.is_playing = False
        self.btn_play.config(text="▶ Play (Loop)", bg="lightgreen")
        sd.stop()
        
        if self.playback_timer_id is not None:
            self.root.after_cancel(self.playback_timer_id)
            self.playback_timer_id = None

    def apply_ui_changes(self):
        """Safely parses and caches interface inputs strictly outside of the performance loop."""
        try:
            win_val = self.fft_window_var.get().strip()
            self.cached_win_len = float(win_val) if win_val else 0.1
            if self.cached_win_len <= 0: self.cached_win_len = 0.1
        except ValueError:
            self.cached_win_len = 0.1

        self.cached_delta_t = 10 * self.cached_win_len
        
        if not self.is_playing:
            self.compute_fft_snapshot(target_time=0.0)

    def automated_playback_updater(self):
        """Monitors playback loops efficiently using pre-cached variables."""
        if not self.is_playing or self.data is None:
            return

        elapsed = time.time() - self.play_start_time
        current_file_time = elapsed % self.duration

        # Compute the FFT step snapshot only when crossing a Delta T milestone boundary
        if current_file_time >= self.current_analysis_time:
            self.compute_fft_snapshot(target_time=self.current_analysis_time)
            self.current_analysis_time += self.cached_delta_t
            
        if current_file_time < (self.current_analysis_time - self.cached_delta_t):
            self.current_analysis_time = 0.0

        self.playback_timer_id = self.root.after(20, self.automated_playback_updater)
    def compute_fft_snapshot(self, target_time):
        """Extracts the audio slice, calculates multi-harmonic peak-picked RMS spectra, and renders updates."""
        if self.data is None: return

        # Sync the green line vector marker coordinate instantly
        self.live_time_marker.set_xdata([target_time])

        start_sample = int(target_time * self.sample_rate)
        window_size_samples = int(self.cached_win_len * self.sample_rate)
        end_sample = start_sample + window_size_samples

        if end_sample > len(self.data):
            end_sample = len(self.data)
            start_sample = max(0, end_sample - window_size_samples)

        fft_data_slice = self.data[start_sample:end_sample].astype(float)
        win_type = self.window_func_var.get()

        if len(fft_data_slice) > 10:
            if win_type == "Hann" and len(fft_data_slice) > 1: slice_data = fft_data_slice * np.hanning(len(fft_data_slice))
            elif win_type == "Hamming" and len(fft_data_slice) > 1: slice_data = fft_data_slice * np.hamming(len(fft_data_slice))
            elif win_type == "Blackman" and len(fft_data_slice) > 1: slice_data = fft_data_slice * np.blackman(len(fft_data_slice))
            else: slice_data = fft_data_slice

            # --- PHYSICAL SCALE CONVERSION MATH ---
            # To get accurate RMS from real-valued FFT bins, we divide by the total bins and scale by sqrt(2)
            raw_fft = np.fft.rfft(slice_data)
            fft_mag_linear = np.abs(raw_fft) / len(slice_data)
            fft_mag_linear[1:] *= np.sqrt(2) # Account for single-sided folding scaling losses
            
            fft_freqs = np.fft.rfftfreq(len(slice_data), d=1/self.sample_rate)

            # --- ADVANCED LOGICAL MULTI-PEAK PICKER ---
            current_now = time.time()
            if (current_now - self.last_pitch_update_time) > 0.25:  # Throttle updates to 4 times per second max
                # Filter down to the structural acoustic musical search window
                valid_search_mask = (fft_freqs >= 40) & (fft_freqs <= 5000)
                search_indices = np.where(valid_search_mask)[0]

                peaks_list = []
                # Step through spectrum with an index radius buffer window to isolate true peaks
                neighbor_radius = max(2, int(len(fft_freqs) * 0.005))
                
                for idx in search_indices:
                    if idx <= neighbor_radius or idx >= len(fft_freqs) - neighbor_radius:
                        continue
                    current_amplitude = fft_mag_linear[idx]
                    
                    # Establish if this point is a true local peak relative to its neighbors
                    local_left_max = np.max(fft_mag_linear[idx - neighbor_radius:idx])
                    local_right_max = np.max(fft_mag_linear[idx + 1:idx + neighbor_radius + 1])
                    
                    if current_amplitude > local_left_max and current_amplitude > local_right_max:
                        peaks_list.append((current_amplitude, fft_freqs[idx]))

                # Sort distinct peak targets descending by amplitude magnitude
                peaks_list.sort(key=lambda item: item[0], reverse=True)

                # Construct a clean 4-line readout summary map
                report_lines = []
                for label_rank, title_prefix in enumerate(["Dominant", "Peak #2  ", "Peak #3  ", "Peak #4  "]):
                    if label_rank < len(peaks_list):
                        amp_rms, freq_hz = peaks_list[label_rank]
                        # Compute local peak attributes in dB format
                        amp_db = 20 * np.log10(amp_rms + 1e-8)
                        report_lines.append(f"{title_prefix}: {freq_hz:6.1f} Hz | RMS: {amp_rms:5.4f} ({amp_db:5.1f} dB)")
                    else:
                        report_lines.append(f"{title_prefix}: -- Hz | RMS: --")

                self.lbl_pitch.config(text="\n".join(report_lines))
                self.last_pitch_update_time = current_now

            # Map scaling flags onto our data display line vectors
            if self.db_scale_var.get():
                fft_display_vals = 20 * np.log10(fft_mag_linear + 1e-8)
                self.ax_fft.set_ylabel("RMS Magnitude (dB)")
                max_db = np.max(fft_display_vals)
                self.ax_fft.set_ylim(max_db - 65.0, max_db + 5.0)
            else:
                fft_display_vals = fft_mag_linear
                self.ax_fft.set_ylabel("RMS Magnitude (Linear)")
                max_lin = np.max(fft_display_vals)
                self.ax_fft.set_ylim(0.0, max_lin * 1.1 if max_lin > 0 else 1.0)

            # Update line plots
            self.line_fft.set_data(fft_freqs, fft_display_vals)
            self.ax_fft.set_xlim(0, self.slider_freq.get())

        self.ax_fft.set_title(f"FFT Window Snapshot at {target_time:.2f}s (Step Δt = {self.cached_delta_t:.2f}s | Mode: {win_type})")
        self.canvas.draw_idle()

    def on_closing(self):
        self.stop_audio()
        self.root.quit()
        self.root.destroy()

if __name__ == "__main__":
    root = tk.Tk()
    cmd_file = sys.argv[1] if len(sys.argv) > 1 else None
    app = AdvancedWavAnalyzerApp(root, initial_file=cmd_file)
    root.mainloop()

