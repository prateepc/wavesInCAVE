using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

[RequireComponent(typeof(AudioSource))]
public class AdvancedWaveManager : MonoBehaviour 
{
    // New Enum to define simulation states
    public enum SimulationMode { Continuous, FreezeFrame }

    [Header("Simulation Mode Settings")]
    [Tooltip("Press SPACE during runtime to toggle between Continuous and Freeze Frame modes.")]
    public SimulationMode currentMode = SimulationMode.Continuous;

    [Header("UI Display Link")]
    public TextMeshProUGUI uiTextDisplay;

    [Header("Wave Prefab Link")]
    public GameObject wavePrefab;

    [Header("Simulation Timing")]
    private float waveGenerationInterval = 2f;

    [Header("Acoustic Physics Settings")]
    private float simulationSpeedMultiplier = 0.001f;
    private int targetFrequency = 440;
    
    private AudioSource audioSource;
    private List<GameObject> activeWaves = new List<GameObject>();
    private List<float> csvRadii = new List<float>();
    private List<float> csvRMS = new List<float>();
    
    private float waveTimer = 0f;
    private bool dataLoaded = false;
    private float maxDistanceToFurthestCorner = 0f;
    private float maxCSV_RMS = 0.001f; 
    private float liveTrackingRMS = 0f; 

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
        // Simple runtime action toggle: Press Spacebar to swap modes
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ToggleSimulationMode();
        }

        UpdateUIScreen(); 

        if (!dataLoaded) return; 

        // Physics behavior splits based on selected mode
        if (currentMode == SimulationMode.Continuous)
        {
            waveTimer += Time.deltaTime;
            if (waveTimer >= waveGenerationInterval) 
            {
                EmitWaveSnapshot();
                waveTimer = 0f;
            }
            ProcessWavePhysics();
        }
    }

    /// <summary>
    /// Swaps the mode at runtime and handles clearing out or generating old waves.
    /// </summary>
    public void ToggleSimulationMode()
    {
        // Clear old waves to cleanly transition states
        ClearActiveWaves();

        if (currentMode == SimulationMode.Continuous)
        {
            currentMode = SimulationMode.FreezeFrame;
            // Generate the 10 static frozen shells instantly
            EmitFreezeFrameWaves();
        }
        else
        {
            currentMode = SimulationMode.Continuous;
            waveTimer = 0f; // Reset continuous timer
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
    /// Spawns 10 static wave frames frozen at distances calculated from speed of sound.
    /// </summary>
    void EmitFreezeFrameWaves()
    {
        if (wavePrefab == null) return;

        // Base color calculation
        float logFreq = Mathf.Log10(targetFrequency);
        float normFreq = Mathf.InverseLerp(Mathf.Log10(20f), Mathf.Log10(20000f), logFreq);
        Color pristineBaseColor = Color.HSVToRGB(normFreq * 0.85f, 0.9f, 0.9f);
        float roomFadeMaxDistance = chamberMax.z - chamberMin.z;

        // Time step intervals representing a wave moving out at 343 m/s.
        // We divide max room depth into 10 steps.
        float totalTimeForMaxDepth = roomFadeMaxDistance / 343f;
        float timeStep = totalTimeForMaxDepth / 10f;

        for (int i = 1; i <= 10; i++)
        {
            // Calculate frozen snapshot physical radius 
            float targetTime = i * timeStep;
            float frozenRadius = 343f * targetTime; 

            // Instantiate and size the shell instantly
            GameObject frozenWave = Instantiate(wavePrefab, transform.position, Quaternion.identity);
            frozenWave.transform.localScale = new Vector3(frozenRadius * 2f, frozenRadius * 2f, frozenRadius * 2f);

            // Apply static color and telemetry calculation once
            Renderer waveRenderer = frozenWave.GetComponent<Renderer>();
            if (waveRenderer != null)
            {
                Material waveMat = waveRenderer.material; 
                waveMat.SetVector("_ChamberMin", new Vector4(chamberMin.x, chamberMin.y, chamberMin.z, 0f));
                waveMat.SetVector("_ChamberMax", new Vector4(chamberMax.x, chamberMax.y, chamberMax.z, 0f));

                float distanceProgress = Mathf.Clamp01(frozenRadius / roomFadeMaxDistance);
                float amplitudeModifier = 1.0f;

                if (csvRMS.Count > 0)
                {
                    int telemetryIndex = Mathf.FloorToInt(distanceProgress * (csvRMS.Count - 1));
                    telemetryIndex = Mathf.Clamp(telemetryIndex, 0, csvRMS.Count - 1);
                    amplitudeModifier = csvRMS[telemetryIndex] / maxCSV_RMS;
                }

                Color runtimeColor = pristineBaseColor;
                float distanceDecay = Mathf.Clamp01(1.0f - distanceProgress);
                runtimeColor.a = amplitudeModifier * distanceDecay * 0.65f;

                Color targetFadeColor = new Color(0.0f, 0.0f, 0.0f, runtimeColor.a);
                float decayBlendFactor = Mathf.Clamp01((1.0f - amplitudeModifier) + (distanceProgress * 0.2f));
                
                waveRenderer.material.color = Color.Lerp(runtimeColor, targetFadeColor, decayBlendFactor);
            }

            activeWaves.Add(frozenWave);
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
        string[] discoveredFiles = Directory.GetFiles(streamingPath, "telemetry_*Hz.csv");
        
        if (discoveredFiles.Length == 0) 
        {
            Debug.LogError("❌ No telemetry CSV datasets found! Please run your Python orchestrator script first.");
            return;
        }

        System.Array.Sort(discoveredFiles, (a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
        string targetCSVPath = discoveredFiles[0];
        
        string csvFileName = Path.GetFileName(targetCSVPath);
        string cleanName = csvFileName.Replace("telemetry_", "").Replace("Hz.csv", "");
        
        if (int.TryParse(cleanName, out int parsedFreq)) 
        {
            targetFrequency = parsedFreq;
        }

        string[] dataLines = File.ReadAllLines(targetCSVPath);
        for (int i = 1; i < dataLines.Length; i++) 
        {
            string[] rowData = dataLines[i].Split(',');
            if (rowData.Length >= 3) 
            {
                float radius = float.Parse(rowData[1]);
                float rms = float.Parse(rowData[2]);

                csvRadii.Add(radius);
                csvRMS.Add(rms);

                if (rms > maxCSV_RMS)
                {
                    maxCSV_RMS = rms;
                }
            }
        }

        dataLoaded = true;
        string wavName = "tone_" + targetFrequency + "Hz.wav";
        string wavPath = Path.Combine(streamingPath, wavName);
        StartCoroutine(LoadAndPlayAudio(wavPath));
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
        }
    }

    void EmitWaveSnapshot() 
    {
        if (wavePrefab == null) return;

        GameObject newWave = Instantiate(wavePrefab, transform.position, Quaternion.identity);
        newWave.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);

        Renderer waveRenderer = newWave.GetComponent<Renderer>();
        if (waveRenderer != null)
        {
            Material waveMat = waveRenderer.material; 
            waveMat.SetVector("_ChamberMin", new Vector4(chamberMin.x, chamberMin.y, chamberMin.z, 0f));
            waveMat.SetVector("_ChamberMax", new Vector4(chamberMax.x, chamberMax.y, chamberMax.z, 0f));
        }

        activeWaves.Add(newWave);
    }

    void ProcessWavePhysics() 
    {
        List<GameObject> deadWaves = new List<GameObject>();

        float logFreq = Mathf.Log10(targetFrequency);
        float normFreq = Mathf.InverseLerp(Mathf.Log10(20f), Mathf.Log10(20000f), logFreq);
        Color pristineBaseColor = Color.HSVToRGB(normFreq * 0.85f, 0.9f, 0.9f);

        float sampleRMS = 0f;

        for (int i = 0; i < activeWaves.Count; i++) 
        {
            GameObject wave = activeWaves[i];
            if (wave == null) continue;

            float currentRadius = wave.transform.localScale.x / 2f;
            currentRadius += 343f * Time.deltaTime * simulationSpeedMultiplier;
            wave.transform.localScale = new Vector3(currentRadius * 2f, currentRadius * 2f, currentRadius * 2f);

            Renderer waveRenderer = wave.GetComponent<Renderer>();
            if (waveRenderer != null)
            {
                float roomFadeMaxDistance = chamberMax.z - chamberMin.z;
                float distanceProgress = Mathf.Clamp01(currentRadius / roomFadeMaxDistance);
                float amplitudeModifier = 1.0f;

                if (csvRMS.Count > 0)
                {
                    int telemetryIndex = Mathf.FloorToInt(distanceProgress * (csvRMS.Count - 1));
                    telemetryIndex = Mathf.Clamp(telemetryIndex, 0, csvRMS.Count - 1);
                    sampleRMS = csvRMS[telemetryIndex];
                    amplitudeModifier = sampleRMS / maxCSV_RMS;
                }

                Color runtimeColor = pristineBaseColor;
                float distanceDecay = Mathf.Clamp01(1.0f - distanceProgress);
                runtimeColor.a = amplitudeModifier * distanceDecay * 0.65f;

                Color targetFadeColor = new Color(0.0f, 0.0f, 0.0f, runtimeColor.a);
                float decayBlendFactor = Mathf.Clamp01((1.0f - amplitudeModifier) + (distanceProgress * 0.2f));
                waveRenderer.material.color = Color.Lerp(runtimeColor, targetFadeColor, decayBlendFactor);
            }

            if (currentRadius >= maxDistanceToFurthestCorner) 
            {
                deadWaves.Add(wave);
            }
        }

        if (activeWaves.Count > 0)
        {
            liveTrackingRMS = sampleRMS;
        }
        else
        {
            liveTrackingRMS = 0f;
        }

        foreach (GameObject expiredWave in deadWaves) 
        {
            activeWaves.Remove(expiredWave);
            Destroy(expiredWave);
        }
    }

    void UpdateUIScreen()
    {
        if (uiTextDisplay == null) return;

        string frequencyText = dataLoaded ? $"{targetFrequency} Hz" : "Loading CSV...";
        string amplitudeText = dataLoaded ? liveTrackingRMS.ToString("F5") : "0.00000";
        int activeShellsCount = activeWaves != null ? activeWaves.Count : 0;

        uiTextDisplay.text = $"<b>NIST CHAMBER TELEMETRY</b>\n" +
                             $"Simulation Mode: {currentMode}\n" +
                             $"Target Frequency: {frequencyText}\n" +
                             $"Live Front Amplitude: {amplitudeText} RMS\n" +
                             $"Active Wave Shells: {activeShellsCount}";
    }
}