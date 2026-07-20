using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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

    [Header("UI Canvas Configuration Elements")] 
    public Slider sliderFFTTimeWindow; // Time-duration window selection slider (0.1s - 2.0s)
    public Button generateButton;

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

    [Header("Real-Time Dual-Graphing System UI Links")]
    [Tooltip("The manual toggle control button link.")]
    public Button toggleGraphButton;
    [Tooltip("The parent object container containing our visual frequency spectrum chart nodes.")]
    public GameObject fftGraphPanel;
    [Tooltip("The parent object container containing our visual raw sound wave waveform nodes.")]
    public GameObject timeGraphPanel; 
    [Tooltip("Basic layout node prefab (e.g., UI Image or RawImage) used for generating visual bars.")]
    public GameObject graphBarPrefab;
    [Tooltip("Total vertical multiplier adjusting the display scale of both the FFT and raw wave graphs.")]
    public float graphBarHeightScale = 300.0f;

    [Header("Multi-Peak Axis Tracking UI Elements")]
    [Tooltip("The prefab used to spawn sliding frequency labels along the X-axis.")]
    public GameObject peakFreqLabelPrefab;
    [Tooltip("The prefab used to spawn sliding amplitude labels along the Y-axis.")]
    public GameObject peakAmpLabelPrefab;
    [Tooltip("Minimum threshold of energy required to count as an active peak.")]
    public float peakDetectionThreshold = 0.001f;
    [Tooltip("A general sensitivity modifier translating raw spectral magnitudes back into printable Pascal pressure layouts.")]
    public float graphSensitivityMultiplier = 20.0f;

    private List<GameObject> activeWaves = new List<GameObject>();
    private List<RectTransform> initializedFFTBars = new List<RectTransform>();
    private List<RectTransform> initializedTimeBars = new List<RectTransform>();
    private List<GameObject> activeLabelPool = new List<GameObject>();
    
    // Core analytical variables driven by real-time audio
    private float calculatedRMS = 0f;
    private float peakPressure = 0f;
    private float detectedDominantFrequency = 0f;
    private bool analysisComplete = false;

    private Vector3 chamberMin = new Vector3(-3.35f, 0.00f, -5.00f);
    private Vector3 chamberMax = new Vector3(3.35f, 6.70f, 5.00f);
    
    // Dynamic Audio Buffer Matrices
    private double samplingFrequency = 48000.0;
    private int currentFFTSize = 1024;
    private float[] spectrumDataArray;

    void Start()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        samplingFrequency = AudioSettings.outputSampleRate;
        if (samplingFrequency <= 0) samplingFrequency = 48000.0;

        if (uiTextDisplay == null)
        {
            GameObject foundUIObject = GameObject.Find("TelemetryDisplay");
            if (foundUIObject != null) uiTextDisplay = foundUIObject.GetComponent<TextMeshProUGUI>();
        }
        if (tooltipPanel != null) tooltipPanel.SetActive(false);

        // Map Button Handlers
        if (generateButton != null) generateButton.onClick.AddListener(ReadSlidersAndRebuildSimulation);
        if (toggleGraphButton != null) toggleGraphButton.onClick.AddListener(ToggleGraphVisibilityState);

        // Setup Time Window Duration Sliders
        if (sliderFFTTimeWindow != null)
        {
            sliderFFTTimeWindow.minValue = 0.10f;
            sliderFFTTimeWindow.maxValue = 2.00f;
            sliderFFTTimeWindow.wholeNumbers = false;
            sliderFFTTimeWindow.value = 0.10f;
            UpdateSliderLabel(sliderFFTTimeWindow);
        }
        UpdateFFTTimeWindow(sliderFFTTimeWindow != null ? sliderFFTTimeWindow.value : 0.10f);

        // Structural UI Generations
        InitializeDualGraphVisuals();
        GenerateStaticFourierSlices();
        GenerateDynamicLegendTexture();
        analysisComplete = true;
    }

    void Update()
    {
        if (!analysisComplete) return;

        samplingFrequency = AudioSettings.outputSampleRate;
        
        AnalyzeAudioSourceVolume();
        UpdateRealTimeFFTGraph();
        UpdateVisualShellsWithAudio();
        UpdateFivePointColorLegend();
        UpdateUIScreen();
        HandleWaveInterrogation();
    }

    public void UpdateFFTTimeWindow(float windowDurationSeconds)
    {
        if (samplingFrequency <= 0) samplingFrequency = AudioSettings.outputSampleRate;
        int rawSampleRequirement = Mathf.RoundToInt(windowDurationSeconds * (float)samplingFrequency);

        currentFFTSize = Mathf.ClosestPowerOfTwo(rawSampleRequirement);
        currentFFTSize = Mathf.Clamp(currentFFTSize, 64, 8192);

        spectrumDataArray = new float[currentFFTSize];
    }

    private void InitializeDualGraphVisuals()
    {
        if (graphBarPrefab == null) return;
        int totalVisualBars = 256;

        // FFT Frequency Bars Generation
        if (fftGraphPanel != null)
        {
            RectTransform fftRect = fftGraphPanel.GetComponent<RectTransform>();
            if (fftRect != null)
            {
                float barWidth = fftRect.rect.width / totalVisualBars;
                for (int i = 0; i < totalVisualBars; i++)
                {
                    GameObject barInstance = Instantiate(graphBarPrefab, fftGraphPanel.transform, false);
                    RectTransform barRect = barInstance.GetComponent<RectTransform>();
                    if (barRect != null)
                    {
                        barRect.anchorMin = new Vector2(0f, 0f);
                        barRect.anchorMax = new Vector2(0f, 0f);
                        barRect.pivot = new Vector2(0.5f, 0f);
                        barRect.anchoredPosition = new Vector2((i * barWidth) + (barWidth / 2f), 0f);
                        barRect.sizeDelta = new Vector2(barWidth * 0.85f, 0f);
                        initializedFFTBars.Add(barRect);
                    }
                }
            }
        }

        // Waveform Time-Domain Bars Generation
        if (timeGraphPanel != null)
        {
            RectTransform timeRect = timeGraphPanel.GetComponent<RectTransform>();
            if (timeRect != null)
            {
                float barWidth = timeRect.rect.width / totalVisualBars;
                for (int i = 0; i < totalVisualBars; i++)
                {
                    GameObject barInstance = Instantiate(graphBarPrefab, timeGraphPanel.transform, false);
                    RectTransform barRect = barInstance.GetComponent<RectTransform>();
                    if (barRect != null)
                    {
                        barRect.anchorMin = new Vector2(0f, 0.5f);
                        barRect.anchorMax = new Vector2(0f, 0.5f);
                        barRect.pivot = new Vector2(0.5f, 0.5f);
                        barRect.anchoredPosition = new Vector2((i * barWidth) + (barWidth / 2f), 0f);
                        barRect.sizeDelta = new Vector2(barWidth * 0.85f, 0f);
                        initializedTimeBars.Add(barRect);
                    }
                }
            }
        }
    }

    public void ToggleGraphVisibilityState()
    {
        if (fftGraphPanel != null) fftGraphPanel.SetActive(!fftGraphPanel.activeSelf);
        if (timeGraphPanel != null) timeGraphPanel.SetActive(!timeGraphPanel.activeSelf);
    }

    public void ReadSlidersAndRebuildSimulation()
    {
        if (sliderFFTTimeWindow != null) UpdateFFTTimeWindow(sliderFFTTimeWindow.value);
    }

    void AnalyzeAudioSourceVolume()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            float[] localOutputSamples = new float[256];
            audioSource.GetOutputData(localOutputSamples, 0);

            float sumOfSquares = 0f;
            for (int i = 0; i < localOutputSamples.Length; i++)
            {
                sumOfSquares += localOutputSamples[i] * localOutputSamples[i];
            }
            
            calculatedRMS = Mathf.Sqrt(sumOfSquares / localOutputSamples.Length) * sensitivity;
            peakPressure = calculatedRMS * 1.414f; 
        }
        else
        {
            calculatedRMS = Mathf.MoveTowards(calculatedRMS, 0f, Time.deltaTime * 2.0f);
            peakPressure = Mathf.MoveTowards(peakPressure, 0f, Time.deltaTime * 2.0f);
        }
    }

    private void UpdateRealTimeFFTGraph()
    {
        bool isFFTActive = fftGraphPanel != null && fftGraphPanel.activeSelf;
        bool isTimeActive = timeGraphPanel != null && timeGraphPanel.activeSelf;

        if ((!isFFTActive && !isTimeActive) || audioSource == null || spectrumDataArray == null) return;

        for (int i = activeLabelPool.Count - 1; i >= 0; i--)
        {
            if (activeLabelPool[i] != null) Destroy(activeLabelPool[i]);
        }
        activeLabelPool.Clear();

        // Waveform Time Graph Update Processing Loop
        if (isTimeActive && initializedTimeBars.Count > 0)
        {
            float[] timeDomainSamples = new float[initializedTimeBars.Count];
            audioSource.GetOutputData(timeDomainSamples, 0);

            for (int i = 0; i < initializedTimeBars.Count; i++)
            {
                if (initializedTimeBars[i] == null) continue;
                float sampleWaveAmplitude = timeDomainSamples[i] * (graphBarHeightScale * 0.5f);
                Vector2 size = initializedTimeBars[i].sizeDelta;
                size.y = Mathf.Lerp(size.y, Mathf.Max(Mathf.Abs(sampleWaveAmplitude), 2f), Time.deltaTime * 20f);
                initializedTimeBars[i].sizeDelta = size;
            }
        }

        // Spectral FFT Frequency Graph Update Processing Loop
        if (isFFTActive && initializedFFTBars.Count > 0)
        {
            float panelWidth = fftGraphPanel.GetComponent<RectTransform>().rect.width;
            int totalBars = initializedFFTBars.Count;

            audioSource.GetSpectrumData(spectrumDataArray, 0, FFTWindow.BlackmanHarris);

            float highestIntensitySeen = 0f;
            float halfSampleRate = (float)(samplingFrequency / 2.0);
            float maxViewableFrequency = 5000f;

            for (int i = 0; i < totalBars; i++)
            {
                if (initializedFFTBars[i] == null) continue;

                float targetFrequency = ((float)i / totalBars) * maxViewableFrequency;
                int spectrumIndex = Mathf.RoundToInt((targetFrequency / halfSampleRate) * currentFFTSize);
                spectrumIndex = Mathf.Clamp(spectrumIndex, 0, currentFFTSize - 1);

                float sampleIntensity = spectrumDataArray[spectrumIndex];
                float dynamicHeightValue = Mathf.Clamp(sampleIntensity * graphBarHeightScale * 3f, 2f, graphBarHeightScale);

                Vector2 alteredDimensions = initializedFFTBars[i].sizeDelta;
                alteredDimensions.y = Mathf.Lerp(alteredDimensions.y, dynamicHeightValue, Time.deltaTime * 14f);
                initializedFFTBars[i].sizeDelta = alteredDimensions;

                // Tracks peak real-time frequency analysis elements inside the asset layer
                if (sampleIntensity > highestIntensitySeen)
                {
                    highestIntensitySeen = sampleIntensity;
                    detectedDominantFrequency = spectrumIndex * halfSampleRate / currentFFTSize;
                }

                // Peak Axis Telemetry Generation Pass
                if (i > 0 && i < totalBars - 1 && peakFreqLabelPrefab != null && peakAmpLabelPrefab != null)
                {
                    float currentVisualHeight = alteredDimensions.y;
                    float prevHeight = initializedFFTBars[i - 1].sizeDelta.y;
                    float nextHeight = initializedFFTBars[i + 1].sizeDelta.y;

                    if (sampleIntensity > peakDetectionThreshold && currentVisualHeight > prevHeight && currentVisualHeight > nextHeight)
                    {
                        float horizontalPercentage = (float)i / (totalBars - 1);
                        float targetXCoordinate = (horizontalPercentage * panelWidth) - (panelWidth / 2f);
                        float peakBarLocalYHeight = alteredDimensions.y;

                        float peakFrequency = spectrumIndex * halfSampleRate / currentFFTSize;
                        float pressure = sampleIntensity * graphSensitivityMultiplier;

                        GameObject freqLabel = Instantiate(peakFreqLabelPrefab, fftGraphPanel.transform, false);
                        activeLabelPool.Add(freqLabel);
                        TextMeshProUGUI freqText = freqLabel.GetComponent<TextMeshProUGUI>();
                        if (freqText != null)
                        {
                            freqText.text = $"{peakFrequency:F0} Hz";
                            RectTransform freqRect = freqLabel.GetComponent<RectTransform>();
                            freqRect.localPosition = new Vector3(targetXCoordinate, freqRect.localPosition.y, 0f);
                        }

                        GameObject ampLabel = Instantiate(peakAmpLabelPrefab, fftGraphPanel.transform, false);
                        activeLabelPool.Add(ampLabel);
                        TextMeshProUGUI ampText = ampLabel.GetComponent<TextMeshProUGUI>();
                        if (ampText != null)
                        {
                            ampText.text = $"{pressure:F2} Pa";
                            RectTransform ampRect = ampLabel.GetComponent<RectTransform>();
                            ampRect.localPosition = new Vector3(targetXCoordinate, ampRect.localPosition.y, 0f);
                            ampRect.anchoredPosition = new Vector2(ampRect.anchoredPosition.x, peakBarLocalYHeight);
                        }
                    }
                }
            }
        }
    }

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

            WaveDataIdentifier identifier = wave.GetComponent<WaveDataIdentifier>();
            if (identifier != null)
            {
                identifier.waveFrequency = Mathf.RoundToInt(detectedDominantFrequency);
                identifier.harmonicOrder = normalizedPressure > 0.05f ? "Fourier Compression Zone (High Pressure Crest)" :
                                           normalizedPressure < -0.05f ? "Fourier Rarefaction Zone (Low Pressure Trough)" : "Acoustic Equilibrium Node (Zero Pressure)";
            }

            Renderer waveRenderer = wave.GetComponent<Renderer>();
            if (waveRenderer != null)
            {
                waveRenderer.GetPropertyBlock(propBlock);
                propBlock.SetColor("_Color", dynamicColor);
                propBlock.SetColor("_BaseColor", dynamicColor);
                propBlock.SetColor("_EmissionColor", dynamicColor * maxGlowIntensity * distanceDecay);
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
        int totalSlices = 24; 
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

            frozenWave.AddComponent<WaveDataIdentifier>();
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
            Color pixelColor = normalizedY < 0.5f ? 
                Color.Lerp(activePalette.lowPressureTrough, activePalette.zeroPressureEquilibrium, normalizedY * 2f) : 
                Color.Lerp(activePalette.zeroPressureEquilibrium, activePalette.highPressureCrest, (normalizedY - 0.5f) * 2f);

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

    private void UpdateSliderLabel(Slider targetSlider)
    {
        SliderTextBridge bridge = targetSlider.GetComponent<SliderTextBridge>();
        if (bridge != null) bridge.UpdateTextValue(targetSlider.value);
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
                tooltipPanel.transform.position = (caveWandPointer != null) ? Camera.main.WorldToScreenPoint(hit.point) : Input.mousePosition + new Vector3(20f, 20f, 0f);
                float hitRadius = Vector3.Distance(transform.position, hit.point);

                tooltipText.text = $"<b>{targetedWave.harmonicOrder}</b>\n" +
                                   $"Current Selected File: {(audioSource != null && audioSource.clip != null ? audioSource.clip.name : "None")}\n" +
                                   $"Track Dominant Peak: {targetedWave.waveFrequency} Hz\n" +
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
                                 $"Dominant Tone Peak: {detectedDominantFrequency:F0} Hz\n" +
                                 $"RMS Amplitude: {calculatedRMS:F4} Pa";
        }
        else
        {
            uiTextDisplay.text = "<b>SYSTEM STANDBY</b>\nSelect a recording to begin wave analysis.";
        }
    }
}