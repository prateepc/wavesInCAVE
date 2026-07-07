using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;

[RequireComponent(typeof(AudioSource))]
public class AdvancedWaveManager : MonoBehaviour 
{
    public enum SimulationMode { Continuous, FreezeFrame }

    [Header("Simulation Mode Settings")]
    [Tooltip("Press SPACE during runtime to toggle between Continuous and Freeze Frame modes.")]
    public SimulationMode currentMode = SimulationMode.Continuous;

    [Header("UI Display Links")]
    public TextMeshProUGUI uiTextDisplay;
    
    [Header("Dynamic Intensity Legend UI Overlay")]
    public RawImage legendColorBar;
    public TextMeshProUGUI MinIntensityLabel;          
    public TextMeshProUGUI QuarterIntensityLabel;      
    public TextMeshProUGUI MidIntensityLabel;          
    public TextMeshProUGUI ThreeQuarterIntensityLabel;  
    public TextMeshProUGUI MaxIntensityLabel;          

    [Header("Hover Tooltip UI Elements")]
    public GameObject tooltipPanel;
    public TextMeshProUGUI tooltipText;
    public Transform caveWandPointer;

    [Header("Wave Prefab Link")]
    [Tooltip("The base sphere prefab used for all visual wave shells.")]
    public GameObject wavePrefab;

    [Header("Visual Tuning Layout")]
    public float maxGlowIntensity = 5.0f;
    [Range(0f, 1f)] public float waveOpacity = 0.60f;

    [Header("Simulation Timing")]
    private float waveGenerationInterval = 0.05f; 

    [Header("Acoustic Physics Settings")]
    private float simulationSpeedMultiplier = 1.0f; 
    private int targetFrequency = 440;
    
    private AudioSource audioSource;
    private List<GameObject> activeWaves = new List<GameObject>();
    
    private List<float> csvRadii = new List<float>();
    private List<float> csvFundamentalRMS = new List<float>();
    private List<int> csvFrequencies = new List<int>(); 
    
    private float waveTimer = 0f;
    private bool dataLoaded = false;
    private float maxDistanceToFurthestCorner = 0f;
    
    private float maxFundRMS = 0.0001f; 
    private float liveFundamentalRMS = 0f;
    private int currentCSVIndex = 0;

    private Vector3 chamberMin = new Vector3(-3.35f, 0.00f, -5.00f); 
    private Vector3 chamberMax = new Vector3(3.35f, 6.70f, 5.00f);  

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        maxDistanceToFurthestCorner = CalculateMaxCornerDistance();

        if (uiTextDisplay == null)
        {
            GameObject foundUIObject = GameObject.Find("TelemetryDisplay");
            if (foundUIObject != null) uiTextDisplay = foundUIObject.GetComponent<TextMeshProUGUI>();
        }

        if (tooltipPanel != null) tooltipPanel.SetActive(false);

