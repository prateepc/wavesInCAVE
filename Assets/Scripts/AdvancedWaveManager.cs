using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

[RequireComponent(typeof(AudioSource))]
public class AdvancedWaveManager : MonoBehaviour 
{
    public enum SimulationMode { Continuous, FreezeFrame }

    [Header("Simulation Mode Settings")]
    [Tooltip("Press SPACE during runtime to toggle between Continuous and Freeze Frame modes.")]
    public SimulationMode currentMode = SimulationMode.Continuous;

    [Header("UI Display Link")]
    public TextMeshProUGUI uiTextDisplay;

    [Header("Wave Prefab Link")]
    [Tooltip("The base sphere prefab used for both fundamental and harmonic visualizations.")]
    public GameObject wavePrefab;

    [Header("Visual Tuning Layout")]
    [Tooltip("Controls how intensely loud sounds glow within the CAVE environment.")]
    public float maxGlowIntensity = 4.0f;

    [Header("Constant Translucency Settings")]
    [Range(0f, 1f)] public float fundamentalAlpha = 0.80f; // Increased for higher opacity
    [Range(0f, 1f)] public float harmonicAlpha = 0.70f;    // Increased for higher opacity

    [Header("Simulation Timing")]
    private float waveGenerationInterval = 2f;

    [Header("Acoustic Physics Settings")]
    private float simulationSpeedMultiplier = 0.001f;
    private int targetFrequency = 440;
    
    private AudioSource audioSource;
    
    // Tracks the active fundamental parent spheres
    private List<GameObject> activeWaves = new List<GameObject>();
    
    // Data arrays mapped to the automated CSV structure
    private List<float> csvRadii = new List<float>();
    private List<float> csvFundamentalRMS = new List<float>();
    private List<float> csvHarmonicRMS = new List<float>();
    
    private float waveTimer = 0f;
    private bool dataLoaded = false;
    private float maxDistanceToFurthestCorner = 0f;
    
    // Tracking maximum bounds for dynamic normalization
    private float maxFundRMS = 0.001f; 
    private float maxHarmRMS = 0.001f; 
    
    // Live feedback strings mapped up to the UI text canvas loop
    private float liveFundamentalRMS = 0f; 
    private float liveHarmonicRMS = 0f;

    // Hardcoded Absolute Structural Boundaries based on your precise 3D model list
    private Vector3 chamberMin = new Vector3(-3.35f, 0.00f, -5.00f); 
    private Vector3 chamberMax = new Vector3(3.35f, 6.70f, 5.00f);  

    void Start() 
    {
        audioSource = GetComponent<AudioSource>();
        maxDistanceToFurthestCorner = CalculateMaxCornerDistance();

        if (uiTextDisplay == null)
        {
            GameObject foundUIObject = GameObject.Find("TelemetryDisplay");
            if (foundUIObject != null)
            {
                uiTextDisplay = foundUIObject.GetComponent<TextMeshProUGUI>();
            }
        }

        AutomatePipelineDiscovery();
    }

