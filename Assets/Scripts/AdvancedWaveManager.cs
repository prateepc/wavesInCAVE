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
    public Slider sliderFFTResolution; // Dynamic bin sizing control layout slider
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

    [Header("Real-Time Graphing System UI links")]
    [Tooltip("The manual toggle control button link.")]
    public Button toggleGraphButton;
    [Tooltip("The parent object container containing our visual chart nodes.")]
    public GameObject fftGraphPanel;
    [Tooltip("Basic layout node image prefab used for generating spectrum layout nodes.")]
    public GameObject graphBarPrefab;
    [Tooltip("Total vertical multiplier adjusting spectrum display height tracking arrays.")]
    public float graphBarHeightScale = 300.0f;

    [Header("Multi-Peak Axis Tracking UI Elements")]
    [Tooltip("The prefab used to spawn sliding frequency labels along the X-axis.")]
    public GameObject peakFreqLabelPrefab;
    [Tooltip("The prefab used to spawn sliding amplitude labels along the Y-axis.")]
    public GameObject peakAmpLabelPrefab;
    [Tooltip("Minimum threshold of energy required to count as an active peak.")]
    public float peakDetectionThreshold = 0.005f;
    [Tooltip("A general sensitivity modifier translating raw spectral magnitudes back into printable Pascal pressure layouts.")]
    public float graphSensitivityMultiplier = 20.0f;

    private AudioSource audioSource;
    private List<GameObject> activeWaves = new List<GameObject>();
    private List<RectTransform> initializedGraphBars = new List<RectTransform>();
    private List<GameObject> activeLabelPool = new List<GameObject>();
    
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

       // Initialize Dynamic Power-of-Two FFT Resolution slider metrics
        if (sliderFFTResolution != null)
        {
            sliderFFTResolution.minValue = 1; // Minimum changed to 1
            sliderFFTResolution.maxValue = 7; // Maximum stays 7
            sliderFFTResolution.wholeNumbers = true;
            sliderFFTResolution.value = 1;    // Default baseline selection maps to 1024
            UpdateSliderLabel(sliderFFTResolution);
        }
        UpdateFFTResolution(sliderFFTResolution != null ? (int)sliderFFTResolution.value : 1);
        InitializeFFTGraphVisuals();

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

    public void UpdateFFTResolution(int sliderStepValue)
    {
        // Clamp incoming slider value to the strict 1-7 layout range
        sliderStepValue = Mathf.Clamp(sliderStepValue, 1, 7);

        // Linearly map slider values 1 through 7 to exponent values 10 through 12
        // Step 1 -> 10 (1024)
        // Step 4 -> 11 (2048)
        // Step 7 -> 12 (4096)
        float normalizedStep = (sliderStepValue - 1f) / 6f; // 0.0 to 1.0
        int targetExponent = Mathf.RoundToInt(Mathf.Lerp(10f, 12f, normalizedStep));

        currentFFTSize = (int)Mathf.Pow(2, targetExponent);
        currentFFTSize = Mathf.Clamp(currentFFTSize, 1024, 4096);

        // Reallocate internal matrix size arrays securely at runtime
        spectrumDataArray = new float[currentFFTSize];
    }

    private void InitializeFFTGraphVisuals()
    {
        if (fftGraphPanel == null || graphBarPrefab == null) return;

        RectTransform panelRect = fftGraphPanel.GetComponent<RectTransform>();
        if (panelRect == null) return;

        int totalVisualBars = 256;
        float containerWidth = panelRect.rect.width;
        float individualBarWidth = containerWidth / totalVisualBars;

        for (int i = 0; i < totalVisualBars; i++)
        {
            GameObject barInstance = Instantiate(graphBarPrefab, fftGraphPanel.transform, false);
            RectTransform barRect = barInstance.GetComponent<RectTransform>();
            
            if (barRect != null)
            {
                barRect.anchorMin = new Vector2(0f, 0f);
                barRect.anchorMax = new Vector2(0f, 0f);
                barRect.pivot = new Vector2(0.5f, 0f);
                
                barRect.anchoredPosition = new Vector2((i * individualBarWidth) + (individualBarWidth / 2f), 0f);
                barRect.sizeDelta = new Vector2(individualBarWidth * 0.85f, 0f); 
                
                initializedGraphBars.Add(barRect);
            }
        }
    }

    public void ToggleGraphVisibilityState()
    {
        if (fftGraphPanel != null)
        {
            fftGraphPanel.SetActive(!fftGraphPanel.activeSelf);
        }
    }

    private void UpdateRealTimeFFTGraph()
    {
        if (fftGraphPanel == null || !fftGraphPanel.activeSelf || audioSource == null || peakFreqLabelPrefab == null || peakAmpLabelPrefab == null || spectrumDataArray == null) return;

        // 1. Gather dynamic high-resolution spectrum data matrices
        audioSource.GetSpectrumData(spectrumDataArray, 0, FFTWindow.BlackmanHarris);

        // Wipe old temporary axis text labels from the previous frame
        for (int i = activeLabelPool.Count - 1; i >= 0; i--)
        {
            if (activeLabelPool[i] != null) Destroy(activeLabelPool[i]);
        }
        activeLabelPool.Clear();

        float panelWidth = fftGraphPanel.GetComponent<RectTransform>().rect.width;

        // 2. Map and scale visible display layout bars across dynamic logarithmic frequency maps
        for (int i = 0; i < initializedGraphBars.Count; i++)
        {
            if (initializedGraphBars[i] == null) continue;

            float normalizedPosition = (float)i / initializedGraphBars.Count;
            int spectrumIndex = Mathf.FloorToInt(Mathf.Pow(normalizedPosition, 2f) * (currentFFTSize - i));
            spectrumIndex = Mathf.Clamp(spectrumIndex, 0, currentFFTSize - 1);

            float sampleIntensity = spectrumDataArray[spectrumIndex];
            float dynamicHeightValue = Mathf.Clamp(sampleIntensity * graphBarHeightScale, 2f, graphBarHeightScale);

            Vector2 alteredDimensions = initializedGraphBars[i].sizeDelta;
            alteredDimensions.y = Mathf.Lerp(alteredDimensions.y, dynamicHeightValue, Time.deltaTime * 12f);
            initializedGraphBars[i].sizeDelta = alteredDimensions;

            // 3. Multi-Peak Axis Tracker (Local Maxima evaluation)
            if (i > 0 && i < initializedGraphBars.Count - 1)
            {
                float currentVisualHeight = alteredDimensions.y;
                float prevHeight = initializedGraphBars[i - 1].sizeDelta.y;

                float nextHeight = dynamicHeightValue; 
                if (i + 1 < initializedGraphBars.Count)
                {
                    float nextNorm = (float)(i + 1) / initializedGraphBars.Count;
                    int nextIdx = Mathf.Clamp(Mathf.FloorToInt(Mathf.Pow(nextNorm, 2f) * (currentFFTSize - (i + 1))), 0, currentFFTSize - 1);
                    nextHeight = Mathf.Clamp(spectrumDataArray[nextIdx] * graphBarHeightScale, 2f, graphBarHeightScale);
                }

                // If sample breaks baseline threshold boundaries and is taller than immediate neighbors
                if (sampleIntensity > peakDetectionThreshold && currentVisualHeight > prevHeight && currentVisualHeight > nextHeight)
                {
                    // Calculate precise coordinate values from the visual layout
                    float horizontalPercentage = (float)i / (initializedGraphBars.Count - 1);
                    float targetXCoordinate = (horizontalPercentage * panelWidth) - (panelWidth / 2f);
                    float peakBarLocalYHeight = alteredDimensions.y;

                    float halfSampleRate = (float)(samplingFrequency / 2.0);
                    float peakFrequency = spectrumIndex * halfSampleRate / currentFFTSize;
                    float pressure = sampleIntensity * graphSensitivityMultiplier;

                    // --- SPAWN FREQUENCY LABEL ---
                    GameObject freqLabel = Instantiate(peakFreqLabelPrefab, fftGraphPanel.transform, false);
                    activeLabelPool.Add(freqLabel);

                    TextMeshProUGUI freqText = freqLabel.GetComponent<TextMeshProUGUI>();
                    if (freqText != null)
                    {
                        freqText.text = $"{peakFrequency:F0}";
                        RectTransform freqRect = freqLabel.GetComponent<RectTransform>();
                        
                        Vector3 localPos = freqRect.localPosition;
                        localPos.x = targetXCoordinate;
                        freqRect.localPosition = localPos;
                    }

                    // --- SPAWN AMPLITUDE LABEL ---
                    GameObject ampLabel = Instantiate(peakAmpLabelPrefab, fftGraphPanel.transform, false);
                    activeLabelPool.Add(ampLabel);

                    TextMeshProUGUI ampText = ampLabel.GetComponent<TextMeshProUGUI>();
                    if (ampText != null)
                    {
                        ampText.text = $"{pressure:F2}";
                        RectTransform ampRect = ampLabel.GetComponent<RectTransform>();
                        
                        // 1. Unified Horizontal Axis Sync Placement Setup Logic
                        Vector3 localPos = ampRect.localPosition;
                        localPos.x = targetXCoordinate; // Strictly force X alignment to match the frequency label's coordinate
                        ampRect.localPosition = localPos;

                        // 2. Re-map ONLY the Y coordinate via anchoredPosition to maintain structural sliding vertical axis functionality
                        Vector2 currentAnchoredPos = ampRect.anchoredPosition;
                        currentAnchoredPos.y = peakBarLocalYHeight; 
                        ampRect.anchoredPosition = currentAnchoredPos;
                    }
                }
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
        
        // Read and dynamically allocate dynamic bin distributions
        if (sliderFFTResolution != null) UpdateFFTResolution((int)sliderFFTResolution.value);

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