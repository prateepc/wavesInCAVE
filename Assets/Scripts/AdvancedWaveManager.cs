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
    [Tooltip("The base sphere prefab used for all nested acoustic wavefront visualizations.")]
    public GameObject wavePrefab;

    [Header("Visual Tuning Layout")]
    [Tooltip("Controls how intensely loud sounds glow within the CAVE environment.")]
    public float maxGlowIntensity = 5.0f;

    [Header("Constant Translucency Settings")]
    [Range(0f, 1f)] public float fundamentalAlpha = 0.85f;
    [Range(0f, 1f)] public float harmonicAlpha = 0.60f;

    [Header("Simulation Timing")]
    [SerializeField] private float waveGenerationInterval = 1.5f;

    [Header("Acoustic Physics Settings")]
    [SerializeField] private float simulationSpeedMultiplier = 1.0f; 

    private AudioSource audioSource;
    private List<GameObject> activeWaves = new List<GameObject>();

    // Zero-allocation parallel data arrays from Python pipeline
    private List<float> csvTimestamps = new List<float>();
    private List<float> csvRadii = new List<float>();
    private List<int> csvPrincipalFreqs = new List<int>();
    private List<float> csvP0Pressures = new List<float>();
    private List<float> csvH1Pressures = new List<float>();
    private List<float> csvH2Pressures = new List<float>();
    private List<float> csvH3Pressures = new List<float>();

    private float waveTimer = 0f;
    private bool dataLoaded = false;
    private float maxDistanceToFurthestCorner = 0f;

    // Peak tracking for dynamic visual normalization
    private float maxP0 = 0.001f;
    private float maxH1 = 0.001f;
    private float maxH2 = 0.001f;
    private float maxH3 = 0.001f;

    // Live runtime tracking metrics for UI Canvas
    private int liveFrequency = 0;
    private float liveP0 = 0f;
    private float liveH1 = 0f;
    private float liveH2 = 0f;
    private float liveH3 = 0f;

    // Hardcoded Absolute Structural Boundaries based on your precise 3D model list
    private Vector4 chamberMin = new Vector4(-3.35f, 0.00f, -5.00f, 0f);
    private Vector4 chamberMax = new Vector4(3.35f, 6.70f, 5.00f, 0f);

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        maxDistanceToFurthestCorner = CalculateMaxCornerDistance();

        if (uiTextDisplay == null)
        {
            GameObject foundUIObject = GameObject.Find("TelemetryDisplay");
            if (foundUIObject != null) uiTextDisplay = foundUIObject.GetComponent<TextMeshProUGUI>();
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
    /// Spawns an isotropic fundamental root wavefront containing 3 independent nested harmonic shells.
    /// </summary>
    void EmitCompoundWaveSnapshot()
    {
        if (wavePrefab == null) return;

        // 1. Spawn Core Principal Wavefront (f0 Root)
        GameObject fundamentalRoot = Instantiate(wavePrefab, transform.position, Quaternion.identity);
        fundamentalRoot.name = "Acoustic_Root_f0";
        fundamentalRoot.transform.localScale = Vector3.one * 0.01f;
        ConfigureShaderBoundaries(fundamentalRoot);

        // 2. Spawn 3 Concentric Internal Harmonics
        string[] harmonicNames = { "Harmonic_Shell_2f0", "Harmonic_Shell_3f0", "Harmonic_Shell_4f0" };
        for (int i = 0; i < 3; i++)
        {
            GameObject harmonicChild = Instantiate(wavePrefab, transform.position, Quaternion.identity);
            harmonicChild.name = harmonicNames[i];
            harmonicChild.transform.SetParent(fundamentalRoot.transform);
            harmonicChild.transform.localPosition = Vector3.zero;
            harmonicChild.transform.localRotation = Quaternion.identity;
            
            // Offset scales slightly so shells don't clip visually on top of each other
            harmonicChild.transform.localScale = Vector3.one * (0.98f - (i * 0.05f));
            ConfigureShaderBoundaries(harmonicChild);
        }

        activeWaves.Add(fundamentalRoot);
    }
    void ProcessWavePhysics()
    {
        List<GameObject> deadWaves = new List<GameObject>();
        float roomFadeMaxDistance = chamberMax.z - chamberMin.z;

        for (int i = 0; i < activeWaves.Count; i++)
        {
            GameObject rootWave = activeWaves[i];
            if (rootWave == null) continue;

            // Expand radius isotropically using speed of sound equations
            float currentRadius = rootWave.transform.localScale.x / 2f;
            currentRadius += 343f * Time.deltaTime * simulationSpeedMultiplier;
            rootWave.transform.localScale = Vector3.one * (currentRadius * 2f);

            float distanceProgress = Mathf.Clamp01(currentRadius / roomFadeMaxDistance);
            float distanceDecay = Mathf.Clamp01(1.0f - distanceProgress);

            // Isolate matching data array indices based on spatial position history
            if (csvTimestamps.Count > 0)
            {
                int idx = Mathf.FloorToInt(distanceProgress * (csvTimestamps.Count - 1));
                idx = Mathf.Clamp(idx, 0, csvTimestamps.Count - 1);

                liveFrequency = csvPrincipalFreqs[idx];
                liveP0 = csvP0Pressures[idx];
                liveH1 = csvH1Pressures[idx];
                liveH2 = csvH2Pressures[idx];
                liveH3 = csvH3Pressures[idx];

                // Normalize pressures across recorded local maximum boundaries
                float normP0 = liveP0 / maxP0;
                float normH1 = liveH1 / maxH1;
                float normH2 = liveH2 / maxH2;
                float normH3 = liveH3 / maxH3;

                // Color shifts driven directly by tracking real-time frequency changes
                float logFreq = Mathf.Log10(Mathf.Clamp(liveFrequency, 20, 20000));
                float normFreq = Mathf.InverseLerp(Mathf.Log10(20f), Mathf.Log10(2000f), logFreq);
                Color cP0 = Color.HSVToRGB(normFreq * 0.85f, 0.9f, 0.9f);
                Color cH1 = Color.cyan;
                Color cH2 = Color.magenta;
                Color cH3 = Color.red;

                // Fire rendering states directly to Root and Nested Child Renderers
                UpdateShellRenderer(rootWave, cP0, normP0, fundamentalAlpha, distanceDecay, new Vector4(liveP0, liveH1, liveH2, liveH3));
                
                if (rootWave.transform.childCount >= 3)
                {
                    UpdateShellRenderer(rootWave.transform.GetChild(0).gameObject, cH1, normH1, harmonicAlpha, distanceDecay, new Vector4(liveP0, liveH1, liveH2, liveH3));
                    UpdateShellRenderer(rootWave.transform.GetChild(1).gameObject, cH2, normH2, harmonicAlpha, distanceDecay, new Vector4(liveP0, liveH1, liveH2, liveH3));
                    UpdateShellRenderer(rootWave.transform.GetChild(2).gameObject, cH3, normH3, harmonicAlpha, distanceDecay, new Vector4(liveP0, liveH1, liveH2, liveH3));
                }
            }

            if (currentRadius >= maxDistanceToFurthestCorner)
            {
                deadWaves.Add(rootWave);
            }
        }

        // Clean up structures that expanded out past CAVE architecture
        foreach (GameObject expiredWave in deadWaves)
        {
            activeWaves.Remove(expiredWave);
            Destroy(expiredWave);
        }
    }
    private void UpdateShellRenderer(GameObject targetObj, Color baseColor, float normPressure, float baseAlpha, float decay, Vector4 combinedPressures)
    {
        Renderer r = targetObj.GetComponent<Renderer>();
        if (r == null) return;

        r.material.EnableKeyword("_EMISSION");
        
        baseColor.a = baseAlpha * decay;
        r.material.color = baseColor;

        // Drive dynamic HDR visual glow using Sound Pressure values
        Color dynamicEmission = baseColor * normPressure * maxGlowIntensity * decay;
        r.material.SetColor("_EmissionColor", dynamicEmission);

        // Package all sound pressures into a vector pass to maximize custom GPU pipeline flexibility
        r.material.SetVector("_AcousticPressures", combinedPressures);
    }

    void EmitFreezeFrameWaves()
    {
        if (wavePrefab == null || csvTimestamps.Count == 0) return;

        float roomFadeMaxDistance = chamberMax.z - chamberMin.z;
        int totalSnapshots = 8; // Number of static spatial iterations inside the CAVE

        for (int i = 1; i <= totalSnapshots; i++)
        {
            float targetProgress = (float)i / totalSnapshots;
            float frozenRadius = targetProgress * roomFadeMaxDistance;

            int idx = Mathf.FloorToInt(targetProgress * (csvTimestamps.Count - 1));
            idx = Mathf.Clamp(idx, 0, csvTimestamps.Count - 1);

            GameObject fRoot = Instantiate(wavePrefab, transform.position, Quaternion.identity);
            fRoot.name = $"Frozen_Root_Snapshot_{i}";
            fRoot.transform.localScale = Vector3.one * (frozenRadius * 2f);
            ConfigureShaderBoundaries(fRoot);

            Color cP0 = Color.HSVToRGB(Mathf.InverseLerp(40, 2000, csvPrincipalFreqs[idx]) * 0.85f, 0.9f, 0.9f);
            Vector4 pressures = new Vector4(csvP0Pressures[idx], csvH1Pressures[idx], csvH2Pressures[idx], csvH3Pressures[idx]);
            
            UpdateShellRenderer(fRoot, cP0, csvP0Pressures[idx] / maxP0, fundamentalAlpha, 1.0f - targetProgress, pressures);

            // Generate spatial internal nested nodes for snapshot review
            Color[] hColors = { Color.cyan, Color.magenta, Color.red };
            float[] hPressures = { csvH1Pressures[idx], csvH2Pressures[idx], csvH3Pressures[idx] };
            float[] maxHvals = { maxH1, maxH2, maxH3 };

            for (int h = 0; h < 3; h++)
            {
                GameObject hChild = Instantiate(wavePrefab, transform.position, Quaternion.identity);
                hChild.transform.SetParent(fRoot.transform);
                hChild.transform.localPosition = Vector3.zero;
                hChild.transform.localScale = Vector3.one * (0.98f - (h * 0.05f));
                ConfigureShaderBoundaries(hChild);

                UpdateShellRenderer(hChild, hColors[h], hPressures[h] / maxHvals[h], harmonicAlpha, 1.0f - targetProgress, pressures);
            }

            activeWaves.Add(fRoot);
        }
    }
    private void ConfigureShaderBoundaries(GameObject waveTarget)
    {
        Renderer r = waveTarget.GetComponent<Renderer>();
        if (r != null)
        {
            r.material.SetVector("_ChamberMin", chamberMin);
            r.material.SetVector("_ChamberMax", chamberMax);
        }
    }

    private float CalculateMaxCornerDistance()
    {
        Vector3[] corners = new Vector3[] {
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
        if (!Directory.Exists(streamingPath))
        {
            Debug.LogError("❌ StreamingAssets folder missing entirely from project root!");
            return;
        }

        string[] discoveredFiles = Directory.GetFiles(streamingPath, "*.csv");
        if (discoveredFiles.Length == 0)
        {
            Debug.LogError("❌ No telemetry CSV datasets found in StreamingAssets!");
            return;
        }

        System.Array.Sort(discoveredFiles, (a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
        string targetCSVPath = discoveredFiles[0];
        
        Debug.Log("🤖 CAVE Pipeline Target Initialized: " + Path.GetFileName(targetCSVPath));

        string[] dataLines = File.ReadAllLines(targetCSVPath);
        if (dataLines.Length <= 1) return;

        // Skip headers (i=0) and parse raw parameters into performance arrays
        for (int i = 1; i < dataLines.Length; i++)
        {
            string[] rowData = dataLines[i].Split(',');
            if (rowData.Length >= 7)
            {
                float t = float.Parse(rowData[0]);
                float r = float.Parse(rowData[1]);
                int f0 = int.Parse(rowData[2]);
                float p0 = float.Parse(rowData[3]);
                float h1 = float.Parse(rowData[4]);
                float h2 = float.Parse(rowData[5]);
                float h3 = float.Parse(rowData[6]);

                csvTimestamps.Add(t);
                csvRadii.Add(r);
                csvPrincipalFreqs.Add(f0);
                csvP0Pressures.Add(p0);
                csvH1Pressures.Add(h1);
                csvH2Pressures.Add(h2);
                csvH3Pressures.Add(h3);

                // Track historical ceiling peaks for clean visual scaling factors
                if (p0 > maxP0) maxP0 = p0;
                if (h1 > maxH1) maxH1 = h1;
                if (h2 > maxH2) maxH2 = h2;
                if (h3 > maxH3) maxH3 = h3;
            }
        }

        dataLoaded = true;

        string wavName = Path.GetFileNameWithoutExtension(targetCSVPath) + ".wav";
        string fullWavPath = Path.Combine(streamingPath, wavName);
        if (File.Exists(fullWavPath)) StartCoroutine(LoadAndPlayAudio(fullWavPath));
    }

    System.Collections.IEnumerator LoadAndPlayAudio(string path)
    {
        using (UnityWebRequest multimediaRequest = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.WAV))
        {
            yield return multimediaRequest.SendWebRequest();
            if (multimediaRequest.result == UnityWebRequest.Result.Success)
            {
                audioSource.clip = DownloadHandlerAudioClip.GetContent(multimediaRequest);
                audioSource.loop = true;
                audioSource.Play();
            }
        }
    }

    void UpdateUIScreen()
    {
        if (uiTextDisplay == null) return;

        if (!dataLoaded)
        {
            uiTextDisplay.text = "<b>CAVE AKUSTIK TELEMETRY</b>\nStatus: Loading Pipeline Datasets...";
            return;
        }

        uiTextDisplay.text = $"<b>CAVE AKUSTIK TELEMETRY</b>\n" +
                             $"Mode: <color=#FFFF00>{currentMode}</color>\n" +
                             $"Principal Freq (f0): {liveFrequency} Hz\n" +
                             $"P0 Pressure: {liveP0.ToString("F4")} Pa\n" +
                             $"H1 Pressure (2f): {liveH1.ToString("F4")} Pa\n" +
                             $"H2 Pressure (3f): {liveH2.ToString("F4")} Pa\n" +
                             $"H3 Pressure (4f): {liveH3.ToString("F4")} Pa";
    }
}

