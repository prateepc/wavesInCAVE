using System.IO;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

[RequireComponent(typeof(AudioSource))]
public class AdvancedWaveManager2 : MonoBehaviour
{
    [System.Serializable]
    public struct BipolarSpectrumPair
    {
        public Color lowPressureTrough;
        public Color zeroPressureEquilibrium;
        public Color highPressureCrest;
    }

    [Header("Audio File Target")]
    [Tooltip("The speaker audio source playing the loaded WAV recording.")]
    public AudioSource audioSource;
    [Tooltip("Increases or decreases wave size in response to the recording's volume.")]
    public float sensitivity = 20.0f; 

    [Header("UI Display Links (Outputs Only)")]
    public TextMeshProUGUI uiTextDisplay;

    [Header("Dynamic 5-Point Color Legend")]
    public UnityEngine.UI.Graphic singleColorBarGraphic;
    public TextMeshProUGUI textMaxPa;
    public TextMeshProUGUI textThreeQuartersPa;
    public TextMeshProUGUI textMidPa;
    public TextMeshProUGUI textQuarterPa;
    public TextMeshProUGUI textMinPa;

    [Header("Hover Tooltip UI Elements")]
    public GameObject tooltipPanel;
    public TextMeshProUGUI tooltipText;
    public Transform caveWandPointer;

    [Header("Wave Prefab Link")]
    public GameObject wavePrefab;

    [Header("Visual Tuning Layout")]
    public float maxGlowIntensity = 5.0f;
    [Range(0f, 1f)] public float waveOpacity = 0.60f;

    [Header("Acoustic Layer Color Matrices")]
    public BipolarSpectrumPair[] harmonicColorPalettes = new BipolarSpectrumPair[]
    {
        new BipolarSpectrumPair { lowPressureTrough = Color.blue, zeroPressureEquilibrium = Color.white, highPressureCrest = Color.red }
    };

    private List<GameObject> activeWaves = new List<GameObject>();
    
    // Core analytical variables driven by real-time audio
    private float calculatedRMS = 0f;
    private float peakPressure = 0f;
    private bool analysisComplete = false;

    private Vector3 chamberMin = new Vector3(-3.35f, 0.00f, -5.00f);
    private Vector3 chamberMax = new Vector3(3.35f, 6.70f, 5.00f);
    
    // Real-time audio buffer array (reads 256 physical sound samples)
    private float[] audioSamples = new float[256];

    void Start()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (uiTextDisplay == null)
        {
            GameObject foundUIObject = GameObject.Find("TelemetryDisplay");
            if (foundUIObject != null) uiTextDisplay = foundUIObject.GetComponent<TextMeshProUGUI>();
        }
        if (tooltipPanel != null) tooltipPanel.SetActive(false);