        AutomatePipelineDiscovery();
        StartCoroutine(InitializeLegendUI());
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space)) ToggleSimulationMode();

        UpdateUIScreen(); 

        if (!dataLoaded) return; 

        if (currentMode == SimulationMode.Continuous)
        {
            waveTimer += Time.deltaTime;
            if (waveTimer >= waveGenerationInterval) 
            {
                EmitSequentialPhysicalWave();
                waveTimer = 0f;
            }
            ProcessWavePhysics();
        }

        HandleWaveInterrogation();
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
            currentCSVIndex = 0;
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

    System.Collections.IEnumerator InitializeLegendUI()
    {
        while (!dataLoaded)
        {
            yield return null;
        }
        
        yield return new WaitForEndOfFrame();

        if (legendColorBar != null)
        {
            Texture2D texture = new Texture2D(1, 512);
            texture.wrapMode = TextureWrapMode.Clamp;

            for (int y = 0; y < 512; y++)
            {
                float progress = (float)y / 511f;
                Color pixelColor = Color.Lerp(new Color(0.1f, 0.0f, 0.3f), Color.red, progress);
                if (progress > 0.75f) pixelColor = Color.Lerp(Color.red, Color.yellow, (progress - 0.75f) * 4f);
                texture.SetPixel(0, y, pixelColor);
            }
            texture.Apply();
            legendColorBar.texture = texture;
        }

        if (MinIntensityLabel != null) MinIntensityLabel.text = "0.00000 RMS";
        if (QuarterIntensityLabel != null) QuarterIntensityLabel.text = $"{(maxFundRMS * 0.25f):F5} RMS";
        if (MidIntensityLabel != null) MidIntensityLabel.text = $"{(maxFundRMS * 0.50f):F5} RMS";
        if (ThreeQuarterIntensityLabel != null) ThreeQuarterIntensityLabel.text = $"{(maxFundRMS * 0.75f):F5} RMS";
        if (MaxIntensityLabel != null) MaxIntensityLabel.text = $"{maxFundRMS:F5} RMS";
    }

    void EmitSequentialPhysicalWave() 
    {
        if (wavePrefab == null || csvFundamentalRMS.Count == 0) return;
        if (currentCSVIndex >= csvFundamentalRMS.Count) currentCSVIndex = 0; 

        float targetRMS = csvFundamentalRMS[currentCSVIndex];
        int targetFreq = csvFrequencies[currentCSVIndex];

        GameObject waveShell = Instantiate(wavePrefab, transform.position, Quaternion.identity);
        waveShell.name = $"Acoustic_Shell_Idx_{currentCSVIndex}";
        waveShell.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
        ConfigureShaderBoundaries(waveShell);

        WaveDataIdentifier identifier = waveShell.AddComponent<WaveDataIdentifier>();
        identifier.waveFrequency = targetFreq;
        identifier.harmonicOrder = $"Physical Wave Profile";

        liveFundamentalRMS = targetRMS;

        activeWaves.Add(waveShell);
        currentCSVIndex++;
    }

    void ProcessWavePhysics() 
    {
        List<GameObject> deadWaves = new List<GameObject>();
        float roomFadeMaxDistance = chamberMax.z - chamberMin.z;

        for (int i = 0; i < activeWaves.Count; i++) 
        {
            GameObject wave = activeWaves[i];
            if (wave == null) continue;

            float currentRadius = wave.transform.localScale.x / 2f;
            currentRadius += 343f * Time.deltaTime * simulationSpeedMultiplier;
            wave.transform.localScale = new Vector3(currentRadius * 2f, currentRadius * 2f, currentRadius * 2f);

            float distanceProgress = Mathf.Clamp01(currentRadius / roomFadeMaxDistance);
            float distanceDecay = Mathf.Clamp01(1.0f - distanceProgress);

            int dataIndex = Mathf.Clamp(Mathf.FloorToInt(distanceProgress * (csvFundamentalRMS.Count - 1)), 0, csvFundamentalRMS.Count - 1);
            float localRMS = csvFundamentalRMS[dataIndex];
            float normalizedIntensity = Mathf.Clamp01(localRMS / maxFundRMS);

            Renderer waveRenderer = wave.GetComponent<Renderer>();
            if (waveRenderer != null)
            {
                waveRenderer.material.EnableKeyword("_EMISSION");
                Color physicalColor = Color.Lerp(new Color(0.1f, 0.0f, 0.3f), Color.red, normalizedIntensity);
                if (normalizedIntensity > 0.75f) physicalColor = Color.Lerp(Color.red, Color.yellow, (normalizedIntensity - 0.75f) * 4f);
                
                physicalColor.a = waveOpacity * distanceDecay; 
                waveRenderer.material.color = physicalColor;
                waveRenderer.material.SetColor("_EmissionColor", physicalColor * normalizedIntensity * maxGlowIntensity * distanceDecay);
            }

            if (currentRadius >= maxDistanceToFurthestCorner) deadWaves.Add(wave);
        }

        foreach (GameObject expiredWave in deadWaves) 
        {
            activeWaves.Remove(expiredWave);
            Destroy(expiredWave);
        }
    }

    void EmitFreezeFrameWaves()
    {
        if (wavePrefab == null || csvFundamentalRMS.Count == 0) return;

        float roomFadeMaxDistance = chamberMax.z - chamberMin.z;
        float totalTimeForMaxDepth = roomFadeMaxDistance / 343f;
        
        int totalSlices = 40;
        float timeStep = totalTimeForMaxDepth / totalSlices;

        for (int i = 1; i <= totalSlices; i++)
        {
            float targetTime = i * timeStep;
            float frozenRadius = 343f * targetTime; 

            GameObject frozenWave = Instantiate(wavePrefab, transform.position, Quaternion.identity);
            frozenWave.name = $"Frozen_Pressure_Shell_{i}";
            frozenWave.transform.localScale = new Vector3(frozenRadius * 2f, frozenRadius * 2f, frozenRadius * 2f);
            ConfigureShaderBoundaries(frozenWave);

            Collider c = frozenWave.GetComponent<Collider>();
            if (c != null) c.enabled = false;

            float distanceProgress = Mathf.Clamp01(frozenRadius / roomFadeMaxDistance);
            float distanceDecay = Mathf.Clamp01(1.0f - distanceProgress);
            
            int dataIndex = Mathf.Clamp(Mathf.FloorToInt(distanceProgress * (csvFundamentalRMS.Count - 1)), 0, csvFundamentalRMS.Count - 1);
            float localRMS = csvFundamentalRMS[dataIndex];
            float normalizedIntensity = Mathf.Clamp01(localRMS / maxFundRMS);
            int calculatedFreezeFrequency = csvFrequencies[dataIndex];

            WaveDataIdentifier identifier = frozenWave.AddComponent<WaveDataIdentifier>();
            identifier.waveFrequency = calculatedFreezeFrequency;
            identifier.harmonicOrder = $"Pressure Slice [Energy: {localRMS:F5} RMS]";

            Renderer waveRenderer = frozenWave.GetComponent<Renderer>();
            if (waveRenderer != null)
            {
                waveRenderer.material.EnableKeyword("_EMISSION");
                Color physicalColor = Color.Lerp(new Color(0.1f, 0.0f, 0.3f), Color.red, normalizedIntensity);
                if (normalizedIntensity > 0.75f) physicalColor = Color.Lerp(Color.red, Color.yellow, (normalizedIntensity - 0.75f) * 4f);
                
                physicalColor.a = waveOpacity * distanceDecay;
                waveRenderer.material.color = physicalColor;
                waveRenderer.material.SetColor("_EmissionColor", physicalColor * normalizedIntensity * maxGlowIntensity * distanceDecay);
            }

            activeWaves.Add(frozenWave);
        }
    }

    void HandleWaveInterrogation()
    {
        if (tooltipPanel == null || tooltipText == null) return;

        // --- FIXED STATIONARY FREEZE FRAME HUD OVERLAY ---
        if (currentMode == SimulationMode.FreezeFrame)
        {
            tooltipPanel.SetActive(true);
            
            RectTransform panelRect = tooltipPanel.GetComponent<RectTransform>();
            if (panelRect != null)
            {
                panelRect.anchorMin = new Vector2(1f, 0f);
                panelRect.anchorMax = new Vector2(1f, 0f);
                panelRect.pivot = new Vector2(1f, 0f);
                panelRect.anchoredPosition = new Vector2(-40f, 40f);
            }
            
            tooltipText.text = $"<b>FREEZE FRAME SUMMARY</b>\n" +
                               $"Analyzed Slices: {activeWaves.Count}\n" +
                               $"Peak Capture: {maxFundRMS:F5} RMS\n" +
                               $"Target Pitch: {targetFrequency} Hz";
            return;
        }

        // --- CONTINUOUS MODE RAYCAST DETECTOR ---
        Ray ray = (caveWandPointer != null) 
            ? new Ray(caveWandPointer.position, caveWandPointer.forward) 
            : Camera.main.ScreenPointToRay(Input.mousePosition);

        RaycastHit hit;
        
        if (Physics.Raycast(ray, out hit, 100f, Physics.AllLayers, QueryTriggerInteraction.Collide))
        {
            WaveDataIdentifier targetedWave = hit.collider.GetComponent<WaveDataIdentifier>();

            if (targetedWave != null && targetedWave.waveFrequency > 0)
            {
                tooltipPanel.SetActive(true);
                
                RectTransform panelRect = tooltipPanel.GetComponent<RectTransform>();
                if (panelRect != null)
                {
                    panelRect.anchorMin = new Vector2(0f, 0f);
                    panelRect.anchorMax = new Vector2(0f, 0f);
                    panelRect.pivot = new Vector2(0f, 0f);
                }

                tooltipPanel.transform.position = (caveWandPointer != null) 
                    ? Camera.main.WorldToScreenPoint(hit.point) 
                    : Input.mousePosition + new Vector3(20f, 20f, 0f);

                float internalHitRadius = Vector3.Distance(transform.position, hit.point);
                float roomFadeMaxDistance = chamberMax.z - chamberMin.z;
                float progress = Mathf.Clamp01(internalHitRadius / roomFadeMaxDistance);
                int dataIndex = Mathf.Clamp(Mathf.FloorToInt(progress * (csvFundamentalRMS.Count - 1)), 0, csvFundamentalRMS.Count - 1);
                
                float intersectionRMS = csvFundamentalRMS[dataIndex];

                tooltipText.text = $"<b>{targetedWave.harmonicOrder}</b>\n" +
                                   $"Freq: {targetedWave.waveFrequency} Hz\n" +
                                   $"Energy: {intersectionRMS:F5} RMS\n" +
                                   $"Distance: {internalHitRadius:F2} m";
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
        if (!Directory.Exists(streamingPath)) return;
        
        string[] discoveredFiles = Directory.GetFiles(streamingPath, "*.csv");
        if (discoveredFiles.Length == 0) return;

        System.Array.Sort(discoveredFiles, (a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
        string targetCSVPath = discoveredFiles[0];
        string baseFileName = Path.GetFileNameWithoutExtension(targetCSVPath);

        string[] dataLines = File.ReadAllLines(targetCSVPath);
        if (dataLines.Length <= 1) return;

        string[] firstRow = dataLines[1].Split(',');
        if (firstRow.Length >= 5) int.TryParse(firstRow[4], out targetFrequency);

        for (int i = 1; i < dataLines.Length; i++) 
        {
            string[] rowData = dataLines[i].Split(',');
            if (rowData.Length >= 3) 
            {
                float fundRMS = float.Parse(rowData[2]);
                int parsedFreq = targetFrequency;

                if (rowData.Length >= 7) int.TryParse(rowData[6], out parsedFreq);

                csvRadii.Add(float.Parse(rowData[1]));
                csvFundamentalRMS.Add(fundRMS);
                csvFrequencies.Add(parsedFreq); 

                if (fundRMS > maxFundRMS) maxFundRMS = fundRMS;
            }
        }

        dataLoaded = true;
        string wavName = baseFileName + ".wav"; 
        string fullWavPath = Path.Combine(streamingPath, wavName);

        if (File.Exists(fullWavPath)) StartCoroutine(LoadAndPlayAudio(fullWavPath));
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

    void UpdateUIScreen()
    {
        if (uiTextDisplay == null) return;

        string frequencyText = dataLoaded ? $"{targetFrequency} Hz" : "Loading...";
        string fundText = dataLoaded ? liveFundamentalRMS.ToString("F5") : "0.00000";

        uiTextDisplay.text = $"<b>NIST CHAMBER TELEMETRY (RMS MODEL)</b>\n" +
                             $"Simulation Mode: {currentMode}\n" +
                             $"Tracked Core Pitch: {frequencyText}\n" +
                             $"Instantaneous Wave Energy: {fundText} RMS";
    }
}