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
    public float waveGenerationInterval = 0.15f; 

    private int targetFrequency = 440; 
    private AudioSource audioSource;
    private List<GameObject> activeWaves = new List<GameObject>();
    private List<float> csvRadii = new List<float>();
    private List<float> csvRMS = new List<float>();
    
    private float waveTimer = 0f;
    private bool dataLoaded = false;
    private float maxChamberRadius = 5.0f; 

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        AutomatePipelineDiscovery();
    }

    void Update()
    {
        if (!dataLoaded) return;

        waveTimer += Time.deltaTime;

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
            Debug.Log("🤖 Automation Engine: Detected newest frequency configuration -> " + targetFrequency + "Hz");
        }
        else
        {
            Debug.LogError("❌ Couldn't parse frequency from file name: " + csvFileName);
            return;
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
            else
            {
                Debug.LogError("❌ Audio pipeline initialization failed: " + multimediaRequest.error);
            }
        }
    }

    void EmitWaveSnapshot()
    {
        if (wavePrefab == null) return;

        GameObject newWave = Instantiate(wavePrefab, transform.position, Quaternion.identity);
        newWave.transform.localScale = Vector3.zero;
        
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

            float currentRadius = wave.transform.localScale.x / 2f;
            currentRadius += 343f * Time.deltaTime * 0.005f; 

            float waveDensityFactor = 1f + (currentRadius * 0.15f);
            wave.transform.localScale = new Vector3(currentRadius * 2, currentRadius * 2, currentRadius * 2) * waveDensityFactor;

            Renderer waveRenderer = wave.GetComponent<Renderer>();
            Color currentColor = waveRenderer.material.color;
            
            float energyAlpha = Mathf.Clamp01(1f / (currentRadius * currentRadius + 0.5f));
            currentColor.a = energyAlpha * 0.5f;
            
            waveRenderer.material.color = Color.Lerp(currentColor, Color.black, currentRadius / maxChamberRadius);

            if (currentRadius >= maxChamberRadius)
            {
                deadWaves.Add(wave);
            }
        }

        foreach (GameObject expiredWave in deadWaves)
        {
            activeWaves.Remove(expiredWave);
            Destroy(expiredWave);
        }
    }
}

