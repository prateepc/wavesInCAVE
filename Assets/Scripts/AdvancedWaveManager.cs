using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public class AdvancedWaveManager : MonoBehaviour
{
    [Header("UI Canvas Elements")]
    public Slider sliderFrequency;
    public Slider sliderRMS;
    public Slider sliderDuration; // Tracks time vector boundaries
    public Slider sliderCount;
    public Slider sliderPower;
    public Slider sliderStep;
    public Button generateButton;

    [Header("UI & Tooltip Bindings")]
    public GameObject tooltipPanel;
    public TMPro.TMP_Text tooltipText;

    [Header("Chamber Dimensions")]
    public Vector3 chamberMin;
    public Vector3 chamberMax;

    [Header("Real-time Wave Parameters (Internal Sync)")]
    public float fundamentalFrequency = 343.0f;
    public float targetRMS = 0.5f;
    public bool includeHarmonics = true;
    public int numHarmonics = 3;
    public float decayPower = 1.5f;
    [Range(1, 2)] public int stepMultiplier = 1; 

    [Header("Environmental Constants")]
    public float SPEED_OF_SOUND = 343.0f;

    // Simulation states linked up to visualization pipelines
    private float[] harmonicAmplitudes = new float[4];
    private List<string> activeTestHarmonicsLabels = new List<string>();
    private float calculatedRMS;
    private float peakPressure;
    private bool analysisComplete = false;

    private double audioPhasePosition = 0.0;
    private float samplingRate = 44100f;
    private AudioSource audioSource;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        samplingRate = AudioSettings.outputSampleRate;
    }

    void Start()
    {
        // Bind button dynamically at startup if not completely wired up in editor
        if (generateButton != null)
        {
            generateButton.onClick.AddListener(ReadSlidersAndRebuildSimulation);
        }

        // Run default initialization parameters
        ReadSlidersAndRebuildSimulation();
    }

    void Update()
    {
        HandleWaveInterrogation();
    }

    /// <summary>
    /// Reads slider configuration metrics directly out of UI layout objects 
    /// and pushes them down into the real-time procedural acoustic solver.
    /// </summary>
    public void ReadSlidersAndRebuildSimulation()
    {
        if (sliderFrequency != null) fundamentalFrequency = sliderFrequency.value;
        if (sliderRMS != null) targetRMS = sliderRMS.value;
        if (sliderCount != null) numHarmonics = Mathf.RoundToInt(sliderCount.value);
        if (sliderPower != null) decayPower = sliderPower.value;
        if (sliderStep != null) stepMultiplier = Mathf.RoundToInt(sliderStep.value);
        
        includeHarmonics = (numHarmonics > 0);

        ConfigureAndStartSimulation();
    }

    /// <summary>
    /// Recalculates mathematical vectors to switch audio synthesis profiles instantly.
    /// </summary>
    public void ConfigureAndStartSimulation()
    {
        analysisComplete = false;
        System.Array.Clear(harmonicAmplitudes, 0, harmonicAmplitudes.Length);
        activeTestHarmonicsLabels.Clear();

        // 1. Assign the fundamental base tier (1f)
        harmonicAmplitudes[0] = targetRMS; 
        activeTestHarmonicsLabels.Add($"1f (Fundamental): {fundamentalFrequency:F1} Hz");

        // 2. Map structural harmonic steps matching the integrated script configuration
        if (includeHarmonics && numHarmonics > 0)
        {
            for (int idx = 1; idx <= numHarmonics; idx++)
            {
                int multiplier = 1 + (stepMultiplier * idx);
                
                // Track spatial indexing matrix safety thresholds (Max 4 layers tracked: 1f to 4f)
                if (multiplier > 4) continue; 

                float harmonicFreq = fundamentalFrequency * multiplier;
                if (harmonicFreq > samplingRate / 2f) break; 

                // Core decay power calculation modeled directly from original framework layout
                harmonicAmplitudes[multiplier - 1] = targetRMS / Mathf.Pow(multiplier, decayPower);
                activeTestHarmonicsLabels.Add($"{multiplier}f (Harmonic Overtone): {harmonicFreq:F1} Hz");
            }
        }

        calculatedRMS = targetRMS;
        peakPressure = targetRMS * 1.414f; 

        if (!audioSource.isPlaying)
        {
            audioSource.playOnAwake = true;
            audioSource.loop = true;
            audioSource.Play();
        }

        analysisComplete = true;
    }

    /// <summary>
    /// Casts an extraction ray through the room space matrix to determine local phase vectors.
    /// </summary>
    void HandleWaveInterrogation()
    {
        if (tooltipPanel == null || tooltipText == null || !analysisComplete) return;

        Ray ray = new Ray(transform.position, transform.forward);
        Bounds roomVolume = new Bounds();
        roomVolume.SetMinMax(chamberMin, chamberMax);

        float intersectionDistance;
        if (roomVolume.IntersectRay(ray, out intersectionDistance))
        {
            Vector3 worldHitPoint = ray.GetPoint(intersectionDistance);
            float hitRadius = Vector3.Distance(Vector3.zero, worldHitPoint);
            float roomFadeMaxDistance = chamberMax.z - chamberMin.z;

            if (hitRadius <= roomFadeMaxDistance && hitRadius > 0.01f)
            {
                tooltipPanel.SetActive(true);
                
                RectTransform panelRect = tooltipPanel.GetComponent<RectTransform>();
                if (panelRect != null)
                {
                    panelRect.anchorMin = new Vector2(1f, 0f);
                    panelRect.anchorMax = new Vector2(1f, 0f);
                    panelRect.pivot = new Vector2(1f, 0f);
                    panelRect.anchoredPosition = new Vector2(-40f, 40f); 
                }

                float complexAcousticWave = 0f;
                float totalWeights = 0f;
                float fundamentalPhaseSign = 0f;

                // 0-indexed scanner loop prevents structural component index skew errors
                for (int i = 0; i < 4; i++)
                {
                    float amplitudeWeight = harmonicAmplitudes[i];
                    if (amplitudeWeight > 0f)
                    {
                        int harmonicMultiplier = i + 1;
                        float currentFrequency = fundamentalFrequency * harmonicMultiplier;
                        
                        float k = (2f * Mathf.PI * currentFrequency) / SPEED_OF_SOUND;
                        float layerPressure = (amplitudeWeight / hitRadius) * Mathf.Sin(k * hitRadius);

                        complexAcousticWave += layerPressure;
                        totalWeights += (amplitudeWeight / hitRadius); 

                        if (harmonicMultiplier == 1) fundamentalPhaseSign = layerPressure;
                    }
                }

                string zoneLabel = fundamentalPhaseSign > 0.02f ? "Fourier Compression Zone (High Pressure Crest)" :
                                   fundamentalPhaseSign < -0.02f ? "Fourier Rarefaction Zone (Low Pressure Trough)" : "Acoustic Equilibrium Node (Zero Pressure)";

                string harmonicsBlockText = "";
                foreach (string harmonicInfo in activeTestHarmonicsLabels)
                {
                    harmonicsBlockText += $"• {harmonicInfo}\n";
                }

                tooltipText.text = $"<b>{zoneLabel}</b>\n" +
                                   $"Analyzed Fundamental: {fundamentalFrequency:F1} Hz\n" +
                                   $"Global RMS Weight: {calculatedRMS:F5}\n" +
                                   $"Global Peak Pressure: {peakPressure:F5}\n\n" +
                                   $"<b>ACTIVE SIMULATION HARMONICS:</b>\n{harmonicsBlockText}";
                return;
            }
        }
        tooltipPanel.SetActive(false);
    }

    // =====================================================================
    // NATIVE PROCEDURAL AUDIO FILTER READ PIPE (Replaces Wav Import Process)
    // =====================================================================
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!analysisComplete) return;

        double samplesPerCycle = samplingRate;

        for (int i = 0; i < data.Length; i += channels)
        {
            float combinedSignal = 0f;

            // 1. Fundamental Component Wave layer (Sine orientation matching synthesis criteria)
            combinedSignal += Mathf.Sin((float)(2.0 * Mathf.PI * fundamentalFrequency * audioPhasePosition));

            // 2. Interleaved Upper Overtones (Cosine configuration with sign modification flip algorithms)
            if (includeHarmonics && numHarmonics > 0)
            {
                for (int idx = 1; idx <= numHarmonics; idx++)
                {
                    int multiplier = 1 + (stepMultiplier * idx);
                    float harmonicFreq = fundamentalFrequency * multiplier;

                    float signModifier = (idx % 2 == 0) ? 1f : -1f; 
                    float weight = 1f / Mathf.Pow(multiplier, decayPower);

                    combinedSignal += signModifier * weight * Mathf.Cos((float)(2.0 * Mathf.PI * harmonicFreq * audioPhasePosition));
                }
            }

            float finalSample = combinedSignal * (targetRMS * 1.414f);

            // Hard protection boundary filters
            if (finalSample > 1.0f) finalSample = 1.0f;
            if (finalSample < -1.0f) finalSample = -1.0f;

            for (int c = 0; c < channels; c++)
            {
                data[i + c] = finalSample;
            }

            audioPhasePosition += 1.0 / samplesPerCycle;
            if (audioPhasePosition >= 1.0)
            {
                audioPhasePosition -= 1.0; 
            }
        }
    }
}