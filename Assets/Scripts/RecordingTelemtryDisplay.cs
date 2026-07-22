using UnityEngine;
using TMPro;
using System.Reflection;

public class SimulationTelemetryDisplay : MonoBehaviour
{
    [Header("UI Reference")]
    [Tooltip("Drag your TelemetryText object here.")]
    public TextMeshProUGUI telemetryText;

    [Header("Simulation Reference")]
    [Tooltip("Drag the GameObject with AdvancedWaveManager attached here.")]
    public AdvancedWaveManager waveManager;

    [Header("Optional Audio Source (For WAV Files)")]
    [Tooltip("Leave EMPTY if only using the wave synthesizer.")]
    public AudioSource optionalAudioSource;

    void Start()
    {
        // Auto-find wave manager in the scene if unassigned
        if (waveManager == null)
        {
            waveManager = FindFirstObjectByType<AdvancedWaveManager>();
        }

        // Auto-detect AudioSource if one exists on the WaveManager object
        if (optionalAudioSource == null && waveManager != null)
        {
            optionalAudioSource = waveManager.GetComponent<AudioSource>();
        }
    }

    void Update()
    {
        if (telemetryText == null) return;

        // MODE 1: Playing a WAV file clip
        if (optionalAudioSource != null && optionalAudioSource.clip != null)
        {
            AudioClip clip = optionalAudioSource.clip;
            bool isPlaying = optionalAudioSource.isPlaying;

            telemetryText.text = 
                $"<b><color=#00FFCC>--- SIMULATION TELEMETRY ---</color></b>\n" +
                $"<b>Mode:</b> Loaded Audio File\n" +
                $"<b>File:</b> {clip.name}\n" +
                $"<b>Status:</b> {(isPlaying ? "<color=#00FF00>PLAYING</color>" : "<color=#FFCC00>STOPPED</color>")}\n" +
                $"<b>Time:</b> {optionalAudioSource.time:F1}s / {clip.length:F1}s\n" +
                $"<b>Sample Rate:</b> {clip.frequency} Hz";
        }
        // MODE 2: Pure Synthesizer Mode (No AudioSource required)
        else if (waveManager != null)
        {
            float freq = GetFloatValue("principalFrequency", 343f);
            float rms = GetFloatValue("calculatedRMS", 0.5f);
            float peak = GetFloatValue("peakPressure", 0.707f);
            int harmonics = GetIntValue("numHarmonics", 0);

            telemetryText.text = 
                $"<b><color=#00FFCC>--- SYNTHESIZER TELEMETRY ---</color></b>\n" +
                $"<b>Mode:</b> Real-time Math Wave\n" +
                $"<b>Status:</b> <color=#00FF00>GENERATING AUDIO</color>\n" +
                $"<b>Frequency:</b> {freq:F1} Hz\n" +
                $"<b>RMS Pressure:</b> {rms:F3} Pa\n" +
                $"<b>Peak Pressure:</b> {peak:F3} Pa\n" +
                $"<b>Harmonics:</b> {harmonics}";
        }
        // MODE 3: Standby / Unlinked
        else
        {
            telemetryText.text = 
                $"<b><color=#FF5555>--- SIMULATION TELEMETRY ---</color></b>\n" +
                $"<b>Status:</b> Offline\n" +
                $"<i>Drag WaveManager into Inspector.</i>";
        }
    }

    // Safe reflection helpers to read analytical metrics from AdvancedWaveManager
    private float GetFloatValue(string fieldName, float fallback)
    {
        if (waveManager == null) return fallback;
        FieldInfo field = typeof(AdvancedWaveManager).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        return field != null ? System.Convert.ToSingle(field.GetValue(waveManager)) : fallback;
    }

    private int GetIntValue(string fieldName, int fallback)
    {
        if (waveManager == null) return fallback;
        FieldInfo field = typeof(AdvancedWaveManager).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        return field != null ? System.Convert.ToInt32(field.GetValue(waveManager)) : fallback;
    }
}