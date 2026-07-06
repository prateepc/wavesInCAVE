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
    
    [Header("Dynamic Legend UI Overlay Links")]
    public RawImage legendColorBar;
    public TextMeshProUGUI minFreqLabel;
    public TextMeshProUGUI quarterFreqLabel; 
    public TextMeshProUGUI midFreqLabel;
    public TextMeshProUGUI threeQuarterFreqLabel; 
    public TextMeshProUGUI maxFreqLabel;

    [Header("Hover Tooltip UI Elements")]
    public GameObject tooltipPanel;
    public TextMeshProUGUI tooltipText;
    public Transform caveWandPointer;

    [Header("Wave Prefab Link")]
    [Tooltip("The base sphere prefab used for all visual wave shells.")]
    public GameObject wavePrefab;

    [Header("Visual Tuning Layout")]
    public float maxGlowIntensity = 4.0f;

    [Header("Constant Translucency Settings")]
    [Range(0f, 1f)] public float fundamentalAlpha = 0.50f;
    [Range(0f, 1f)] public float harmonicAlpha = 0.40f;

    [Header("Simulation Timing")]
    private float waveGenerationInterval = 2f;

    [Header("Acoustic Physics Settings")]
    private float simulationSpeedMultiplier = 0.001f;
    private int targetFrequency = 440;
    
    private AudioSource audioSource;
    private List<GameObject> activeWaves = new List<GameObject>();
    
    private List<float> csvRadii = new List<float>();
    private List<float> csvFundamentalRMS = new List<float>();
    private List<float> csvHarmonicRMS = new List<float>();
    private List<int> csvFrequencies = new List<int>(); 
    
    private float waveTimer = 0f;
    private bool dataLoaded = false;
    private float maxDistanceToFurthestCorner = 0f;
    
    private float maxFundRMS = 0.001f; 
    private float maxHarmRMS = 0.001f; 
    
    private float liveFundamentalRMS = 0f; 
    private float liveHarmonicRMS = 0f;

    private Vector3 chamberMin = new Vector3(-3.35f, 0.00f, -5.00f); 
    private Vector3 chamberMax = new Vector3(3.35f, 6.70f, 5.00f);  

    private const float pipelineMinFreq = 20f;
    private const float pipelineMaxFreq = 20000f;

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

        StartCoroutine(InitializeLegendUI());
        AutomatePipelineDiscovery();
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
                EmitCompoundWaveSnapshot();
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
        yield return new WaitForEndOfFrame();

        if (legendColorBar != null)
        {
            Texture2D texture = new Texture2D(1, 512);
            texture.wrapMode = TextureWrapMode.Clamp;

            for (int y = 0; y < 512; y++)
            {
                float progress = (float)y / 511f;
                Color pixelColor = Color.HSVToRGB(progress * 0.85f, 0.9f, 0.9f);
                texture.SetPixel(0, y, pixelColor);
            }
            texture.Apply();
            legendColorBar.texture = texture;
        }

        if (minFreqLabel != null) minFreqLabel.text = $"{pipelineMinFreq} Hz";
        if (maxFreqLabel != null) maxFreqLabel.text = $"{pipelineMaxFreq} Hz";

        float logMin = Mathf.Log10(pipelineMinFreq);     
        float logMax = Mathf.Log10(pipelineMaxFreq);     
        float logRange = logMax - logMin;

        if (quarterFreqLabel != null)
        {
            float logQ1 = logMin + logRange * 0.25f;
            quarterFreqLabel.text = $"{Mathf.RoundToInt(Mathf.Pow(10f, logQ1))} Hz";
        }

        if (midFreqLabel != null)
        {
            float logMid = logMin + logRange * 0.50f;
            midFreqLabel.text = $"{Mathf.RoundToInt(Mathf.Pow(10f, logMid))} Hz";
        }

        if (threeQuarterFreqLabel != null)
        {
            float logQ3 = logMin + logRange * 0.75f;
            threeQuarterFreqLabel.text = $"{Mathf.RoundToInt(Mathf.Pow(10f, logQ3))} Hz";
        }
    }

    void EmitCompoundWaveSnapshot() 
    {
        if (wavePrefab == null) return;

        // 1. Spawn Fundamental Wave
        GameObject fundamentalWave = Instantiate(wavePrefab, transform.position, Quaternion.identity);
        fundamentalWave.name = "Fundamental_1f";
        fundamentalWave.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
        ConfigureShaderBoundaries(fundamentalWave);
        
        // --- ADDED RIGHT HERE ---
        fundamentalWave.layer = 8; 

        WaveDataIdentifier id1 = fundamentalWave.AddComponent<WaveDataIdentifier>();
        id1.waveFrequency = targetFrequency;
        id1.harmonicOrder = "1f Fundamental";

        // 2. Your existing Harmonic Loop
        for (int order = 2; order <= 4; order++)
        {
            GameObject harmonicWave = Instantiate(wavePrefab, transform.position, Quaternion.identity);
            harmonicWave.name = $"Harmonic_{order}f";
            harmonicWave.transform.SetParent(fundamentalWave.transform);
            harmonicWave.transform.localPosition = Vector3.zero;
            harmonicWave.transform.localRotation = Quaternion.identity;
            harmonicWave.transform.localScale = Vector3.one * (1.0f + (order - 1) * 0.005f); 
            ConfigureShaderBoundaries(harmonicWave);

            // --- ADDED RIGHT HERE INSIDE THE EXISTING LOOP ---
            harmonicWave.layer = 9; 

            WaveDataIdentifier idH = harmonicWave.AddComponent<WaveDataIdentifier>();
            idH.waveFrequency = targetFrequency * order;
            idH.harmonicOrder = $"{order}f Harmonic";
        }

        activeWaves.Add(fundamentalWave);
    }

    void ProcessWavePhysics() 
    {
        List<GameObject> deadWaves = new List<GameObject>();
        float roomFadeMaxDistance = chamberMax.z - chamberMin.z;

        float trackingFundRMS = 0f;
        float trackingHarmRMS = 0f;

        for (int i = 0; i < activeWaves.Count; i++) 
        {
            GameObject fundWave = activeWaves[i];
            if (fundWave == null) continue;

            float currentRadius = fundWave.transform.localScale.x / 2f;
            currentRadius += 343f * Time.deltaTime * simulationSpeedMultiplier;
            fundWave.transform.localScale = new Vector3(currentRadius * 2f, currentRadius * 2f, currentRadius * 2f);

            float distanceProgress = Mathf.Clamp01(currentRadius / roomFadeMaxDistance);
            float distanceDecay = Mathf.Clamp01(1.0f - distanceProgress);

            float fundAmpModifier = 0.0f;
            float harmAmpModifier = 0.0f;
            int liveBaseFrequency = targetFrequency;

            if (csvFundamentalRMS.Count > 0)
            {
                int index = Mathf.FloorToInt(distanceProgress * (csvFundamentalRMS.Count - 1));
                index = Mathf.Clamp(index, 0, csvFundamentalRMS.Count - 1);

                trackingFundRMS = csvFundamentalRMS[index];
                trackingHarmRMS = csvHarmonicRMS[index];
                if (csvFrequencies.Count > index) liveBaseFrequency = csvFrequencies[index];

                fundAmpModifier = trackingFundRMS / maxFundRMS;
                harmAmpModifier = trackingHarmRMS / maxHarmRMS;
            }

            WaveDataIdentifier fundId = fundWave.GetComponent<WaveDataIdentifier>();
            if (fundId != null) fundId.waveFrequency = liveBaseFrequency;

            float logFreq = Mathf.Log10(liveBaseFrequency);
            float normFreq = Mathf.InverseLerp(Mathf.Log10(pipelineMinFreq), Mathf.Log10(pipelineMaxFreq), logFreq);
            Color baseFundamentalColor = Color.HSVToRGB(normFreq * 0.85f, 0.9f, 0.9f); 

            foreach (Transform child in fundWave.transform)
            {
                WaveDataIdentifier childId = child.GetComponent<WaveDataIdentifier>();
                int childOrderNum = 2;
                if (child.name.Contains("3f")) childOrderNum = 3;
                if (child.name.Contains("4f")) childOrderNum = 4;

                if (childId != null) childId.waveFrequency = liveBaseFrequency * childOrderNum;

                float physicalShellGap = 1.0f + ((childOrderNum - 1) * 0.005f) + (harmAmpModifier * 0.02f * (childOrderNum - 1));
                child.transform.localScale = Vector3.one * physicalShellGap;

                Renderer harmRenderer = child.GetComponent<Renderer>();
                if (harmRenderer != null)
                {
                    harmRenderer.material.EnableKeyword("_EMISSION");
                    Color harmColor = (childOrderNum == 2) ? Color.magenta : (childOrderNum == 3) ? Color.cyan : Color.yellow;
                    harmColor.a = harmonicAlpha * distanceDecay; 
                    harmRenderer.material.color = harmColor;
                    harmRenderer.material.SetColor("_EmissionColor", harmColor * harmAmpModifier * maxGlowIntensity * distanceDecay);
                }
            }

            Renderer fundRenderer = fundWave.GetComponent<Renderer>();
            if (fundRenderer != null)
            {
                fundRenderer.material.EnableKeyword("_EMISSION");
                Color fundColor = baseFundamentalColor;
                fundColor.a = fundamentalAlpha * distanceDecay; 
                fundRenderer.material.color = fundColor;
                fundRenderer.material.SetColor("_EmissionColor", baseFundamentalColor * fundAmpModifier * maxGlowIntensity * distanceDecay);
            }

            if (currentRadius >= maxDistanceToFurthestCorner) deadWaves.Add(fundWave);
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
        if (wavePrefab == null || csvFundamentalRMS.Count == 0) return;

        float roomFadeMaxDistance = chamberMax.z - chamberMin.z;
        float totalTimeForMaxDepth = roomFadeMaxDistance / 343f;
        float timeStep = totalTimeForMaxDepth / 10f;

        for (int i = 1; i <= 10; i++)
        {
            float targetTime = i * timeStep;
            float frozenRadius = 343f * targetTime; 

            // 1. Spawn Frozen Fundamental Wave
            GameObject frozenFundWave = Instantiate(wavePrefab, transform.position, Quaternion.identity);
            frozenFundWave.name = $"Fundamental_1f_FF_Set_{i}";
            frozenFundWave.transform.localScale = new Vector3(frozenRadius * 2f, frozenRadius * 2f, frozenRadius * 2f);
            ConfigureShaderBoundaries(frozenFundWave);

            // --- ADDED RIGHT HERE ---
            frozenFundWave.layer = 8; 

            float distanceProgress = Mathf.Clamp01(frozenRadius / roomFadeMaxDistance);
            float distanceDecay = Mathf.Clamp01(1.0f - distanceProgress);
            
            int dataIndex = Mathf.Clamp(Mathf.FloorToInt(distanceProgress * (csvFundamentalRMS.Count - 1)), 0, csvFundamentalRMS.Count - 1);
            float fundAmpModifier = csvFundamentalRMS[dataIndex] / maxFundRMS;
            float harmAmpModifier = csvHarmonicRMS[dataIndex] / maxHarmRMS;
            
            int calculatedFreezeFrequency = targetFrequency;
            if (csvFrequencies.Count > dataIndex) calculatedFreezeFrequency = csvFrequencies[dataIndex];

            WaveDataIdentifier fundId = frozenFundWave.AddComponent<WaveDataIdentifier>();
            fundId.waveFrequency = calculatedFreezeFrequency;
            fundId.harmonicOrder = "1f Fundamental";

            float logFreq = Mathf.Log10(calculatedFreezeFrequency);
            float normFreq = Mathf.InverseLerp(Mathf.Log10(pipelineMinFreq), Mathf.Log10(pipelineMaxFreq), logFreq);
            Color baseFundamentalColor = Color.HSVToRGB(normFreq * 0.85f, 0.9f, 0.9f);

            // 2. Your existing Frozen Harmonic Loop
            for (int order = 2; order <= 4; order++)
            {
                GameObject frozenHarmWave = Instantiate(wavePrefab, transform.position, Quaternion.identity);
                frozenHarmWave.name = $"Harmonic_{order}f_FF_Set_{i}";
                frozenHarmWave.transform.SetParent(frozenFundWave.transform);
                frozenHarmWave.transform.localPosition = Vector3.zero;
                frozenHarmWave.transform.localRotation = Quaternion.identity;
                ConfigureShaderBoundaries(frozenHarmWave);

                // --- ADDED RIGHT HERE INSIDE THE EXISTING LOOP ---
                frozenHarmWave.layer = 9; 

                WaveDataIdentifier harmId = frozenHarmWave.AddComponent<WaveDataIdentifier>();
                harmId.waveFrequency = calculatedFreezeFrequency * order;
                harmId.harmonicOrder = $"{order}f Harmonic";

                float frozenShellGap = 1.0f + ((order - 1) * 0.05f) + (harmAmpModifier * 0.02f * (order - 1));
                frozenHarmWave.transform.localScale = Vector3.one * frozenShellGap;

                Renderer harmRenderer = frozenHarmWave.GetComponent<Renderer>();
                if (harmRenderer != null)
                {
                    harmRenderer.material.EnableKeyword("_EMISSION");
                    Color c = (order == 2) ? Color.magenta : (order == 3) ? Color.cyan : Color.yellow;
                    c.a = harmonicAlpha * distanceDecay;
                    harmRenderer.material.color = c;
                    harmRenderer.material.SetColor("_EmissionColor", c * harmAmpModifier * maxGlowIntensity * distanceDecay);
                }
            }

            Renderer fundRenderer = frozenFundWave.GetComponent<Renderer>();
            if (fundRenderer != null)
            {
                fundRenderer.material.EnableKeyword("_EMISSION");
                Color c = baseFundamentalColor;
                c.a = fundamentalAlpha * distanceDecay;
                fundRenderer.material.color = c;
                fundRenderer.material.SetColor("_EmissionColor", baseFundamentalColor * fundAmpModifier * maxGlowIntensity * distanceDecay);
            }

            activeWaves.Add(frozenFundWave);
        }
    }

    void HandleWaveInterrogation()
    {
        if (tooltipPanel == null || tooltipText == null) return;

        Ray ray = (caveWandPointer != null) 
            ? new Ray(caveWandPointer.position, caveWandPointer.forward) 
            : Camera.main.ScreenPointToRay(Input.mousePosition);

        RaycastHit hit;
        
        // --- ADD LAYER MASK FILTERING ---
        int layerMask;
        if (Input.GetKey(KeyCode.LeftShift))
        {
            // Only detect objects on Layer 9 (HarmonicWave) when Shift is held
            layerMask = 1 << 9; 
        }
        else
        {
            // Only detect objects on Layer 8 (FundamentalWave) by default
            layerMask = 1 << 8; 
        }

        // Fire the physics raycast using our targeted layerMask
        if (Physics.Raycast(ray, out hit, 100f, layerMask, QueryTriggerInteraction.Collide))
        {
            WaveDataIdentifier targetedWave = hit.collider.GetComponent<WaveDataIdentifier>();

            if (targetedWave != null && targetedWave.waveFrequency > 0)
            {
                tooltipPanel.SetActive(true);
                
                if (caveWandPointer != null)
                {
                    tooltipPanel.transform.position = Camera.main.WorldToScreenPoint(hit.point);
                }
                else
                {
                    tooltipPanel.transform.position = Input.mousePosition + new Vector3(20f, 20f, 0f);
                }

                tooltipText.text = $"<b>{targetedWave.harmonicOrder}</b>\nFreq: {targetedWave.waveFrequency} Hz";
                return; 
            }
        }

        // Hide the box immediately if pointing into blank space or a non-targeted wave layer
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
            if (rowData.Length >= 5) 
            {
                float fundRMS = float.Parse(rowData[2]);
                float harmRMS = float.Parse(rowData[3]);
                int parsedFreq = targetFrequency;

                if(rowData.Length >= 7) int.TryParse(rowData[6], out parsedFreq);
                else int.TryParse(rowData[4], out parsedFreq);

                csvRadii.Add(float.Parse(rowData[1]));
                csvFundamentalRMS.Add(fundRMS);
                csvHarmonicRMS.Add(harmRMS);
                csvFrequencies.Add(parsedFreq); 

                if (fundRMS > maxFundRMS) maxFundRMS = fundRMS;
                if (harmRMS > maxHarmRMS) maxHarmRMS = harmRMS;
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
        string harmText = dataLoaded ? liveHarmonicRMS.ToString("F5") : "0.00000";

        uiTextDisplay.text = $"<b>NIST CHAMBER TELEMETRY</b>\n" +
                             $"Simulation Mode: {currentMode}\n" +
                             $"Fundamental Frequency: {frequencyText}\n" +
                             $"Live Fundamental Energy: {fundText} RMS\n" +
                             $"Live Harmonic Energy: {harmText} RMS";
    }
}