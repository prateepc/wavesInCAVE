using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro; // Crucial library addition for TextMeshPro support

[RequireComponent(typeof(AudioSource))]
public class AdvancedWaveManager : MonoBehaviour 
{
    [Header("UI Display Link")]
    [Tooltip("Drag your TelemetryDisplay UI Text object into this slot via the Unity Inspector.")]
    public TextMeshProUGUI uiTextDisplay;

    [Header("Wave Prefab Link")]
    public GameObject wavePrefab;

    [Header("Simulation Timing")]
    private float waveGenerationInterval = 0.75f;

    [Header("Acoustic Physics Settings")]
    [Tooltip("Slowing down velocity so human eyes can track wave propagation in the CAVE.")]
    private float simulationSpeedMultiplier = 0.001f;
    private int targetFrequency = 440;
    
    private AudioSource audioSource;
    private List<GameObject> activeWaves = new List<GameObject>();
    private List<float> csvRadii = new List<float>();
    private List<float> csvRMS = new List<float>();
    
    private float waveTimer = 0f;
    private bool dataLoaded = false;
    private float maxDistanceToFurthestCorner = 0f;
    private float maxCSV_RMS = 0.001f; // Safeguard against division by zero
    private float liveTrackingRMS = 0f; // Stores the current real-time RMS for the UI

    // Hardcoded Absolute Structural Boundaries based on your precise 3D model list
    private Vector3 chamberMin = new Vector3(-3.35f, 0.00f, -5.00f); // Floor Y=0, Left X=-3.35, Front Z=-5
    private Vector3 chamberMax = new Vector3(3.35f, 6.70f, 5.00f);  // Ceiling Y=6.7, Right X=3.35, Back Z=5

void Start() 
{
    audioSource = GetComponent<AudioSource>();
    
    // Calculate maximum travel distance from your point source (0, 1.6, -3.35) to the furthest room corner
    maxDistanceToFurthestCorner = CalculateMaxCornerDistance();

    // AUTO-LINK PIPELINE FIX: 
    // If the inspector slot is empty, the script searches the active scene for "TelemetryDisplay"
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
    // FIX: Move the UI text block here so it renders even while data is initializing!
    UpdateUIScreen(); 

    if (!dataLoaded) return; // Physics loop waits here for the CSV, but the text box will now update!

    waveTimer += Time.deltaTime;
    
    if (waveTimer >= waveGenerationInterval) 
    {
        EmitWaveSnapshot();
        waveTimer = 0f;
    }

    ProcessWavePhysics();
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
            Debug.Log("🤖 Automation Engine: Operating at -> " + targetFrequency + "Hz");
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

                // Track highest recorded RMS peak value to establish normal bounds
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
            // Pass absolute structural limits down to the shader layout
            waveMat.SetVector("_ChamberMin", new Vector4(chamberMin.x, chamberMin.y, chamberMin.z, 0f));
            waveMat.SetVector("_ChamberMax", new Vector4(chamberMax.x, chamberMax.y, chamberMax.z, 0f));
        }

        activeWaves.Add(newWave);
    }

void ProcessWavePhysics() 
{
    List<GameObject> deadWaves = new List<GameObject>();

    // Generate base frequency target color cleanly outside the loop
    float logFreq = Mathf.Log10(targetFrequency);
    float normFreq = Mathf.InverseLerp(Mathf.Log10(20f), Mathf.Log10(20000f), logFreq);
    Color pristineBaseColor = Color.HSVToRGB(normFreq * 0.85f, 0.9f, 0.9f);

    // Tracker variable to send the active wave data up to the text display
    float sampleRMS = 0f;

    for (int i = 0; i < activeWaves.Count; i++) 
    {
        GameObject wave = activeWaves[i];
        if (wave == null) continue;

        // Expand scale outward continuously using human-readable velocity metrics
        float currentRadius = wave.transform.localScale.x / 2f;
        currentRadius += 343f * Time.deltaTime * simulationSpeedMultiplier;
        wave.transform.localScale = new Vector3(currentRadius * 2f, currentRadius * 2f, currentRadius * 2f);

        Renderer waveRenderer = wave.GetComponent<Renderer>();
        if (waveRenderer != null)
        {
            // FIX: Map progress against the total structural depth of the chamber (boundZ * 2 = 10m)
            // This allows the full length of your Python telemetry dataset to play out over the room distance.
            float roomFadeMaxDistance = chamberMax.z - chamberMin.z;
            float distanceProgress = Mathf.Clamp01(currentRadius / roomFadeMaxDistance);

            // DEFAULT TELEMETRY FALLBACK VALUES
            float amplitudeModifier = 1.0f;

            // DATA INTEGRATION LINK:
            // Find matching index in our telemetry array based on expanding radius progress
            if (csvRMS.Count > 0)
            {
                int telemetryIndex = Mathf.FloorToInt(distanceProgress * (csvRMS.Count - 1));
                telemetryIndex = Mathf.Clamp(telemetryIndex, 0, csvRMS.Count - 1);

                // Track active loop index to display on UI overlay text screen
                sampleRMS = csvRMS[telemetryIndex];

                // Normalize amplitude against peak data threshold (0.0 to 1.0 scale range)
                amplitudeModifier = sampleRMS / maxCSV_RMS;
            }

            // Apply telemetry values directly to properties
            Color runtimeColor = pristineBaseColor;

            // PHYSICAL TUNING: 
            // Forces the transparency to continuously increase over distance.
            // Opacity is driven by raw CSV amplitude modulated by a continuous distance decay factor.
            float distanceDecay = Mathf.Clamp01(1.0f - distanceProgress);
            runtimeColor.a = amplitudeModifier * distanceDecay * 0.65f; // Boosted baseline multiplier

            // Brightness decays organically into black based on telemetry level drops
            Color targetFadeColor = new Color(0.0f, 0.0f, 0.0f, runtimeColor.a);

            // Combines physical distance decay with raw telemetry modulation
            float decayBlendFactor = Mathf.Clamp01((1.0f - amplitudeModifier) + (distanceProgress * 0.2f));
            waveRenderer.material.color = Color.Lerp(runtimeColor, targetFadeColor, decayBlendFactor);
        }

        // Remove tracking container safely when it expands past the furthest possible corner point
        if (currentRadius >= maxDistanceToFurthestCorner) 
        {
            deadWaves.Add(wave);
        }
    }

    // Pipe the latest live data value up to the tracking text interface loop
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
    // Safety check to ensure the UI Text slot isn't empty
    if (uiTextDisplay == null) return;

    // Direct initialization safeties
    string frequencyText = dataLoaded ? $"{targetFrequency} Hz" : "Loading CSV...";
    string amplitudeText = dataLoaded ? liveTrackingRMS.ToString("F5") : "0.00000";
    int activeShellsCount = activeWaves != null ? activeWaves.Count : 0;

    // Update screen canvas
    uiTextDisplay.text = $"<b>NIST CHAMBER TELEMETRY</b>\n" +
                         $"Target Frequency: {frequencyText}\n" +
                         $"Live Front Amplitude: {amplitudeText} RMS\n" +
                         $"Active Wave Shells: {activeShellsCount}";
}

}

