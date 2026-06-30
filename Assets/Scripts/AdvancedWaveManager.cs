using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[RequireComponent(typeof(AudioSource))]
public class AdvancedWaveManager : MonoBehaviour
{
    [Header("Wave Prefab Link")]
    public GameObject wavePrefab; 
    
    [Header("Simulation Timing")]
    [Tooltip("Time in seconds between each frozen snapshot layer creation.")]
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

    // NIST Fixed Rectangular Internal Bounds (Half-extents from Center Origin)
    private float boundX = 3.35f;  // Total Width = 6.7m
    private float boundY = 3.35f;  // Total Height = 6.7m
    private float boundZ = 5.00f;  // Total Length = 10.0m

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        AutomatePipelineDiscovery();
    }

    void Update()
    {
        if (!dataLoaded) return;

        waveTimer += Time.deltaTime;

        // Task 6: Spawn sequential frozen outward-traveling snapshots
        if (waveTimer >= waveGenerationInterval)
        {
            EmitWaveSnapshot();
            waveTimer = 0f;
        }

        ProcessWavePhysics();
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
                csvRadii.Add(float.Parse(rowData[1])); 
                csvRMS.Add(float.Parse(rowData[2]));   
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
            if (multimediaRequest.result == UnityWebRequest.Result.Success)
            {
                audioSource.clip = DownloadHandlerAudioClip.GetContent(multimediaRequest);
                audioSource.loop = true;
                audioSource.Play();
            }
        }
    }

    void EmitWaveSnapshot()
    {
        if (wavePrefab == null) return;

        // Spawn a fresh snapshot layer at our point source origin center
        GameObject newWave = Instantiate(wavePrefab, transform.position, Quaternion.identity);
        newWave.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
        
        // Logarithmic color mapping spectrum calculations (20Hz - 20kHz)
        float logFreq = Mathf.Log10(targetFrequency);
        float normFreq = Mathf.InverseLerp(Mathf.Log10(20f), Mathf.Log10(20000f), logFreq);
        Color baseFreqColor = Color.HSVToRGB(normFreq * 0.85f, 0.9f, 0.9f); 
        
        newWave.GetComponent<Renderer>().material.color = baseFreqColor;
        activeWaves.Add(newWave);
    }

    void ProcessWavePhysics()
    {
        List<GameObject> deadWaves = new List<GameObject>();

        for (int i = 0; i < activeWaves.Count; i++)
        {
            GameObject wave = activeWaves[i];
            if (wave == null) continue;

            // Expand scale outward continuously using human-readable velocity metrics
            float currentRadius = wave.transform.localScale.x / 2f;
            currentRadius += 343f * Time.deltaTime * simulationSpeedMultiplier; 
            
            wave.transform.localScale = new Vector3(currentRadius * 2f, currentRadius * 2f, currentRadius * 2f);

            // Compute Inverse Square Law energy loss updates (Shade shifts darker & clears Alpha)
            Renderer waveRenderer = wave.GetComponent<Renderer>();
            Color currentColor = waveRenderer.material.color;
            
            // Energy drops off exponentially over expanding radius distances
            float energyDecayFactor = Mathf.Clamp01(1f / (currentRadius * currentRadius + 0.3f));
            
            // Smoothly shift current tracking alpha down to fully transparent
            currentColor.a = energyDecayFactor * 0.4f; 
            
            // Shift color spectrum hue darker toward black over distance
            waveRenderer.material.color = Color.Lerp(currentColor, Color.black, currentRadius / boundZ);

            // Task 8: Accurate Rectangular Chamber Wall Absorption Bounds Checking
            // Checks if the sphere boundary edge breaches any flat planar coordinate wall line
            if (currentRadius >= boundX || currentRadius >= boundY || currentRadius >= boundZ)
            {
                deadWaves.Add(wave);
            }
        }

        // Clean boundary-violating frozen slices safely out of active GPU runtime memory
        foreach (GameObject expiredWave in deadWaves)
        {
            activeWaves.Remove(expiredWave);
            Destroy(expiredWave);
        }
    }
}

