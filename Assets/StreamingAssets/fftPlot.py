import sys
import os
import tkinter as tk
from tkinter import filedialog, messagebox
import matplotlib.pyplot as plt
from matplotlib.backends.backend_tkagg import FigureCanvasTkAgg
from matplotlib.widgets import Slider
import numpy as np
import soundfile as sf
import sounddevice as sd

class AdvancedWavAnalyzerApp:
    def __init__(self, root, initial_file=None):
        self.root = root
        self.root.title("Advanced WAV Time Series & Dynamic FFT Analyzer")
        self.root.geometry("1100x850")

        # Audio Data Variables
        self.sample_rate = None
        self.data = None
        self.duration = 0.0
        self.is_playing = False
        self.slider_time = None  # Timeline navigation slider object

        # UI State Control Variables
        self.fft_window_var = tk.StringVar(value="0.1")
        self.window_func_var = tk.StringVar(value="Hann")
        self.db_scale_var = tk.BooleanVar(value=True)

        # Build UI layout
        self.create_widgets()
        self.setup_plots()

        # Handle window closure cleanup properly
        self.root.protocol("WM_DELETE_WINDOW", self.on_closing)

        # Handle command-line input cleanly as a string path
        if initial_file and len(initial_file) > 1:
            cmd_path = initial_file if isinstance(initial_file, list) else initial_file
            if os.path.exists(cmd_path):
                self.root.after(100, lambda: self.load_file_from_path(cmd_path))
    def create_widgets(self):
        """Creates an expanded control interface."""
        control_frame = tk.Frame(self.root)
        control_frame.pack(side=tk.TOP, fill=tk.X, padx=10, pady=5)

        # Row 1: File Loading, Playback, and Duration Metrics
        row1 = tk.Frame(control_frame)
        row1.pack(side=tk.TOP, fill=tk.X, pady=2)

        self.btn_load = tk.Button(row1, text="Load Audio File", command=self.browse_file)
        self.btn_load.pack(side=tk.LEFT, padx=5)

        self.lbl_file = tk.Label(row1, text="No file loaded", fg="gray")
        self.lbl_file.pack(side=tk.LEFT, padx=10)

        # Native Looping Play Button
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
        self.menu_window = tk.OptionMenu(row2, self.window_func_var, *windows, command=lambda _: self.update_plots())
        self.menu_window.pack(side=tk.LEFT, padx=5)

        self.chk_db = tk.Checkbutton(row2, text="Use dB Scale", variable=self.db_scale_var, command=self.update_plots)
        self.chk_db.pack(side=tk.LEFT, padx=15)

        tk.Label(row2, text="Max Freq Zoom (Hz):").pack(side=tk.LEFT, padx=(15, 5))
        self.slider_freq = tk.Scale(row2, from_=500, to=22050, orient=tk.HORIZONTAL, length=180, command=lambda _: self.update_plots())
        self.slider_freq.set(22050)
        self.slider_freq.pack(side=tk.LEFT, padx=5)

        self.btn_apply = tk.Button(row2, text="Apply Changes", command=self.update_plots, bg="lightblue", state=tk.DISABLED)
        self.btn_apply.pack(side=tk.RIGHT, padx=5)

    def setup_plots(self):
        """Initializes Time and FFT plots with room for a slider coordinate axis."""
        self.fig, (self.ax_time, self.ax_fft) = plt.subplots(2, 1, figsize=(8, 6))
        self.fig.tight_layout(pad=4.0)

        self.ax_time.set_title("Time Series View")
        self.ax_time.grid(True)
        self.ax_fft.set_title("Frequency Domain")
        self.ax_fft.grid(True)

        self.canvas = FigureCanvasTkAgg(self.fig, master=self.root)
        self.canvas.get_tk_widget().pack(side=tk.TOP, fill=tk.BOTH, expand=1)
    def browse_file(self):
        file_path = filedialog.askopenfilename(filetypes=[("Audio files", "*.wav;*.mp3")])
        if file_path:
            self.load_file_from_path(file_path)

    def load_file_from_path(self, file_path):
        """Parses audio files safely, resolving multi-channel numpy scalar exceptions."""
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
            
            # Unpack sample rate safely to prevent scalar array conversion crashes
            if isinstance(sr_raw, np.ndarray):
                self.sample_rate = int(np.squeeze(sr_raw).item())
            else:
                self.sample_rate = int(sr_raw)

            # Mix down to flat 1D mono channel early
            data_converted = raw_data.astype(np.float32)
            if len(data_converted.shape) > 1:
                data_converted = data_converted.mean(axis=1, dtype=np.float32)
            
            data_converted -= np.mean(data_converted)
            max_val = np.max(np.abs(data_converted))
            if max_val > 0:
                data_converted = data_converted / max_val
                
            self.data = data_converted
            self.duration = float(len(self.data) / self.sample_rate)

            # Boundaries validation slider configurations
            nyquist = int(self.sample_rate / 2)
            self.slider_freq.config(to=nyquist)
            self.slider_freq.set(nyquist if nyquist < 22050 else 22050)

            # RESTORED / ADDED: Clean, interactive Matplotlib navigation slider layout axis hook
            self.fig.subplots_adjust(bottom=0.15)  # Shifts subplots up to create layout space
            ax_slider = self.fig.add_axes([0.15, 0.05, 0.7, 0.03])
            
            self.slider_time = Slider(
                ax=ax_slider, label='FFT Position (s)', 
                valmin=0.0, valmax=self.duration, valinit=0.0, 
                valfmt='%1.2fs', color='royalblue'
            )
            # Link slider adjustments to trigger on-the-fly plot re-calculations
            self.slider_time.on_changed(lambda val: self.update_plots(target_time=val))

            # UI Component Synchronization
            self.lbl_file.config(text=os.path.basename(file_path), fg="black")
            self.btn_apply.config(state=tk.NORMAL)
            self.btn_play.config(state=tk.NORMAL)
            self.lbl_page.config(text=f"Total Duration: {self.duration:.2f}s")
            self.update_plots()

        except Exception as e:
            messagebox.showerror("Error", f"Could not decode or sanitize audio file:\n{str(e)}")

    def toggle_playback(self):
        if self.is_playing:
            self.stop_audio()
        else:
            if self.data is not None:
                self.is_playing = True
                self.btn_play.config(text="⏹ Stop Loop", bg="salmon")
                sd.play(self.data, samplerate=self.sample_rate, loop=True)

    def stop_audio(self):
        self.is_playing = False
        self.btn_play.config(text="▶ Play (Loop)", bg="lightgreen")
        sd.stop()
    def update_plots(self, target_time=0.0):
        """Draws the stable time series waveform profile and slices data dynamically using the slider position."""
        if self.data is None: 
            return

        time_vector = np.linspace(0.0, self.duration, num=len(self.data))

        # --- Redraw Time Plot ---
        self.ax_time.clear()
        self.ax_time.plot(time_vector, self.data, color='royalblue', lw=0.5)
        self.ax_time.set_xlim(0.0, self.duration + 0.5)
        self.ax_time.set_title("Full Audio Waveform Profile")
        self.ax_time.set_xlabel("Time (seconds)")
        self.ax_time.set_ylabel("Amplitude")
        self.ax_time.grid(True)

        # ADDED: Draw a static vertical green line on the time series indicating exactly where your FFT slice begins
        self.ax_time.axvline(x=target_time, color='darkgreen', linestyle='--', lw=1.5, label='FFT Slice Target')

        # --- Compute & Redraw Dynamic FFT Plot ---
        try:
            win_val = self.fft_window_var.get().strip()
            fft_win_len = float(win_val) if win_val else 0.1
            if fft_win_len <= 0: fft_win_len = 0.1
        except ValueError:
            fft_win_len = 0.1

        # UPDATED MATH: Calculate the data index frame dynamically relative to your slider position
        start_sample = int(target_time * self.sample_rate)
        end_sample = start_sample + int(fft_win_len * self.sample_rate)
        
        # Keep endpoints safe from array bounds overshoots
        if end_sample > len(self.data):
            end_sample = len(self.data)
            start_sample = max(0, end_sample - int(fft_win_len * self.sample_rate))

        fft_data_slice = self.data[start_sample:end_sample].astype(float)

        self.ax_fft.clear()
        if len(fft_data_slice) > 10:
            win_type = self.window_func_var.get()
            if win_type == "Hann" and len(fft_data_slice) > 1:
                fft_data_slice *= np.hanning(len(fft_data_slice))
            elif win_type == "Hamming" and len(fft_data_slice) > 1:
                fft_data_slice *= np.hamming(len(fft_data_slice))
            elif win_type == "Blackman" and len(fft_data_slice) > 1:
                fft_data_slice *= np.blackman(len(fft_data_slice))

            fft_vals = np.abs(np.fft.rfft(fft_data_slice))
            fft_freqs = np.fft.rfftfreq(len(fft_data_slice), d=1/self.sample_rate)

            if self.db_scale_var.get():
                fft_vals = 20 * np.log10(fft_vals + 1e-8)
                self.ax_fft.set_ylabel("Magnitude (dB)")
            else:
                self.ax_fft.set_ylabel("Magnitude (Linear)")

            self.ax_fft.plot(fft_freqs, fft_vals, color='crimson', lw=1.2)
            self.ax_fft.set_xlim(0, self.slider_freq.get())

        self.ax_fft.set_title(f"FFT Spectrum Window at {target_time:.2f}s ({fft_win_len}s | Mode: {win_type})")
        self.ax_fft.set_xlabel("Frequency (Hz)")
        self.ax_fft.grid(True)

        self.canvas.draw_idle()

    def on_closing(self):
        self.stop_audio()
        self.root.quit()
        self.root.destroy()

if __name__ == "__main__":
    root = tk.Tk()
    
    # CRITICAL STRING FIX: Extract the 1st argument index string cleanly, not the full list array
    cmd_file = sys.argv[1] if len(sys.argv) > 1 else None
    
    app = AdvancedWavAnalyzerApp(root, initial_file=cmd_file)
    root.mainloop()

