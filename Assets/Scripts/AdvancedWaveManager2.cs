using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(AudioSource))]
public class AdvancedWaveManager2 : MonoBehaviour
{
    public enum WindowType
    {
        Hamming,
        Hann,
        Blackman,
        Rectangular
    }

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
    public Slider sliderFFTTimeWindow; 
    public Slider sliderMaxFrequency;   
    public Button generateButton;

    [Header("Windowing & FFT Controls")]
    public WindowType selectedWindowType = WindowType.Hamming;
    [Tooltip("Dropdown UI element to select the active windowing function.")]
    public TMP_Dropdown windowTypeDropdown;
    [Tooltip("Size of the FFT window in samples (Power of 2).")]
    public int windowSizeSamples = 1024;

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
    public Button toggleGraphButton;
    public GameObject fftGraphPanel;
    public GameObject timeGraphPanel; 
    public GameObject graphBarPrefab;

    [Tooltip("Vertical line indicator overlay on the time graph showing the current playback timestamp.")]
    public RectTransform timePlayheadLine;

    public float graphBarHeightScale = 300.0f;
    public float maxFFTFrequency = 3000.0f;

    [Header("FFT dB Scale Settings")]
    public float minFFTdB = -150.0f;
    public float maxdBBuffer = 10.0f;

    [Header("Axis Labeling System UI Prefabs")]
    [Tooltip("Prefab containing a TextMeshProUGUI component used to instantiate axis tick labels.")]
    public GameObject axisLabelPrefab;

    private List<GameObject> activeWaves = new List<GameObject>();
    private List<RectTransform> initializedFFTBars = new List<RectTransform>();
    private List<RectTransform> initializedTimeBars = new List<RectTransform>();
    
    // Static Axis Label Management
    private List<TextMeshProUGUI> fftXAxisLabels = new List<TextMeshProUGUI>();
    private List<TextMeshProUGUI> timeXAxisLabels = new List<TextMeshProUGUI>();
    private List<TextMeshProUGUI> fftYAxisLabels = new List<TextMeshProUGUI>();
    private List<TextMeshProUGUI> timeYAxisLabels = new List<TextMeshProUGUI>();
    
    // Core analytical variables driven by real-time audio
    private float calculatedRMS = 0f;
    private float peakPressure = 0f;
    private float detectedDominantFrequency = 0f;
    private bool analysisComplete = false;

    private Vector3 chamberMin = new Vector3(-3.35f, 0.00f, -5.00f);
    private Vector3 chamberMax = new Vector3(3.35f, 6.70f, 5.00f);
    
    // Dynamic Audio Buffer Matrices
    private double samplingFrequency = 48000.0;
    private float[] spectrumDataArray;
    private float[] fullAudioClipSamples;
    private float nextFFTUpdateTime = 0f;

    void Start()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

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

        // Setup Dropdown Windowing Selection Listener
        if (windowTypeDropdown != null)
        {
            windowTypeDropdown.value = (int)selectedWindowType;
            windowTypeDropdown.onValueChanged.RemoveAllListeners();
            windowTypeDropdown.onValueChanged.AddListener(OnWindowTypeChanged);
        }

        // Setup Sliders
        if (sliderFFTTimeWindow != null)
        {
            sliderFFTTimeWindow.minValue = 0.10f;
            sliderFFTTimeWindow.maxValue = 2.00f;
            sliderFFTTimeWindow.wholeNumbers = false;
            sliderFFTTimeWindow.value = 0.10f;
            UpdateSliderLabel(sliderFFTTimeWindow);
        }

        if (sliderMaxFrequency != null)
        {
            sliderMaxFrequency.maxValue = 10000.0f;
            sliderMaxFrequency.minValue = 500.0f;   
            sliderMaxFrequency.wholeNumbers = true;
            sliderMaxFrequency.value = 3000.0f;     
            maxFFTFrequency = sliderMaxFrequency.value;
            UpdateSliderLabel(sliderMaxFrequency);

            sliderMaxFrequency.onValueChanged.RemoveAllListeners();
            sliderMaxFrequency.onValueChanged.AddListener((float newValue) => {
                maxFFTFrequency = newValue;
                UpdateSliderLabel(sliderMaxFrequency);
                UpdateFFTAxisLabelValues();
            });
        }

