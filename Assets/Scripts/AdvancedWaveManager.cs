using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI; // Required for Slider and Button references
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
    
    // Core analytical variables (Now driven dynamically by Sliders)
    private float principalFrequency = 343f;
    private float calculatedRMS = 0f;
    private float peakPressure = 0f;
    private float[] harmonicAmplitudes = new float[] { 0f, 0f, 0f, 0f };
    private bool analysisComplete = false;

    // Internal structural metrics configuration targets
    private float decayPower = 1.5f;
    private int stepMultiplier = 1;
    private int numHarmonics = 3;

    private Vector3 chamberMin = new Vector3(-3.35f, 0.00f, -5.00f);
    private Vector3 chamberMax = new Vector3(3.35f, 6.70f, 5.00f);
    private const float SPEED_OF_SOUND = 343.0f;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
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

        // UPDATED: Boots up using your default slider values directly on frame one
        ReadSlidersAndRebuildSimulation();
    }

    void Update()
    {
        UpdateUIScreen();
        HandleWaveInterrogation();
    }

    /// <summary>
    /// Reads current slider configurations from UI layout elements
    /// and updates the local internal state parameters.
    /// </summary>
    public void ReadSlidersAndRebuildSimulation()
    {
        if (sliderFrequency != null) principalFrequency = sliderFrequency.value;
        if (sliderRMS != null) calculatedRMS = sliderRMS.value;
        if (sliderCount != null) numHarmonics = Mathf.RoundToInt(sliderCount.value);
        if (sliderPower != null) decayPower = sliderPower.value;
        if (sliderStep != null) stepMultiplier = Mathf.RoundToInt(sliderStep.value);

        // UPDATED FOR STEP 3: Re-routed away from disk discovery straight into the physics compiler
        ConfigureAndStartSimulation();
    }

    /// <summary>
    /// ADDED FOR STEP 3: Bypasses file loading entirely. Synthesizes the exact 
    /// harmonic vector arrays mathematical fields from the slider states instantly.
    /// </summary>
    public void ConfigureAndStartSimulation()
    {
        analysisComplete = false;
        System.Array.Clear(harmonicAmplitudes, 0, harmonicAmplitudes.Length);

        // 1. Assign the fundamental baseline core energy (Index 0 = 1f)
        harmonicAmplitudes[0] = calculatedRMS;
        peakPressure = calculatedRMS * 1.414f; // Clean sine amplitude peak scaling translation

        // 2. Synthesize upper overtone weights using the Python decay profile matching step choice configurations
        if (numHarmonics > 0)
        {
            for (int idx = 1; idx <= numHarmonics; idx++)
            {
                // stepMultiplier = 1 -> Multipliers: 2, 3, 4 (Sequential series)
                // stepMultiplier = 2 -> Multipliers: 3, 5, 7 (Odd series)
                int multiplier = 1 + (stepMultiplier * idx);

                // Stop assigning weights if they exceed our 4-layer physical visualization collection structures
                if (multiplier > 4) continue;

                // Core exponential decay drop algorithm ported directly from the custom script framework
                harmonicAmplitudes[multiplier - 1] = calculatedRMS / Mathf.Pow(multiplier, decayPower);
            }
        }

        analysisComplete = true;

        // Instantly generate the 3D visual slices inside your room volume layout
        GenerateStaticFourierSlices();
    }

    // NOTE: DiscoverAndAnalyzeLatestWav, LoadAndAnalyzeAudioPipeline, and ExecuteInPlaceCooleyTukeyFFT
    // have been hidden/bypassed here since our data stream is now purely procedural. 
    // They are left completely out of the execution line now.

    void GenerateStaticFourierSlices()
    {
        foreach (GameObject wave in activeWaves) { if (wave != null) Destroy(wave); }
        activeWaves.Clear();

        if (wavePrefab == null) return;

        float roomFadeMaxDistance = chamberMax.z - chamberMin.z; // 10.0 meters total long
        float baseWavelength = SPEED_OF_SOUND / principalFrequency; // Dynamic to slider now!
        
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
}