    void Update() 
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ToggleSimulationMode();
        }

        UpdateUIScreen(); 

        if (!dataLoaded) return; 

        if (currentMode == SimulationMode.Continuous)
        {
            waveTimer += Time.deltaTime;
            if (waveTimer >= waveGenerationInterval) 
            {
                EmitCompoundWaveSnapshot();
                waveTimer = 0f;
            }
            ProcessWavePhysics();
        }
    }

    public void ToggleSimulationMode()
    {
        ClearActiveWaves();

        if (currentMode == SimulationMode.Continuous)
        {
            currentMode = SimulationMode.FreezeFrame;
            EmitFreezeFrameWaves();
        }
        else
        {
            currentMode = SimulationMode.Continuous;
            waveTimer = 0f; 
        }
    }

    private void ClearActiveWaves()
    {
        foreach (GameObject wave in activeWaves)
        {
            if (wave != null) Destroy(wave);
        }
        activeWaves.Clear();
    }

    /// <summary>
    /// Spawns a fundamental sphere and nests an independent harmonic sphere inside it.
    /// </summary>
    void EmitCompoundWaveSnapshot() 
    {
        if (wavePrefab == null) return;

        // 1. Create Fundamental Outer Wave Shell
        GameObject fundamentalWave = Instantiate(wavePrefab, transform.position, Quaternion.identity);
        fundamentalWave.name = "Fundamental_Shell";
        fundamentalWave.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
        ConfigureShaderBoundaries(fundamentalWave);

        // 2. Create Concentric Harmonic Inner Companion Shell
        GameObject harmonicWave = Instantiate(wavePrefab, transform.position, Quaternion.identity);
        harmonicWave.name = "Harmonic_Shell";
        
        // Parent the harmonic wave to the fundamental so they scale cleanly together
        harmonicWave.transform.SetParent(fundamentalWave.transform);
        harmonicWave.transform.localPosition = Vector3.zero;
        harmonicWave.transform.localRotation = Quaternion.identity;
        harmonicWave.transform.localScale = Vector3.one * 1.01f; 
        ConfigureShaderBoundaries(harmonicWave);

        activeWaves.Add(fundamentalWave);
    }

    void ProcessWavePhysics() 
    {
        List<GameObject> deadWaves = new List<GameObject>();

        float logFreq = Mathf.Log10(targetFrequency);
        float normFreq = Mathf.InverseLerp(Mathf.Log10(20f), Mathf.Log10(20000f), logFreq);
        
        Color baseFundamentalColor = Color.HSVToRGB(normFreq * 0.85f, 0.9f, 0.9f); 
        Color baseHarmonicColor = Color.magenta; 

        float trackingFundRMS = 0f;
        float trackingHarmRMS = 0f;

        for (int i = 0; i < activeWaves.Count; i++) 
        {
            GameObject fundWave = activeWaves[i];
            if (fundWave == null) continue;

            float currentRadius = fundWave.transform.localScale.x / 2f;
            currentRadius += 343f * Time.deltaTime * simulationSpeedMultiplier;
            fundWave.transform.localScale = new Vector3(currentRadius * 2f, currentRadius * 2f, currentRadius * 2f);

            float roomFadeMaxDistance = chamberMax.z - chamberMin.z;
            float distanceProgress = Mathf.Clamp01(currentRadius / roomFadeMaxDistance);
            float distanceDecay = Mathf.Clamp01(1.0f - distanceProgress);

            float fundAmpModifier = 0.0f;
            float harmAmpModifier = 0.0f;

            if (csvFundamentalRMS.Count > 0)
            {
                int index = Mathf.FloorToInt(distanceProgress * (csvFundamentalRMS.Count - 1));
                index = Mathf.Clamp(index, 0, csvFundamentalRMS.Count - 1);

                trackingFundRMS = csvFundamentalRMS[index];
                trackingHarmRMS = csvHarmonicRMS[index];

                fundAmpModifier = trackingFundRMS / maxFundRMS;
                harmAmpModifier = trackingHarmRMS / maxHarmRMS;
            }

            // High volume pushes the harmonic ring structurally outward
            if (fundWave.transform.childCount > 0)
            {
                Transform harmonicChild = fundWave.transform.GetChild(0);
                float physicalShellGap = 1.005f + (harmAmpModifier * 0.05f);
                harmonicChild.transform.localScale = Vector3.one * physicalShellGap;

                // Apply dynamic HDR Glow to the Harmonic Shell with CONSTANT Alpha
                Renderer harmRenderer = harmonicChild.GetComponent<Renderer>();
                if (harmRenderer != null)
                {
                    harmRenderer.material.EnableKeyword("_EMISSION");
                    Color harmColor = baseHarmonicColor;
                    harmColor.a = harmonicAlpha * distanceDecay; // Steady constant translucency fading over chamber depth
                    harmRenderer.material.color = harmColor;
                    
                    // Drive visual brightness via target volume energy
                    Color dynamicEmission = baseHarmonicColor * harmAmpModifier * maxGlowIntensity * distanceDecay;
                    harmRenderer.material.SetColor("_EmissionColor", dynamicEmission);
                }
            }

            // Apply dynamic HDR Glow to the Fundamental Shell with CONSTANT Alpha
            Renderer fundRenderer = fundWave.GetComponent<Renderer>();
            if (fundRenderer != null)
            {
                fundRenderer.material.EnableKeyword("_EMISSION");
                Color fundColor = baseFundamentalColor;
                fundColor.a = fundamentalAlpha * distanceDecay; // Steady constant translucency fading over chamber depth
                fundRenderer.material.color = fundColor;

                Color dynamicEmission = baseFundamentalColor * fundAmpModifier * maxGlowIntensity * distanceDecay;
                fundRenderer.material.SetColor("_EmissionColor", dynamicEmission);
            }

            if (currentRadius >= maxDistanceToFurthestCorner) 
            {
                deadWaves.Add(fundWave);
            }
        }

        liveFundamentalRMS = (activeWaves.Count > 0) ? trackingFundRMS : 0f;
        liveHarmonicRMS = (activeWaves.Count > 0) ? trackingHarmRMS : 0f;

        foreach (GameObject expiredWave in deadWaves) 
        {
            activeWaves.Remove(expiredWave);
            Destroy(expiredWave);
        }
    }

    void EmitFreezeFrameWaves()
    {
        if (wavePrefab == null) return;

        float logFreq = Mathf.Log10(targetFrequency);
        float normFreq = Mathf.InverseLerp(Mathf.Log10(20f), Mathf.Log10(20000f), logFreq);
        Color baseFundamentalColor = Color.HSVToRGB(normFreq * 0.85f, 0.9f, 0.9f);
        Color baseHarmonicColor = Color.magenta;

        float roomFadeMaxDistance = chamberMax.z - chamberMin.z;
        float totalTimeForMaxDepth = roomFadeMaxDistance / 343f;
        float timeStep = totalTimeForMaxDepth / 10f;

        for (int i = 1; i <= 10; i++)
        {
            float targetTime = i * timeStep;
            float frozenRadius = 343f * targetTime; 

            GameObject frozenFundWave = Instantiate(wavePrefab, transform.position, Quaternion.identity);
            frozenFundWave.transform.localScale = new Vector3(frozenRadius * 2f, frozenRadius * 2f, frozenRadius * 2f);
            ConfigureShaderBoundaries(frozenFundWave);

            GameObject frozenHarmWave = Instantiate(wavePrefab, transform.position, Quaternion.identity);
            frozenHarmWave.transform.SetParent(frozenFundWave.transform);
            frozenHarmWave.transform.localPosition = Vector3.zero;
            ConfigureShaderBoundaries(frozenHarmWave);

            float distanceProgress = Mathf.Clamp01(frozenRadius / roomFadeMaxDistance);
            float distanceDecay = Mathf.Clamp01(1.0f - distanceProgress);
            float fundAmpModifier = 0.0f;
            float harmAmpModifier = 0.0f;

            if (csvFundamentalRMS.Count > 0)
            {
                int index = Mathf.FloorToInt(distanceProgress * (csvFundamentalRMS.Count - 1));
                index = Mathf.Clamp(index, 0, csvFundamentalRMS.Count - 1);
                fundAmpModifier = csvFundamentalRMS[index] / maxFundRMS;
                harmAmpModifier = csvHarmonicRMS[index] / maxHarmRMS;
            }

            float physicalShellGap = 1.005f + (harmAmpModifier * 0.05f);
            frozenHarmWave.transform.localScale = Vector3.one * physicalShellGap;

            Renderer fundRenderer = frozenFundWave.GetComponent<Renderer>();
            if (fundRenderer != null)
            {
                fundRenderer.material.EnableKeyword("_EMISSION");
                Color c = baseFundamentalColor;
                c.a = fundamentalAlpha * distanceDecay;
                fundRenderer.material.color = c;
                fundRenderer.material.SetColor("_EmissionColor", baseFundamentalColor * fundAmpModifier * maxGlowIntensity * distanceDecay);
            }

            Renderer harmRenderer = frozenHarmWave.GetComponent<Renderer>();
            if (harmRenderer != null)
            {
                harmRenderer.material.EnableKeyword("_EMISSION");
                Color c = baseHarmonicColor;
                c.a = harmonicAlpha * distanceDecay;
                harmRenderer.material.color = c;
                harmRenderer.material.SetColor("_EmissionColor", baseHarmonicColor * harmAmpModifier * maxGlowIntensity * distanceDecay);
            }

            activeWaves.Add(frozenFundWave);
        }
    }

    private void ConfigureShaderBoundaries(GameObject waveTarget)
    {
        Renderer r = waveTarget.GetComponent<Renderer>();
        if (r != null)
        {
            r.material.SetVector("_ChamberMin", new Vector4(chamberMin.x, chamberMin.y, chamberMin.z, 0f));
            r.material.SetVector("_ChamberMax", new Vector4(chamberMax.x, chamberMax.y, chamberMax.z, 0f));
        }
    }

    private float CalculateMaxCornerDistance()
    {
        Vector3[] corners = new Vector3[]
        {
            new Vector3(chamberMin.x, chamberMin.y, chamberMin.z),
            new Vector3(chamberMax.x, chamberMin.y, chamberMin.z),
            new Vector3(chamberMin.x, chamberMax.y, chamberMin.z),
            new Vector3(chamberMax.x, chamberMax.y, chamberMin.z),
            new Vector3(chamberMin.x, chamberMin.y, chamberMax.z),
            new Vector3(chamberMax.x, chamberMin.y, chamberMax.z),
            new Vector3(chamberMin.x, chamberMax.y, chamberMax.z),
            new Vector3(chamberMax.x, chamberMax.y, chamberMax.z)
        };

        float maxDist = 0f;
        foreach (Vector3 corner in corners)
        {
            float dist = Vector3.Distance(transform.position, corner);
            if (dist > maxDist) maxDist = dist;
        }
        return maxDist;
    }

    void AutomatePipelineDiscovery() 
    {
        string streamingPath = Application.streamingAssetsPath;
        string[] discoveredFiles = Directory.GetFiles(streamingPath, "*.csv");
        
        if (discoveredFiles.Length == 0) 
        {
            Debug.LogError("❌ No telemetry CSV datasets found in StreamingAssets!");
            return;
        }

        System.Array.Sort(discoveredFiles, (a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
        string targetCSVPath = discoveredFiles[0];
        
        string baseFileName = Path.GetFileNameWithoutExtension(targetCSVPath);
        Debug.Log("🤖 Dynamic Pipeline Target: " + baseFileName);

        string[] dataLines = File.ReadAllLines(targetCSVPath);
        if (dataLines.Length <= 1) return;

        string[] firstRow = dataLines[1].Split(',');
        if (firstRow.Length >= 5)
        {
            int.TryParse(firstRow[4], out targetFrequency);
        }

        for (int i = 1; i < dataLines.Length; i++) 
        {
            string[] rowData = dataLines[i].Split(',');
            if (rowData.Length >= 5) 
            {
                float fundRMS = float.Parse(rowData[2]);
                float harmRMS = float.Parse(rowData[3]);

                csvRadii.Add(float.Parse(rowData[1]));
                csvFundamentalRMS.Add(fundRMS);
                csvHarmonicRMS.Add(harmRMS);

                if (fundRMS > maxFundRMS) maxFundRMS = fundRMS;
                if (harmRMS > maxHarmRMS) maxHarmRMS = harmRMS;
            }
        }

        dataLoaded = true;

        string wavName = baseFileName + ".wav"; 
        string fullWavPath = Path.Combine(streamingPath, wavName);

        if (File.Exists(fullWavPath))
        {
            StartCoroutine(LoadAndPlayAudio(fullWavPath));
        }
        else
        {
            Debug.LogError($"❌ Audio Pipeline Error: Missing matching audio file {wavName} inside StreamingAssets!");
        }
    }

    System.Collections.IEnumerator LoadAndPlayAudio(string path) 
    {
        using (UnityWebRequest multimediaRequest = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.WAV)) 
        {
            yield return multimediaRequest.SendWebRequest();
            if (multimediaRequest.result == UnityWebRequest.Result.Success) {
                audioSource.clip = DownloadHandlerAudioClip.GetContent(multimediaRequest);
                audioSource.loop = true;
                audioSource.Play();
            }
            else
            {
                Debug.LogError("❌ Unity Audio Engine failed to read file path: " + path);
            }
        }
    }

    void UpdateUIScreen()
    {
        if (uiTextDisplay == null) return;

        string frequencyText = dataLoaded ? $"{targetFrequency} Hz" : "Loading...";
        string fundText = dataLoaded ? liveFundamentalRMS.ToString("F5") : "0.00000";
        string harmText = dataLoaded ? liveHarmonicRMS.ToString("F5") : "0.00000";

        uiTextDisplay.text = $"<b>NIST CHAMBER TELEMETRY</b>\n" +
                             $"Simulation Mode: {currentMode}\n" +
                             $"Fundamental Frequency: {frequencyText}\n" +
                             $"Live Fundamental Energy: {fundText} RMS\n" +
                             $"Live Harmonic Energy: {harmText} RMS";
    }
}