using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI; 
using TMPro;

[RequireComponent(typeof(AudioSource))]
public class AdvancedWaveManager : MonoBehaviour
{
    [System.Serializable]
    public struct BipolarSpectrumPair
    {
        public Color lowPressureTrough;
        public Color zeroPressureEquilibrium;
        public Color highPressureCrest;
    }

    [Header("UI Canvas Configuration Elements")] 
    public Slider sliderFrequency;
    public Slider sliderRMS;
    public Slider sliderCount;
    public Slider sliderPower;
    public Slider sliderStep;
    public Slider sliderFFTTimeWindow;  // Time-duration window selection slider (0.1s - 2.0s)
    public Slider sliderMaxFrequency;   // Slider to dynamically adjust upper X-axis range
    public Button generateButton;

    [Header("UI Display Links")]
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
    [Tooltip("The base sphere/quad prefab used for flat spatial tracking slices.")]
    public GameObject wavePrefab;

    [Header("Visual Tuning Layout")]
    public float maxGlowIntensity = 5.0f;
    [Range(0f, 1f)] public float waveOpacity = 0.60f;

    [Header("Acoustic Layer Color Matrices")]
    [Tooltip("Divergent Color Maps (Low, Zero, High) for Fundamental, 2f, 3f, and 4f layers.")]
    public BipolarSpectrumPair[] harmonicColorPalettes = new BipolarSpectrumPair[]
    {
        new BipolarSpectrumPair { lowPressureTrough = Color.blue, zeroPressureEquilibrium = Color.white, highPressureCrest = Color.red },      // 1f Base fundamental mapping
        new BipolarSpectrumPair { lowPressureTrough = new Color(0f,0.2f,0f), zeroPressureEquilibrium = Color.white, highPressureCrest = Color.green }, // 2f
        new BipolarSpectrumPair { lowPressureTrough = new Color(0f,0.1f,0.3f), zeroPressureEquilibrium = Color.white, highPressureCrest = Color.cyan }, // 3f
        new BipolarSpectrumPair { lowPressureTrough = new Color(0.2f,0f,0.2f), zeroPressureEquilibrium = Color.white, highPressureCrest = Color.magenta } // 4f
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
    [Tooltip("Maximum upper limit for the FFT X-axis frequency spectrum (default 3000 Hz).")]
    public float maxFFTFrequency = 3000.0f;

    [Header("FFT dB Scale Settings")]
    [Tooltip("Fixed minimum y-axis value in dB.")]
    public float minFFTdB = -150.0f;
    [Tooltip("Buffer in dB added above the peak detected magnitude.")]
    public float maxdBBuffer = 10.0f;

    [Header("Axis Labeling System UI Prefabs")]
    [Tooltip("Prefab containing a TextMeshProUGUI component used to instantiate axis tick labels.")]
    public GameObject axisLabelPrefab;

    private AudioSource audioSource;
    private List<GameObject> activeWaves = new List<GameObject>();
    private List<RectTransform> initializedFFTBars = new List<RectTransform>();
    private List<RectTransform> initializedTimeBars = new List<RectTransform>();

    // Static Axis Label Management
    private List<TextMeshProUGUI> fftXAxisLabels = new List<TextMeshProUGUI>();
    private List<TextMeshProUGUI> timeXAxisLabels = new List<TextMeshProUGUI>();      
    private List<TextMeshProUGUI> fftYAxisLabels = new List<TextMeshProUGUI>();
    private List<TextMeshProUGUI> timeYAxisLabels = new List<TextMeshProUGUI>();

    // Core analytical variables
    private float principalFrequency = 343f;
    private float calculatedRMS = 0f;
    private float peakPressure = 0f;
    private float[] harmonicAmplitudes = new float[] { 0f, 0f, 0f, 0f };
    private bool analysisComplete = false;

    // Internal structural metrics configuration targets
    private float decayPower = 1.0f;
    private int stepMultiplier = 1;
    private int numHarmonics = 0;

    private Vector3 chamberMin = new Vector3(-3.35f, 0.00f, -5.00f);
    private Vector3 chamberMax = new Vector3(3.35f, 6.70f, 5.00f);
    private const float SPEED_OF_SOUND = 343.0f;

    // Audio Pipeline State Variables
    private double audioPhase = 0.0;
    private double samplingFrequency = 48000.0; 
    private float outMuteFade = 1.0f;           

    // Dynamic Fourier Array Resolution Parameters
    private int currentFFTSize = 1024;
    private float[] spectrumDataArray;

    void Start()
    {
        principalFrequency = 343f;
        calculatedRMS = 0.5f; 
        numHarmonics = 0;   
        decayPower = 1.0f;   
        stepMultiplier = 1;
        peakPressure = calculatedRMS * 1.414f;
        
        System.Array.Clear(harmonicAmplitudes, 0, harmonicAmplitudes.Length);
        harmonicAmplitudes[0] = calculatedRMS;

        samplingFrequency = AudioSettings.outputSampleRate;
        if (samplingFrequency <= 0) samplingFrequency = 48000.0;

        audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.playOnAwake = true;
            audioSource.loop = true;
        }

        if (uiTextDisplay == null)
        {
            GameObject foundUIObject = GameObject.Find("TelemetryDisplay");
            if (foundUIObject != null) uiTextDisplay = foundUIObject.GetComponent<TextMeshProUGUI>();
        }
        if (tooltipPanel != null) tooltipPanel.SetActive(false);

        if (generateButton != null)
        {
            generateButton.onClick.AddListener(ReadSlidersAndRebuildSimulation);
        }

        if (toggleGraphButton != null)
        {
            toggleGraphButton.onClick.AddListener(ToggleGraphVisibilityState);
        }

        // Initialize Dynamic Time-Window configuration boundaries
        if (sliderFFTTimeWindow != null)
        {
            sliderFFTTimeWindow.minValue = 0.10f; // Minimum bounds limit
            sliderFFTTimeWindow.maxValue = 2.00f; // Upper limit boundary
            sliderFFTTimeWindow.wholeNumbers = false;
            sliderFFTTimeWindow.value = 0.10f;    // Balanced default choice matching 44100-48000 standard configurations
            UpdateSliderLabel(sliderFFTTimeWindow);
        }
        UpdateFFTTimeWindow(sliderFFTTimeWindow != null ? sliderFFTTimeWindow.value : 0.10f);

        // Initialize Max Frequency Range Slider (Default 3000 Hz)
        if (sliderMaxFrequency != null)
        {
            sliderMaxFrequency.minValue = 500.0f;   // Lower bound
            sliderMaxFrequency.maxValue = 10000.0f;  // Upper bound
            sliderMaxFrequency.wholeNumbers = true; 
            sliderMaxFrequency.value = 3000.0f;     // Default setting
            
            maxFFTFrequency = sliderMaxFrequency.value;
            UpdateSliderLabel(sliderMaxFrequency);

            sliderMaxFrequency.onValueChanged.AddListener((float newValue) => {
                maxFFTFrequency = newValue;
                UpdateSliderLabel(sliderMaxFrequency);
                UpdateFFTAxisLabelValues();
            });
        }
        
        InitializeDualGraphVisuals();
        InitializeAxisLabels();

        if (sliderFrequency != null) { sliderFrequency.value = principalFrequency; UpdateSliderLabel(sliderFrequency); }
        if (sliderRMS != null) { sliderRMS.value = calculatedRMS; UpdateSliderLabel(sliderRMS); }
        if (sliderCount != null) { sliderCount.value = numHarmonics; UpdateSliderLabel(sliderCount); }
        if (sliderPower != null) { sliderPower.value = decayPower; UpdateSliderLabel(sliderPower); }
        if (sliderStep != null) { sliderStep.value = stepMultiplier; UpdateSliderLabel(sliderStep); }

        ConfigureAndStartSimulation();
    }