        // Pre-build our physical visual shells inside the CAVE room
        analysisComplete = true;
        GenerateStaticFourierSlices();
        GenerateDynamicLegendTexture();
    }

    void Update()
    {
        AnalyzeAudioSourceVolume();
        UpdateVisualShellsWithAudio();
        UpdateFivePointColorLegend();
        UpdateUIScreen();
        HandleWaveInterrogation();
    }

    /// <summary>
    /// Reads raw output amplitude directly from the AudioSource playing the WAV file.
    /// </summary>
    void AnalyzeAudioSourceVolume()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            // 1. Grab current sound wave sample values
            audioSource.GetOutputData(audioSamples, 0);

            // 2. Perform RMS (Root Mean Square) calculation to determine physical average amplitude
            float sumOfSquares = 0f;
            for (int i = 0; i < audioSamples.Length; i++)
            {
                sumOfSquares += audioSamples[i] * audioSamples[i];
            }
            
            calculatedRMS = Mathf.Sqrt(sumOfSquares / audioSamples.Length) * sensitivity;
            peakPressure = calculatedRMS * 1.414f; 
        }
        else
        {
            // If audio stops or is paused, decay back down to equilibrium
            calculatedRMS = Mathf.MoveTowards(calculatedRMS, 0f, Time.deltaTime * 2.0f);
            peakPressure = Mathf.MoveTowards(peakPressure, 0f, Time.deltaTime * 2.0f);
        }
    }

    /// <summary>
    /// Animates the visual shell colors, glow, and heights dynamically based on audio volume.
    /// </summary>
    void UpdateVisualShellsWithAudio()
    {
        float roomFadeMaxDistance = chamberMax.z - chamberMin.z;
        MaterialPropertyBlock propBlock = new MaterialPropertyBlock();

        for (int i = 0; i < activeWaves.Count; i++)
        {
            GameObject wave = activeWaves[i];
            if (wave == null) continue;

            float r = (wave.transform.localScale.x) / 2f; 
            float distanceDecay = Mathf.Clamp01(1.0f - (r / roomFadeMaxDistance));

            // Generate physical oscillations using real-time calculated RMS
            float waveValue = Mathf.Sin(Time.time * 5.0f - r) * calculatedRMS;
            float normalizedPressure = Mathf.Clamp(waveValue, -1f, 1f);

            Color dynamicColor = Color.white;
            BipolarSpectrumPair palette = harmonicColorPalettes[0];

            if (normalizedPressure >= 0f)
            {
                dynamicColor = Color.Lerp(palette.zeroPressureEquilibrium, palette.highPressureCrest, normalizedPressure);
            }
            else
            {
                dynamicColor = Color.Lerp(palette.zeroPressureEquilibrium, palette.lowPressureTrough, Mathf.Abs(normalizedPressure));
            }

            dynamicColor.a = waveOpacity * distanceDecay;

            // Update identifying fields for the hover system
            WaveDataIdentifier identifier = wave.GetComponent<WaveDataIdentifier>();
            if (identifier != null)
            {
                identifier.harmonicOrder = normalizedPressure > 0.05f ? "Fourier Compression Zone (High Pressure Crest)" :
                                           normalizedPressure < -0.05f ? "Fourier Rarefaction Zone (Low Pressure Trough)" : "Acoustic Equilibrium Node (Zero Pressure)";
            }

            // Apply calculated color and emissions properties dynamically to Renderer
            Renderer waveRenderer = wave.GetComponent<Renderer>();
            if (waveRenderer != null)
            {
                waveRenderer.GetPropertyBlock(propBlock);
                propBlock.SetColor("_Color", dynamicColor);
                propBlock.SetColor("_BaseColor", dynamicColor);
                
                Color emissionGlow = dynamicColor * maxGlowIntensity * distanceDecay;
                propBlock.SetColor("_EmissionColor", emissionGlow);
                
                waveRenderer.SetPropertyBlock(propBlock);
            }
        }
    }

    void GenerateStaticFourierSlices()
    {
        foreach (GameObject wave in activeWaves) { if (wave != null) Destroy(wave); }
        activeWaves.Clear();

        if (wavePrefab == null) return;

        float roomFadeMaxDistance = chamberMax.z - chamberMin.z;
        int totalSlices = 24; // Static visual slice count across the CAVE area
        float spatialStepDistance = roomFadeMaxDistance / totalSlices;
        float sourceOriginSafetyOffset = 0.05f;

        for (int i = 1; i <= totalSlices; i++)
        {
            float r = (i * spatialStepDistance) + sourceOriginSafetyOffset;
            Vector3 spawnPosition = transform.position;

            GameObject frozenWave = Instantiate(wavePrefab, spawnPosition, Quaternion.identity);
            frozenWave.name = $"Acoustic_Wave_Shell_{i}";
            frozenWave.transform.localScale = new Vector3(r * 2f, r * 2f, r * 2f);
            
            ConfigureShaderBoundaries(frozenWave);

            Collider c = frozenWave.GetComponent<Collider>();
            if (c != null) c.isTrigger = true;

            WaveDataIdentifier identifier = frozenWave.AddComponent<WaveDataIdentifier>();
            identifier.waveFrequency = 0; // Determined dynamically by the playing audio track later

            activeWaves.Add(frozenWave);
        }
    }

    private void UpdateFivePointColorLegend()
    {
        float halfPeak = peakPressure * 0.5f;

        if (textMaxPa != null) textMaxPa.text = $"+{peakPressure:F3} Pa";
        if (textThreeQuartersPa != null) textThreeQuartersPa.text = $"+{halfPeak:F3} Pa";
        if (textMidPa != null) textMidPa.text = "0.000 Pa";
        if (textQuarterPa != null) textQuarterPa.text = $"-{halfPeak:F3} Pa";
        if (textMinPa != null) textMinPa.text = $"-{peakPressure:F3} Pa";
    }

    private void GenerateDynamicLegendTexture()
    {
        if (singleColorBarGraphic == null) return;

        BipolarSpectrumPair activePalette = harmonicColorPalettes[0];
        int textureHeight = 256;
        Texture2D gradientTexture = new Texture2D(1, textureHeight, TextureFormat.RGBA32, false);
        gradientTexture.wrapMode = TextureWrapMode.Clamp;
        gradientTexture.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < textureHeight; y++)
        {
            float normalizedY = (float)y / (textureHeight - 1);
            Color pixelColor;

            if (normalizedY < 0.5f)
            {
                float t = normalizedY * 2f; 
                pixelColor = Color.Lerp(activePalette.lowPressureTrough, activePalette.zeroPressureEquilibrium, t);
            }
            else
            {
                float t = (normalizedY - 0.5f) * 2f; 
                pixelColor = Color.Lerp(activePalette.zeroPressureEquilibrium, activePalette.highPressureCrest, t);
            }

            gradientTexture.SetPixel(0, y, pixelColor);
        }

        gradientTexture.Apply();

        if (singleColorBarGraphic is UnityEngine.UI.Image uiImage)
        {
            uiImage.color = Color.white;
            uiImage.sprite = Sprite.Create(gradientTexture, new Rect(0, 0, 1, textureHeight), new Vector2(0.5f, 0.5f));
        }
        else if (singleColorBarGraphic is UnityEngine.UI.RawImage rawImage)
        {
            rawImage.color = Color.white;
            rawImage.texture = gradientTexture;
        }
    }

    void HandleWaveInterrogation()
    {
        if (tooltipPanel == null || tooltipText == null || !analysisComplete) return;

        Ray ray = (caveWandPointer != null) ? new Ray(caveWandPointer.position, caveWandPointer.forward) : Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 100f, Physics.AllLayers, QueryTriggerInteraction.Collide))
        {
            WaveDataIdentifier targetedWave = hit.collider.GetComponent<WaveDataIdentifier>();
            if (targetedWave != null)
            {
                tooltipPanel.SetActive(true);
                RectTransform panelRect = tooltipPanel.GetComponent<RectTransform>();
                if (panelRect != null)
                {
                    panelRect.anchorMin = Vector2.zero; panelRect.anchorMax = Vector2.zero; panelRect.pivot = Vector2.zero;
                }

                tooltipPanel.transform.position = (caveWandPointer != null) ? Camera.main.WorldToScreenPoint(hit.point) : Input.mousePosition + new Vector3(20f, 20f, 0f);
                float hitRadius = Vector3.Distance(transform.position, hit.point);

                tooltipText.text = $"<b>{targetedWave.harmonicOrder}</b>\n" +
                                   $"Current Selected File: {(audioSource != null && audioSource.clip != null ? audioSource.clip.name : "None")}\n" +
                                   $"Real-time RMS Level: {calculatedRMS:F5}\n" +
                                   $"Calculated Peak Pressure: {peakPressure:F5}\n" +
                                   $"Distance From Source: {hitRadius:F2} m";
                return;
            }
        }
        tooltipPanel.SetActive(false);
    }

    private void ConfigureShaderBoundaries(GameObject waveTarget)
    {
        Renderer r = waveTarget.GetComponent<Renderer>();
        if (r != null)
        {
            MaterialPropertyBlock propBlock = new MaterialPropertyBlock();
            r.GetPropertyBlock(propBlock);
            propBlock.SetVector("_ChamberMin", new Vector4(chamberMin.x, chamberMin.y, chamberMin.z, 0f));
            propBlock.SetVector("_ChamberMax", new Vector4(chamberMax.x, chamberMax.y, chamberMax.z, 0f));
            r.SetPropertyBlock(propBlock);
        }
    }

    void UpdateUIScreen()
    {
        if (uiTextDisplay == null) return;
        
        if (audioSource != null && audioSource.isPlaying && audioSource.clip != null)
        {
            uiTextDisplay.text = $"<b>ACTIVE RECORDING SIMULATION</b>\n" +
                                 $"File Name: {audioSource.clip.name}\n" +
                                 $"Sampler Frequency: {audioSource.clip.frequency} Hz\n" +
                                 $"RMS Amplitude: {calculatedRMS:F4} Pa";
        }
        else
        {
            uiTextDisplay.text = "<b>SYSTEM STANDBY</b>\nSelect a recording to begin wave analysis.";
        }
    }
}