#!/bin/bash

# Exit immediately if any command fails
set -e

echo "======================================================="
echo " Starting Automated CAVE Audio Analyzer Setup "
echo "======================================================="

# 1. Verify python3 is installed on the host machine
if ! command -v python3 &> /dev/null; then
    echo "❌ Error: python3 could not be found on your system."
    echo "Please install Python 3.9+ and try again."
    exit 1
fi

# 2. Check for optional ffmpeg dependency (needed for MP3 loading safely)
if ! command -v ffmpeg &> /dev/null; then
    echo "⚠️  Warning: ffmpeg is not found. MP3 loading may fail."
    echo "WAV files will still process perfectly out of the box."
fi

# 3. Wipe clean any stale environment artifacts
if [ -d ".venv" ]; then
    echo "🧹 Removing existing virtual environment directory..."
    rm -rf .venv
fi

# 4. Construct a sandboxed Python virtual environment
echo "📦 Initializing clean Python virtual environment (.venv)..."
python3 -m venv .venv

# 5. Activate local virtual environment layers
echo "🔄 Activating the local environment sandbox..."
source .venv/bin/activate

# 6. Upgrade base internal environment managers safely
echo "⬆️  Upgrading pip package installer..."
pip install --upgrade pip

# 7. Core dependencies direct array assembly installation
echo "⚡ Installing explicit scientific computing and audio hardware libraries..."
pip install numpy matplotlib soundfile sounddevice

# 8. Success execution validation checkpoint
echo "======================================================="
echo " ✅ Installation Complete & Verified Successfully!"
echo "======================================================="
echo "To run your analyzer script, execute the following commands:"
echo "  source .venv/bin/activate"
echo "  python fftPlot.py"
echo "======================================================="