        LoadFullAudioClipData();
        InitializeDualGraphVisuals();
        InitializeAxisLabels();
        GenerateStaticFourierSlices();
        GenerateDynamicLegendTexture();
        analysisComplete = true;
    }

    void Update()
    {
        if (!analysisComplete) return;

        samplingFrequency = AudioSettings.outputSampleRate;
        
        AnalyzeAudioSourceVolume();
        UpdatePlayheadAndFFT();
        UpdateVisualShellsWithAudio();
        UpdateFivePointColorLegend();
        UpdateUIScreen();
        HandleWaveInterrogation();
    }

    private void OnWindowTypeChanged(int index)
    {
        selectedWindowType = (WindowType)index;
    }

    private void LoadFullAudioClipData()
    {
        if (audioSource != null && audioSource.clip != null)
        {
            AudioClip clip = audioSource.clip;
            fullAudioClipSamples = new float[clip.samples * clip.channels];
            clip.GetData(fullAudioClipSamples, 0);
        }
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

        // Static Full Waveform Time Plot
        if (timeGraphPanel != null && fullAudioClipSamples != null && fullAudioClipSamples.Length > 0)
        {
            RectTransform timeRect = timeGraphPanel.GetComponent<RectTransform>();
            if (timeRect != null)
            {
                float barWidth = timeRect.rect.width / totalVisualBars;
                int step = Mathf.Max(1, fullAudioClipSamples.Length / totalVisualBars);

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

                        int sampleIdx = Mathf.Clamp(i * step, 0, fullAudioClipSamples.Length - 1);
                        float waveAmp = Mathf.Abs(fullAudioClipSamples[sampleIdx]) * (graphBarHeightScale * 0.5f) * sensitivity;

                        barRect.sizeDelta = new Vector2(barWidth * 0.85f, Mathf.Max(waveAmp, 2f));
                        initializedTimeBars.Add(barRect);
                    }
                }
            }
        }
    }

    private void InitializeAxisLabels()
    {
        if (axisLabelPrefab == null) return;

        const int numXLabels = 11;
        float xOffset = 18.0f;

        // ==========================================
        // 1. FFT PANEL LABELS (16 Y-Axis Labels)
        // ==========================================
        if (fftGraphPanel != null)
        {
            RectTransform panelRect = fftGraphPanel.GetComponent<RectTransform>();
            float panelWidth = panelRect.rect.width;
            float panelHeight = panelRect.rect.height;

            // --- FFT X-Axis Labels (11 Labels: 0% to 100%) ---
            for (int i = 0; i < numXLabels; i++)
            {
                GameObject labelObj = Instantiate(axisLabelPrefab, fftGraphPanel.transform, false);
                labelObj.name = $"FFT_XAxis_Label_{i}";
                TextMeshProUGUI labelText = labelObj.GetComponent<TextMeshProUGUI>();

                if (labelText != null)
                {
                    labelText.alignment = TextAlignmentOptions.Center;

                    RectTransform labelRect = labelObj.GetComponent<RectTransform>();
                    labelRect.anchorMin = new Vector2(0f, 0f);
                    labelRect.anchorMax = new Vector2(0f, 0f);
                    labelRect.pivot = new Vector2(0.5f, 1f);

                    float normalizedStep = i / (float)(numXLabels - 1);
                    float xPos = (normalizedStep * panelWidth) + xOffset;
                    labelRect.anchoredPosition = new Vector2(xPos, -8f);

                    fftXAxisLabels.Add(labelText);
                }
            }

            // --- FFT Y-Axis Labels (16 Labels: Extending proportionally past 0) ---
            int fftNumYLabels = 16;
            float heightPerStep = panelHeight / 10.0f;

            for (int i = 0; i < fftNumYLabels; i++)
            {
                GameObject labelObj = Instantiate(axisLabelPrefab, fftGraphPanel.transform, false);
                labelObj.name = $"FFT_YAxis_Label_{i}";
                TextMeshProUGUI labelText = labelObj.GetComponent<TextMeshProUGUI>();

                if (labelText != null)
                {
                    labelText.alignment = TextAlignmentOptions.Right;

                    RectTransform labelRect = labelObj.GetComponent<RectTransform>();
                    labelRect.anchorMin = new Vector2(0f, 0f);
                    labelRect.anchorMax = new Vector2(0f, 0f);
                    labelRect.pivot = new Vector2(1f, 0.5f);

                    float yPos = i * heightPerStep;
                    labelRect.anchoredPosition = new Vector2(-10f, yPos);

                    fftYAxisLabels.Add(labelText);
                }
            }

            UpdateFFTAxisLabelValues();
        }

        // ==========================================
        // 2. TIME WAVEFORM PANEL LABELS (11 Y-Axis Labels)
        // ==========================================
        if (timeGraphPanel != null && audioSource != null && audioSource.clip != null)
        {
            RectTransform panelRect = timeGraphPanel.GetComponent<RectTransform>();
            float panelWidth = panelRect.rect.width;
            float panelHeight = panelRect.rect.height;
            float totalClipDuration = audioSource.clip.length;

            // --- Time X-Axis Labels (11 Labels) ---
            for (int i = 0; i < numXLabels; i++)
            {
                GameObject labelObj = Instantiate(axisLabelPrefab, timeGraphPanel.transform, false);
                labelObj.name = $"Time_XAxis_Label_{i}";
                TextMeshProUGUI labelText = labelObj.GetComponent<TextMeshProUGUI>();

                if (labelText != null)
                {
                    labelText.alignment = TextAlignmentOptions.Center;

                    RectTransform labelRect = labelObj.GetComponent<RectTransform>();
                    labelRect.anchorMin = new Vector2(0f, 0f);
                    labelRect.anchorMax = new Vector2(0f, 0f);
                    labelRect.pivot = new Vector2(0.5f, 1f);

                    float normalizedStep = i / (float)(numXLabels - 1);
                    float xPos = (normalizedStep * panelWidth) + xOffset;
                    
                    labelRect.anchoredPosition = new Vector2(xPos, -68f);

                    float timestamp = normalizedStep * totalClipDuration;
                    labelText.text = $"{timestamp:F2}";

                    timeXAxisLabels.Add(labelText);
                }
            }

            // --- Time Y-Axis Labels (11 Labels: -1.0 to +1.0) ---
            int timeNumYLabels = 11;

            for (int i = 0; i < timeNumYLabels; i++)
            {
                GameObject labelObj = Instantiate(axisLabelPrefab, timeGraphPanel.transform, false);
                labelObj.name = $"Time_YAxis_Label_{i}";
                TextMeshProUGUI labelText = labelObj.GetComponent<TextMeshProUGUI>();

                if (labelText != null)
                {
                    labelText.alignment = TextAlignmentOptions.Right;

                    RectTransform labelRect = labelObj.GetComponent<RectTransform>();
                    labelRect.anchorMin = new Vector2(0f, 0f);
                    labelRect.anchorMax = new Vector2(0f, 0f);
                    labelRect.pivot = new Vector2(1f, 0.5f);

                    float normalizedStep = i / (float)(timeNumYLabels - 1);
                    float yPos = normalizedStep * panelHeight;
                    labelRect.anchoredPosition = new Vector2(-10f, yPos);

                    float amplitudeVal = Mathf.Lerp(-1.0f, 1.0f, normalizedStep);
                    labelText.text = $"{amplitudeVal:F1}";

                    timeYAxisLabels.Add(labelText);
                }
            }
        }
    }

    private void UpdateFFTAxisLabelValues()
    {
        // Update X-Axis Frequency Labels
        if (fftXAxisLabels.Count > 0)
        {
            int numLabels = fftXAxisLabels.Count;
            for (int i = 0; i < numLabels; i++)
            {
                float frequencyValue = (i / (float)(numLabels - 1)) * maxFFTFrequency;
                fftXAxisLabels[i].text = $"{frequencyValue:F0}";
            }
        }

        // Update Y-Axis dB Values (-150 to +75 without "dB" text suffix)
        if (fftYAxisLabels.Count > 0)
        {
            int numLabels = fftYAxisLabels.Count;
            float stepSize = Mathf.Abs(minFFTdB) / 10.0f; // 15 units per step

            for (int i = 0; i < numLabels; i++)
            {
                float dBValue = minFFTdB + (i * stepSize);
                fftYAxisLabels[i].text = $"{dBValue:F0}";
            }
        }
    }

    private void UpdatePlayheadAndFFT()
    {
        if (audioSource == null || audioSource.clip == null) return;

        if (timePlayheadLine != null && timeGraphPanel != null)
        {
            RectTransform panelRect = timeGraphPanel.GetComponent<RectTransform>();
            float playbackProgressNormalized = Mathf.Clamp01(audioSource.time / audioSource.clip.length);
            float xPos = (playbackProgressNormalized * panelRect.rect.width) - (panelRect.rect.width / 2f);

            Vector2 currentPosition = timePlayheadLine.anchoredPosition;
            currentPosition.x = xPos;
            timePlayheadLine.anchoredPosition = currentPosition;
        }

        float windowDurationSeconds = (float)windowSizeSamples / (float)samplingFrequency;
        float fftComputeInterval = windowDurationSeconds * 10.0f;

        if (Time.time >= nextFFTUpdateTime)
        {
            nextFFTUpdateTime = Time.time + fftComputeInterval;
            UpdateRealTimeFFTGraph();
        }
    }

    private float ApplyWindowFunction(float sample, int index, int totalSamples)
    {
        switch (selectedWindowType)
        {
            case WindowType.Hamming:
                return sample * (0.54f - 0.46f * Mathf.Cos((2f * Mathf.PI * index) / (totalSamples - 1)));
            case WindowType.Hann:
                return sample * (0.5f * (1f - Mathf.Cos((2f * Mathf.PI * index) / (totalSamples - 1))));
            case WindowType.Blackman:
                return sample * (0.42f - 0.5f * Mathf.Cos((2f * Mathf.PI * index) / (totalSamples - 1)) + 0.08f * Mathf.Cos((4f * Mathf.PI * index) / (totalSamples - 1)));
            case WindowType.Rectangular:
            default:
                return sample;
        }
    }

    private void UpdateRealTimeFFTGraph()
    {
        if (fftGraphPanel == null || !fftGraphPanel.activeSelf || audioSource == null) return;

        if (sliderMaxFrequency != null && maxFFTFrequency != sliderMaxFrequency.value)
        {
            maxFFTFrequency = sliderMaxFrequency.value;
            UpdateFFTAxisLabelValues();
        }

        if (spectrumDataArray == null || spectrumDataArray.Length != windowSizeSamples)
        {
            spectrumDataArray = new float[windowSizeSamples];
        }

        audioSource.GetSpectrumData(spectrumDataArray, 0, FFTWindow.BlackmanHarris);

        for (int i = 0; i < spectrumDataArray.Length; i++)
        {
            spectrumDataArray[i] = ApplyWindowFunction(spectrumDataArray[i], i, spectrumDataArray.Length);
        }

        int totalBars = initializedFFTBars.Count;
        float halfSampleRate = (float)(samplingFrequency / 2.0);

        float[] sampledBValues = new float[totalBars];
        float maxComputeddB = -999f;
        float highestMagnitudeSeen = -1f;

        for (int i = 0; i < totalBars; i++)
        {
            float targetFrequency = ((float)i / (totalBars - 1)) * maxFFTFrequency;
            int spectrumIndex = Mathf.RoundToInt((targetFrequency / halfSampleRate) * windowSizeSamples);
            spectrumIndex = Mathf.Clamp(spectrumIndex, 0, windowSizeSamples - 1);

            float rawMagnitude = spectrumDataArray[spectrumIndex];
            float dB = 20f * Mathf.Log10(rawMagnitude + 1e-12f);
            sampledBValues[i] = dB;

            if (dB > maxComputeddB) maxComputeddB = dB;

            if (rawMagnitude > highestMagnitudeSeen)
            {
                highestMagnitudeSeen = rawMagnitude;
                detectedDominantFrequency = targetFrequency;
            }
        }

        float dynamicMaxdB = maxComputeddB + maxdBBuffer;

        for (int i = 0; i < totalBars; i++)
        {
            if (initializedFFTBars[i] == null) continue;

            float currentdB = sampledBValues[i];
            float normalizedHeight = Mathf.InverseLerp(minFFTdB, dynamicMaxdB, currentdB);

            float targetHeight = Mathf.Max(normalizedHeight * graphBarHeightScale, 2f);

            Vector2 alteredDimensions = initializedFFTBars[i].sizeDelta;
            alteredDimensions.y = Mathf.Lerp(alteredDimensions.y, targetHeight, Time.deltaTime * 14f);
            initializedFFTBars[i].sizeDelta = alteredDimensions;
        }
    }

    public void ToggleGraphVisibilityState()
    {
        if (fftGraphPanel != null) fftGraphPanel.SetActive(!fftGraphPanel.activeSelf);
        if (timeGraphPanel != null) timeGraphPanel.SetActive(!timeGraphPanel.activeSelf);
    }

    public void ReadSlidersAndRebuildSimulation()
    {
        if (sliderMaxFrequency != null)
        {
            maxFFTFrequency = sliderMaxFrequency.value;
            UpdateFFTAxisLabelValues();
        }
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