    void Update()
    {
        UpdateUIScreen();
        HandleWaveInterrogation();
        UpdateRealTimeFFTGraph();

        samplingFrequency = AudioSettings.outputSampleRate;
        outMuteFade = Mathf.MoveTowards(outMuteFade, 1.0f, Time.deltaTime * 2.0f);
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

        // --- GENERATE SPECTRUM FFT NODES ---
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

        // --- GENERATE WAVEFORM TIME-DOMAIN NODES ---
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

        // --- FFT X-Axis Labels ---
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

        // --- FFT Y-Axis Labels (16 Labels) ---
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
    if (timeGraphPanel != null)
    {
        RectTransform panelRect = timeGraphPanel.GetComponent<RectTransform>();
        float panelWidth = panelRect.rect.width;
        float panelHeight = panelRect.rect.height;
        float totalTimeWindow = sliderFFTTimeWindow != null ? sliderFFTTimeWindow.value : 0.10f;

        // --- Time X-Axis Labels ---
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

                float timestamp = normalizedStep * totalTimeWindow;
                labelText.text = $"{timestamp:F2}";

                timeXAxisLabels.Add(labelText);
            }
        }

        // --- Time Y-Axis Labels (Restored to 11 Labels: -1.0 to +1.0) ---
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

        // Update Y-Axis dB Scale Labels (-150 to +75 with fixed 15 dB steps)
        if (fftYAxisLabels.Count > 0)
        {
            int numLabels = fftYAxisLabels.Count;
            float stepSize = Mathf.Abs(minFFTdB) / 10.0f; // 15 dB per step

            for (int i = 0; i < numLabels; i++)
            {
                float dBValue = minFFTdB + (i * stepSize);
                fftYAxisLabels[i].text = $"{dBValue:F0}";
            }
        }
    }

    public void ToggleGraphVisibilityState()
    {
        if (fftGraphPanel != null) fftGraphPanel.SetActive(!fftGraphPanel.activeSelf);
        if (timeGraphPanel != null) timeGraphPanel.SetActive(!timeGraphPanel.activeSelf);
    }

    private void UpdateRealTimeFFTGraph()
    {
        bool isFFTActive = fftGraphPanel != null && fftGraphPanel.activeSelf;
        bool isTimeActive = timeGraphPanel != null && timeGraphPanel.activeSelf;

        if ((!isFFTActive && !isTimeActive) || audioSource == null || spectrumDataArray == null) return;

        // --- PROCESS TIME-DOMAIN WAVEFORM DISPLAY GRAPH ---
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

        // --- PROCESS FREQUENCY FFT SPECTRA DISPLAY GRAPH ---
        if (isFFTActive && initializedFFTBars.Count > 0)
        {
            int totalBars = initializedFFTBars.Count;

            audioSource.GetSpectrumData(spectrumDataArray, 0, FFTWindow.BlackmanHarris);

            float halfSampleRate = (float)(samplingFrequency / 2.0);

            // 1. Convert magnitudes to dB and calculate max for dynamic ceiling buffer
            float[] sampledBValues = new float[totalBars];
            float maxComputeddB = -999f;

            for (int i = 0; i < totalBars; i++)
            {
                // Dynamic upper frequency mapping governed by maxFFTFrequency (slider controlled)
                float targetFrequency = ((float)i / (totalBars - 1)) * maxFFTFrequency;
                int spectrumIndex = Mathf.RoundToInt((targetFrequency / halfSampleRate) * currentFFTSize);
                spectrumIndex = Mathf.Clamp(spectrumIndex, 0, currentFFTSize - 1);

                float rawMagnitude = spectrumDataArray[spectrumIndex];

                float dB = 20f * Mathf.Log10(rawMagnitude + 1e-12f);
                sampledBValues[i] = dB;

                if (dB > maxComputeddB)
                {
                    maxComputeddB = dB;
                }
            }

            float dynamicMaxdB = maxComputeddB + maxdBBuffer;

            // 2. Scale visual bar heights relative to [-150 dB, dynamicMaxdB]
            for (int i = 0; i < totalBars; i++)
            {
                if (initializedFFTBars[i] == null) continue;

                float currentdB = sampledBValues[i];

                float normalizedHeight = Mathf.InverseLerp(minFFTdB, dynamicMaxdB, currentdB);

                float targetHeight = normalizedHeight * graphBarHeightScale;
                targetHeight = Mathf.Max(targetHeight, 2f); 

                Vector2 alteredDimensions = initializedFFTBars[i].sizeDelta;
                alteredDimensions.y = Mathf.Lerp(alteredDimensions.y, targetHeight, Time.deltaTime * 14f);
                initializedFFTBars[i].sizeDelta = alteredDimensions;
            }
        }
    }

    public void ReadSlidersAndRebuildSimulation()
    {
        if (sliderFrequency != null) principalFrequency = sliderFrequency.value;
        if (sliderRMS != null) calculatedRMS = sliderRMS.value;
        if (sliderCount != null) numHarmonics = Mathf.RoundToInt(sliderCount.value);
        if (sliderPower != null) decayPower = sliderPower.value;
        if (sliderStep != null) stepMultiplier = Mathf.RoundToInt(sliderStep.value);
        if (sliderMaxFrequency != null) 
        {
            maxFFTFrequency = sliderMaxFrequency.value;
            UpdateFFTAxisLabelValues();
        }
        
        if (sliderFFTTimeWindow != null) UpdateFFTTimeWindow(sliderFFTTimeWindow.value);

        ConfigureAndStartSimulation();
    }

    public void ConfigureAndStartSimulation()
    {
        analysisComplete = false;
        outMuteFade = 0.0f; 
        System.Array.Clear(harmonicAmplitudes, 0, harmonicAmplitudes.Length);

        harmonicAmplitudes[0] = calculatedRMS;
        peakPressure = calculatedRMS * 1.414f; 

        if (numHarmonics > 0)
        {
            for (int idx = 1; idx <= numHarmonics; idx++)
            {
                int multiplier = 1 + (stepMultiplier * idx);
                if (multiplier > 4) continue;
                harmonicAmplitudes[multiplier - 1] = calculatedRMS / Mathf.Pow(multiplier, decayPower);
            }
        }

        UpdateFivePointColorLegend();
        GenerateDynamicLegendTexture();

        analysisComplete = true;
        GenerateStaticFourierSlices();
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

    void GenerateStaticFourierSlices()
    {
        foreach (GameObject wave in activeWaves) { if (wave != null) Destroy(wave); }
        activeWaves.Clear();

        if (wavePrefab == null) return;

        float roomFadeMaxDistance = chamberMax.z - chamberMin.z; 
        float baseWavelength = SPEED_OF_SOUND / principalFrequency; 
        float totalWavelengthsInChamber = roomFadeMaxDistance / baseWavelength; 
        
        int totalSlices = Mathf.CeilToInt(totalWavelengthsInChamber * 6); 
        if (totalSlices < 2) totalSlices = 2;

        float spatialStepDistance = roomFadeMaxDistance / totalSlices;
        MaterialPropertyBlock propBlock = new MaterialPropertyBlock();

        for (int i = 1; i <= totalSlices; i++)
        {
            float r = (i * spatialStepDistance) + 0.05f;
            Vector3 spawnPosition = transform.position;

            float complexAcousticWave = 0f;
            float totalWeights = 0f;
            float fundamentalPhaseSign = 0f;

            for (int h = 1; h <= 4; h++)
            {
                float amplitudeWeight = harmonicAmplitudes[h - 1];
                if (amplitudeWeight > 0f)
                {
                    float currentFrequency = principalFrequency * h;
                    float k = (2f * Mathf.PI * currentFrequency) / SPEED_OF_SOUND;
                    float layerPressure = (amplitudeWeight / r) * Mathf.Sin(k * r);

                    complexAcousticWave += layerPressure;
                    totalWeights += (amplitudeWeight / r); 

                    if (h == 1) fundamentalPhaseSign = layerPressure;
                }
            }

            float normalizedPressure = (totalWeights > 0f) ? Mathf.Clamp(complexAcousticWave / totalWeights, -1f, 1f) : 0f;
            Color combinedAcousticColor = Color.white;

            if (totalWeights > 0f)
            {
                BipolarSpectrumPair palette = harmonicColorPalettes[0]; 
                combinedAcousticColor = normalizedPressure >= 0f ? 
                    Color.Lerp(palette.zeroPressureEquilibrium, palette.highPressureCrest, normalizedPressure) : 
                    Color.Lerp(palette.zeroPressureEquilibrium, palette.lowPressureTrough, Mathf.Abs(normalizedPressure));
            }

            float distanceDecay = Mathf.Clamp01(1.0f - (r / roomFadeMaxDistance));
            combinedAcousticColor.a = waveOpacity * distanceDecay;

            GameObject frozenWave = Instantiate(wavePrefab, spawnPosition, Quaternion.identity);
            frozenWave.transform.localScale = new Vector3(r * 2f, r * 2f, r * 2f);
            ConfigureShaderBoundaries(frozenWave);

            WaveDataIdentifier identifier = frozenWave.AddComponent<WaveDataIdentifier>();
            identifier.waveFrequency = Mathf.RoundToInt(principalFrequency);
            identifier.harmonicOrder = fundamentalPhaseSign > 0.02f ? "Fourier Compression Zone (High Pressure Crest)" :
                                       fundamentalPhaseSign < -0.02f ? "Fourier Rarefaction Zone (Low Pressure Trough)" : "Acoustic Equilibrium Node (Zero Pressure)";

            Renderer waveRenderer = frozenWave.GetComponent<Renderer>();
            if (waveRenderer != null)
            {
                waveRenderer.sharedMaterial.EnableKeyword("_EMISSION");
                waveRenderer.GetPropertyBlock(propBlock);
                propBlock.SetColor("_Color", combinedAcousticColor);
                propBlock.SetColor("_BaseColor", combinedAcousticColor);
                propBlock.SetColor("_EmissionColor", combinedAcousticColor * maxGlowIntensity * distanceDecay);
                waveRenderer.SetPropertyBlock(propBlock);
            }

            activeWaves.Add(frozenWave);
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
                tooltipPanel.transform.position = (caveWandPointer != null) ? Camera.main.WorldToScreenPoint(hit.point) : Input.mousePosition + new Vector3(20f, 20f, 0f);
                
                tooltipText.text = $"<b>{targetedWave.harmonicOrder}</b>\n" +
                                   $"Analyzed Principal: {targetedWave.waveFrequency} Hz\n" +
                                   $"Global RMS Weight: {calculatedRMS:F5}\n" +
                                   $"Distance From Source: {Vector3.Distance(transform.position, hit.point):F2} m";
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

    void UpdateUIScreen() { }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!analysisComplete || calculatedRMS <= 0.001f || principalFrequency <= 0.1f || harmonicAmplitudes == null)
        {
            System.Array.Clear(data, 0, data.Length);
            return;
        }

        for (int i = 0; i < data.Length; i += channels)
        {
            float currentAcousticSampleValue = 0f;
            double fundamentalAngularVelocity = 2.0 * System.Math.PI * principalFrequency / samplingFrequency;
            currentAcousticSampleValue += harmonicAmplitudes[0] * (float)System.Math.Sin(audioPhase);

            if (numHarmonics > 0)
            {
                for (int h = 1; h <= numHarmonics; h++)
                {
                    int harmonicMultiplier = 1 + (stepMultiplier * h);
                    if (harmonicMultiplier > 4) continue;

                    float harmonicWeight = harmonicAmplitudes[harmonicMultiplier - 1];
                    if (harmonicWeight > 0f)
                    {
                        currentAcousticSampleValue += harmonicWeight * (float)System.Math.Sin(audioPhase * harmonicMultiplier);
                    }
                }
            }

            currentAcousticSampleValue = Mathf.Clamp(currentAcousticSampleValue, -1.0f, 1.0f);

            for (int c = 0; c < channels; c++)
            {
                data[i + c] = currentAcousticSampleValue * outMuteFade;
            }

            audioPhase += fundamentalAngularVelocity;
            if (audioPhase > 2.0 * System.Math.PI) audioPhase %= (2.0 * System.Math.PI);
        }
    }
}