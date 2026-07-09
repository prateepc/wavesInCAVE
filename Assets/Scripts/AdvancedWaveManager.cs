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
    public Slider sliderDuration;
    public Slider sliderCount;
    public Slider sliderPower;
    public Slider sliderStep;
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

    private AudioSource audioSource;
    private List<GameObject> activeWaves = new List<GameObject>();
    
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

    // Step 4 Audio Pipeline State Variables
    private double audioPhase = 0.0;
    private double samplingFrequency = 48000.0; 
    private float outMuteFade = 1.0f;           

    void Start()
    {
        // Force complete structural variable allocation before anything else initializes
        principalFrequency = 343f;
        calculatedRMS = 0.5f; 
        numHarmonics = 0;   
        decayPower = 1.0f;   
        stepMultiplier = 1;
        peakPressure = calculatedRMS * 1.414f;
        
        System.Array.Clear(harmonicAmplitudes, 0, harmonicAmplitudes.Length);
        harmonicAmplitudes[0] = calculatedRMS;

        // SAFE HOOK: Cache the system sampling rate immediately on the main thread
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

        // Force the visual text label bridge items to match our baseline layout metrics
        if (sliderFrequency != null) { sliderFrequency.value = principalFrequency; UpdateSliderLabel(sliderFrequency); }
        if (sliderRMS != null) { sliderRMS.value = calculatedRMS; UpdateSliderLabel(sliderRMS); }
        if (sliderCount != null) { sliderCount.value = numHarmonics; UpdateSliderLabel(sliderCount); }
        if (sliderPower != null) { sliderPower.value = decayPower; UpdateSliderLabel(sliderPower); }
        if (sliderStep != null) { sliderStep.value = stepMultiplier; UpdateSliderLabel(sliderStep); }

        // Fire off the generation pass safely
        ConfigureAndStartSimulation();
    }

    void Update()
    {
        UpdateUIScreen();
        HandleWaveInterrogation();

        // NEW: Safely maintain sample rate tracking on the main thread
        samplingFrequency = AudioSettings.outputSampleRate;

        outMuteFade = Mathf.MoveTowards(outMuteFade, 1.0f, Time.deltaTime * 2.0f);
    }

    public void ReadSlidersAndRebuildSimulation()
    {
        // Fallback hooks to handle dynamic context finding if links break
        if (sliderFrequency == null) { GameObject go = GameObject.Find("Slider_Frequency"); if (go != null) sliderFrequency = go.GetComponent<Slider>(); }
        if (sliderRMS == null) { GameObject go = GameObject.Find("Slider_RMS"); if (go != null) sliderRMS = go.GetComponent<Slider>(); }
        if (sliderCount == null) { GameObject go = GameObject.Find("Slider_Count"); if (go != null) sliderCount = go.GetComponent<Slider>(); }
        if (sliderPower == null) { GameObject go = GameObject.Find("Slider_Power"); if (go != null) sliderPower = go.GetComponent<Slider>(); }
        if (sliderStep == null) { GameObject go = GameObject.Find("Slider_Step"); if (go != null) sliderStep = go.GetComponent<Slider>(); }

        // Fetch current values
        if (sliderFrequency != null) principalFrequency = sliderFrequency.value;
        if (sliderRMS != null) calculatedRMS = sliderRMS.value;
        if (sliderCount != null) numHarmonics = Mathf.RoundToInt(sliderCount.value);
        if (sliderPower != null) decayPower = sliderPower.value;
        if (sliderStep != null) stepMultiplier = Mathf.RoundToInt(sliderStep.value);

        Debug.Log($"UI Settings Read - Freq: {principalFrequency}Hz, RMS: {calculatedRMS}, Harmonics: {numHarmonics}");
        
        ConfigureAndStartSimulation();
    }

    public void ConfigureAndStartSimulation()
    {
        analysisComplete = false;
        outMuteFade = 0.0f; // Drop volume instantly to zero so it can smoothly fade back in pop-free
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

        // Execute procedural layout calculations
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

        // 1. Check for standard image architectures
        if (singleColorBarGraphic is UnityEngine.UI.Image uiImage)
        {
            uiImage.color = Color.white;
            uiImage.sprite = Sprite.Create(gradientTexture, new Rect(0, 0, 1, textureHeight), new Vector2(0.5f, 0.5f));
        }
        // 2. Direct texture deployment mapping for Raw Image layouts
        else if (singleColorBarGraphic is UnityEngine.UI.RawImage rawImage)
        {
            rawImage.color = Color.white;
            rawImage.texture = gradientTexture;
        }
    }

    private void UpdateSliderLabel(Slider targetSlider)
    {
        SliderTextBridge bridge = targetSlider.GetComponent<SliderTextBridge>();
        if (bridge != null)
        {
            bridge.UpdateTextValue(targetSlider.value);
        }
    }

    void GenerateStaticFourierSlices()
    {
        foreach (GameObject wave in activeWaves) { if (wave != null) Destroy(wave); }
        activeWaves.Clear();

        if (wavePrefab == null) return;

        float roomFadeMaxDistance = chamberMax.z - chamberMin.z; 
        float baseWavelength = SPEED_OF_SOUND / principalFrequency; 
        
        float totalWavelengthsInChamber = roomFadeMaxDistance / baseWavelength; 
        int slicesPerWavelength = 6; 
        
        int totalSlices = Mathf.CeilToInt(totalWavelengthsInChamber * slicesPerWavelength); 
        if (totalSlices < 2) totalSlices = 2;

        float spatialStepDistance = roomFadeMaxDistance / totalSlices;
        MaterialPropertyBlock propBlock = new MaterialPropertyBlock();
        float sourceOriginSafetyOffset = 0.05f; 

        for (int i = 1; i <= totalSlices; i++)
        {
            float r = (i * spatialStepDistance) + sourceOriginSafetyOffset;
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

            float normalizedPressure = (totalWeights > 0f) ? (complexAcousticWave / totalWeights) : 0f;
            normalizedPressure = Mathf.Clamp(normalizedPressure, -1f, 1f);

            Color combinedAcousticColor = Color.white;
            if (totalWeights > 0f)
            {
                BipolarSpectrumPair palette = harmonicColorPalettes[0]; 
                if (normalizedPressure >= 0f)
                {
                    combinedAcousticColor = Color.Lerp(palette.zeroPressureEquilibrium, palette.highPressureCrest, normalizedPressure);
                }
                else
                {
                    combinedAcousticColor = Color.Lerp(palette.zeroPressureEquilibrium, palette.lowPressureTrough, Mathf.Abs(normalizedPressure));
                }
            }

            float distanceDecay = Mathf.Clamp01(1.0f - (r / roomFadeMaxDistance));
            combinedAcousticColor.a = waveOpacity * distanceDecay;

            GameObject frozenWave = Instantiate(wavePrefab, spawnPosition, Quaternion.identity);
            frozenWave.name = $"Static_Spherical_Shell_{i}_Radius_{r:F2}m_P_{normalizedPressure:F2}";
            
            frozenWave.transform.localScale = new Vector3(r * 2f, r * 2f, r * 2f);
            ConfigureShaderBoundaries(frozenWave);

            Collider c = frozenWave.GetComponent<Collider>();
            if (c != null) c.isTrigger = true;

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
                
                Color emissionGlow = combinedAcousticColor * maxGlowIntensity * distanceDecay;
                propBlock.SetColor("_EmissionColor", emissionGlow);
                
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
                RectTransform panelRect = tooltipPanel.GetComponent<RectTransform>();
                if (panelRect != null)
                {
                    panelRect.anchorMin = Vector2.zero; panelRect.anchorMax = Vector2.zero; panelRect.pivot = Vector2.zero;
                }

                tooltipPanel.transform.position = (caveWandPointer != null) ? Camera.main.WorldToScreenPoint(hit.point) : Input.mousePosition + new Vector3(20f, 20f, 0f);
                float hitRadius = Vector3.Distance(transform.position, hit.point);

                tooltipText.text = $"<b>{targetedWave.harmonicOrder}</b>\n" +
                                   $"Analyzed Principal: {targetedWave.waveFrequency} Hz\n" +
                                   $"Global RMS Weight: {calculatedRMS:F5}\n" +
                                   $"Global Peak Pressure: {peakPressure:F5}\n" +
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
        string freqText = analysisComplete ? $"{principalFrequency:F1} Hz" : "Computing Fast Fourier Transform...";
    }

    /// <summary>
    /// Unity DSP Callback Engine: Synthesizes continuous wave samples on the audio thread
    /// to seamlessly align the physical acoustics with visual configurations.
    /// </summary>
    void OnAudioFilterRead(float[] data, int channels)
    {
        // SAFETY GUARD: No longer executing illegal main-thread calls here!
        if (!analysisComplete || calculatedRMS <= 0.001f || principalFrequency <= 0.1f)
        {
            System.Array.Clear(data, 0, data.Length);
            return;
        }

        for (int i = 0; i < data.Length; i += channels)
        {
            float currentAcousticSampleValue = 0f;

            // 1. Synthesize baseline fundamental tone (Uses the safely cached samplingFrequency variable)
            double fundamentalAngularVelocity = 2.0 * System.Math.PI * principalFrequency / samplingFrequency;
            currentAcousticSampleValue += harmonicAmplitudes[0] * (float)System.Math.Sin(audioPhase);

            // 2. Interleave harmonic overtones dynamically matching slider arrays
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
            
            if (audioPhase > 2.0 * System.Math.PI)
            {
                audioPhase %= (2.0 * System.Math.PI);
            }
        }
    }
}