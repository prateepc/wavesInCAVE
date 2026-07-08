using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
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

    [Header("FFT Customization Variables")]
    [Tooltip("Must be a power of 2 (e.g., 1024, 2048, 4096, 8192, 16384)")]
    public int fftSize = 16384;
    [Tooltip("Maximum samples to read from the WAV file for overall RMS/Peak processing.")]
    public int analysisMaxSamplesWindow = 131072;
    [Tooltip("Minimum frequency bounds to check for a pitch spike (Hz).")]
    public float minFrequencyBound = 30f;
    [Tooltip("Maximum frequency bounds to check for a pitch spike (Hz).")]
    public float maxFrequencyBound = 4000f;
    [Tooltip("The index strategy offset window size checked around a calculated harmonic target bin (+/- standard index range).")]
    public int harmonicSearchBinOffset = 2;
    [Tooltip("The minimum fractional amplitude ratio required relative to the base 1f peak to validate a harmonic overtone.")]
    public float harmonicThresholdRatio = 0.05f;

    [Header("Scene Plot Configurations")]
    public Transform fftPlotAnchor;
    public Transform timePlotAnchor;
    public float plotWidth = 5f;
    public float plotHeight = 2f;
    public Color fftPlotColor = Color.cyan;
    public Color timePlotColor = Color.yellow;
    public float fftPlotMaxFrequency = 2000f;
    public int timePlotCyclesToShow = 5;

    [Header("Acoustic Layer Color Matrices")]
    [Tooltip("Divergent Color Maps (Low, Zero, High) for Fundamental, 2f, 3f, and 4f layers.")]
    public BipolarSpectrumPair[] harmonicColorPalettes = new BipolarSpectrumPair[] {
        new BipolarSpectrumPair { lowPressureTrough = Color.blue, zeroPressureEquilibrium = Color.white, highPressureCrest = Color.red }, 
        new BipolarSpectrumPair { lowPressureTrough = new Color(0f,0.2f,0f), zeroPressureEquilibrium = Color.white, highPressureCrest = Color.green }, 
        new BipolarSpectrumPair { lowPressureTrough = new Color(0f,0.1f,0.3f), zeroPressureEquilibrium = Color.white, highPressureCrest = Color.cyan }, 
        new BipolarSpectrumPair { lowPressureTrough = new Color(0.2f,0f,0.2f), zeroPressureEquilibrium = Color.white, highPressureCrest = Color.magenta } 
    };

    private AudioSource audioSource;
    private List<GameObject> activeWaves = new List<GameObject>();
    private LineRenderer fftLineRenderer;
    private LineRenderer timeLineRenderer;

    // Core extracted analytical variables
    private float principalFrequency = 343f;
    private float calculatedRMS = 0f;
    private float peakPressure = 0f;
    private float[] harmonicAmplitudes = new float[] { 0f, 0f, 0f, 0f };
    private bool analysisComplete = false;
    private Vector3 chamberMin = new Vector3(-3.35f, 0.00f, -5.00f);
    private Vector3 chamberMax = new Vector3(3.35f, 6.70f, 5.00f);
    private const float SPEED_OF_SOUND = 343.0f;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        InitializeGraphRenderers();
    }

    void Start() 
    {
        if (uiTextDisplay == null) 
        {
            GameObject foundUIObject = GameObject.Find("TelemetryDisplay");
            if (foundUIObject != null) uiTextDisplay = foundUIObject.GetComponent<TextMeshProUGUI>();
        }
        if (tooltipPanel != null) tooltipPanel.SetActive(false);
        
        DiscoverAndAnalyzeLatestWav();
    }

    void Update() 
    {
        UpdateUIScreen();
        HandleWaveInterrogation();
    }

    void InitializeGraphRenderers()
    {
        // 1. Setup the FFT graph line object
        if (fftPlotAnchor != null)
        {
            GameObject fftObj = new GameObject("FFT_Spectrum_Line");
            fftObj.transform.SetParent(fftPlotAnchor, false);
            fftLineRenderer = fftObj.AddComponent<LineRenderer>();
            ConfigureGraphStyle(fftLineRenderer, fftPlotColor);
        }

        // 2. Setup the Time-Domain graph line object
        if (timePlotAnchor != null)
        {
            GameObject timeObj = new GameObject("Time_Domain_Line");
            timeObj.transform.SetParent(timePlotAnchor, false);
            timeLineRenderer = timeObj.AddComponent<LineRenderer>();
            ConfigureGraphStyle(timeLineRenderer, timePlotColor);
        }
    }

    void ConfigureGraphStyle(LineRenderer lr, Color c)
    {
        lr.useWorldSpace = false;
        lr.startWidth = 0.03f;
        lr.endWidth = 0.03f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = c;
        lr.endColor = c;
        lr.positionCount = 0;
    }

    // =====================================================================
    // PLOT SPECTRAL LINES & TIME CONTINUUM INSIDE THE SCENE (NEW)
    // =====================================================================
    void PlotFftSpectrumData(double[] real, double[] imag, float sampleRate)
    {
        if (fftLineRenderer == null || fftPlotAnchor == null) return;

        float binWidth = sampleRate / fftSize;
        int maxPlotBin = Mathf.CeilToInt(fftPlotMaxFrequency / binWidth);
        maxPlotBin = Mathf.Min(maxPlotBin, fftSize / 2);

        // Find highest magnitude for visual Y normalization
        float maxMagnitude = 0.001f;
        float[] magnitudes = new float[maxPlotBin];
        for (int k = 0; k < maxPlotBin; k++)
        {
            magnitudes[k] = Mathf.Sqrt((float)(real[k] * real[k] + imag[k] * imag[k]));
            if (magnitudes[k] > maxMagnitude) maxMagnitude = magnitudes[k];
        }

        fftLineRenderer.positionCount = maxPlotBin;
        for (int k = 0; k < maxPlotBin; k++)
        {
            float progressX = (float)k / (maxPlotBin - 1);
            float progressY = magnitudes[k] / maxMagnitude;

            float xPos = progressX * plotWidth;
            float yPos = progressY * plotHeight;

            fftLineRenderer.SetPosition(k, new Vector3(xPos, yPos, 0f));
        }
    }

    void PlotTimeDomainData(float[] samples, float sampleRate, float pitchFrequency)
    {
        if (timeLineRenderer == null || timePlotAnchor == null || pitchFrequency <= 0.01f) return;

        // Mimics Python's zoom window setup limits
        float oneCycleDuration = 1f / pitchFrequency;
        float zoomDuration = oneCycleDuration * timePlotCyclesToShow;
        int zoomSamplesLimit = Mathf.CeilToInt(sampleRate * zoomDuration);
        zoomSamplesLimit = Mathf.Min(zoomSamplesLimit, samples.Length);

        // Track max peak deviation for dynamic vertical auto-scaling alignment
        float localMaxPeak = 0.001f;
        for (int i = 0; i < zoomSamplesLimit; i++)
        {
            if (Mathf.Abs(samples[i]) > localMaxPeak) localMaxPeak = Mathf.Abs(samples[i]);
        }

        timeLineRenderer.positionCount = zoomSamplesLimit;
        for (int i = 0; i < zoomSamplesLimit; i++)
        {
            float progressX = (float)i / (zoomSamplesLimit - 1);
            // Translate range from [-localMaxPeak, +localMaxPeak] smoothly to [0.0, 1.0]
            float normalizedValue = (samples[i] / localMaxPeak + 1f) * 0.5f;

            float xPos = progressX * plotWidth;
            float yPos = normalizedValue * plotHeight;

            timeLineRenderer.SetPosition(i, new Vector3(xPos, yPos, 0f));
        }
    }
    void DiscoverAndAnalyzeLatestWav() 
    {
        string streamingPath = Application.streamingAssetsPath;
        if (!Directory.Exists(streamingPath)) return;

        string[] discoveredFiles = Directory.GetFiles(streamingPath, "*.wav");
        if (discoveredFiles.Length == 0) return;

        System.Array.Sort(discoveredFiles, (a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
        string targetWavPath = discoveredFiles[0];
        
        StartCoroutine(LoadAndAnalyzeAudioPipeline(targetWavPath));
    }
    System.Collections.IEnumerator LoadAndAnalyzeAudioPipeline(string path) 
    {
        string formattedUrl = new System.Uri(path).AbsoluteUri;
        using (UnityWebRequest multimediaRequest = UnityWebRequestMultimedia.GetAudioClip(formattedUrl, AudioType.WAV)) 
        {
            yield return multimediaRequest.SendWebRequest();
            if (multimediaRequest.result != UnityWebRequest.Result.Success) yield break;

            AudioClip clip = DownloadHandlerAudioClip.GetContent(multimediaRequest);
            audioSource.clip = clip;
            audioSource.loop = true;
            audioSource.Play();

            float[] totalSamples = new float[clip.samples * clip.channels];
            clip.GetData(totalSamples, 0);

            float sumOfSquares = 0f;
            float maxPeak = 0f;
            int analysisLength = Mathf.Min(totalSamples.Length, analysisMaxSamplesWindow);

            for (int i = 0; i < analysisLength; i++) 
            {
                float sampleValue = totalSamples[i];
                sumOfSquares += sampleValue * sampleValue;
                if (Mathf.Abs(sampleValue) > maxPeak) maxPeak = Mathf.Abs(sampleValue);
            }
            calculatedRMS = Mathf.Sqrt(sumOfSquares / analysisLength);
            peakPressure = maxPeak;

            double[] realBuffer = new double[fftSize];
            double[] imagBuffer = new double[fftSize];
            float sampleRate = clip.frequency;

            for (int i = 0; i < fftSize; i++) 
            {
                if (i < totalSamples.Length)
                {
                    float hanningWindow = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / (fftSize - 1)));
                    realBuffer[i] = totalSamples[i] * hanningWindow;
                }
                else
                {
                    realBuffer[i] = 0.0;
                }
                imagBuffer[i] = 0.0;
            }

            ExecuteInPlaceCooleyTukeyFFT(realBuffer, imagBuffer, fftSize);
            float binWidth = sampleRate / fftSize;
            float highestMagnitude = 0f;
            int peakBinIndex = 0;

            int minBin = Mathf.CeilToInt(minFrequencyBound / binWidth);
            int maxBin = Mathf.FloorToInt(maxFrequencyBound / binWidth);
            maxBin = Mathf.Min(maxBin, fftSize / 2);

            for (int k = minBin; k < maxBin; k++) 
            {
                float magnitude = Mathf.Sqrt((float)(realBuffer[k] * realBuffer[k] + imagBuffer[k] * imagBuffer[k]));
                if (magnitude > highestMagnitude) 
                {
                    highestMagnitude = magnitude;
                    peakBinIndex = k;
                }
            }

            principalFrequency = peakBinIndex * binWidth;
            harmonicAmplitudes[0] = highestMagnitude; 

            for (int h = 2; h <= 4; h++) 
            {
                float targetHarmonicFreq = principalFrequency * h;
                int targetBin = Mathf.RoundToInt(targetHarmonicFreq / binWidth);
                float bestHarmonicMag = 0f;

                for (int offset = -harmonicSearchBinOffset; offset <= harmonicSearchBinOffset; offset++) 
                {
                    int currentCheckBin = targetBin + offset;
                    if (currentCheckBin > 0 && currentCheckBin < fftSize / 2) 
                    {
                        float magnitude = Mathf.Sqrt((float)(realBuffer[currentCheckBin] * realBuffer[currentCheckBin] + imagBuffer[currentCheckBin] * imagBuffer[currentCheckBin]));
                        if (magnitude > bestHarmonicMag) bestHarmonicMag = magnitude;
                    }
                }
                harmonicAmplitudes[h - 1] = (bestHarmonicMag > highestMagnitude * harmonicThresholdRatio) ? bestHarmonicMag : 0f;
            }

            analysisComplete = true;

            PlotFftSpectrumData(realBuffer, imagBuffer, sampleRate);
            PlotTimeDomainData(totalSamples, sampleRate, principalFrequency);

            GenerateStaticFourierSlices();
        }
    }
    void ExecuteInPlaceCooleyTukeyFFT(double[] real, double[] imag, int n) 
    {
        int j = 0;
        for (int i = 0; i < n; i++) 
        {
            if (i < j) 
            {
                double tempReal = real[i]; double tempImag = imag[i];
                real[i] = real[j]; imag[i] = imag[j];
                real[j] = tempReal; imag[j] = tempImag;
            }
            int m = n >> 1;
            while (m >= 1 && j >= m) { j -= m; m >>= 1; }
            j += m;
        }

        for (int len = 2; len <= n; len <<= 1) 
        {
            double angle = -2.0 * System.Math.PI / len;
            double wlenReal = System.Math.Cos(angle);
            double wlenImag = System.Math.Sin(angle);
            for (int i = 0; i < n; i += len) 
            {
                double wReal = 1.0; double wImag = 0.0;
                for (int k = 0; k < len / 2; k++) 
                {
                    int u = i + k; int v = i + k + len / 2;
                    double tReal = real[v] * wReal - imag[v] * wImag;
                    double tImag = real[v] * wImag + imag[v] * wReal;
                    real[v] = real[u] - tReal; imag[v] = imag[u] - tImag;
                    real[u] += tReal; imag[u] += tImag;
                    double nextWReal = wReal * wlenReal - wImag * wlenImag;
                    wImag = wReal * wlenImag + wImag * wlenReal;
                    wReal = nextWReal;
                }
            }
        }
    }
    void GenerateStaticFourierSlices() 
    {
        foreach (GameObject wave in activeWaves) 
        {
            if (wave != null) Destroy(wave);
        }
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
                if (amplitudeWeight > 0f) {
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
                    combinedAcousticColor = Color.Lerp(palette.zeroPressureEquilibrium, palette.highPressureCrest, normalizedPressure);
                else 
                    combinedAcousticColor = Color.Lerp(palette.zeroPressureEquilibrium, palette.lowPressureTrough, Mathf.Abs(normalizedPressure));
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
            identifier.harmonicOrder = fundamentalPhaseSign > 0.02f ? "Fourier Compression Zone (High Pressure Crest)" : fundamentalPhaseSign < -0.02f ? "Fourier Rarefaction Zone (Low Pressure Trough)" : "Acoustic Equilibrium Node (Zero Pressure)";

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

    // DEDUPLICATED: Tooltip no longer repeats global frequency, peak pressure, or RMS data
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
                
                // Track purely local data characteristics at this specific target coordinate point
                float hitRadius = Vector3.Distance(transform.position, hit.point);

                //tooltipText.text = $"<b>LOCAL WAVE ANALYSIS</b>\n" +
                //                   $"Node Type: {targetedWave.harmonicOrder}\n" +
                //                   $"Distance From Source: {hitRadius:F2} m";
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

    // CONSOLIDATED CENTRAL DASHBOARD: Aggregates all global calculation states in one clean location
    // CONSOLIDATED DIAGNOSTICS DASHBOARD: Aggregates global acoustics and harmonic tracking metrics
    void UpdateUIScreen() 
    {
        if (uiTextDisplay == null) return;
        
        string freqText = analysisComplete ? $"{principalFrequency:F1} Hz" : "Computing Fast Fourier Transform...";
        
        // Build the system telemetry header
        string telemetryReport = $"<b>NIST CHAMBER SYSTEM (DIRECT FFT ANALYSIS)</b>\n" +
                                 $"• Principal Pitch (1f): {freqText}\n" +
                                 $"• Total Visual Slices: {activeWaves.Count}\n" +
                                 $"• Global Computed RMS: {calculatedRMS:F5}\n" +
                                 $"• Global Peak Pressure: {peakPressure:F5}\n\n" +
                                 $"<b>🧬 HARMONIC DIAGNOSTIC CORE BREAKDOWN:</b>\n";

        if (analysisComplete)
        {
            float fundamentalMag = harmonicAmplitudes[0];

            for (int h = 1; h <= 4; h++)
            {
                float currentFreq = principalFrequency * h;
                float currentMag = harmonicAmplitudes[h - 1];

                if (h == 1)
                {
                    telemetryReport += $"  ↳ [1f Base Core] -> {currentFreq:F1} Hz | Mag Weight: {currentMag:F4} (100% Reference)\n";
                }
                else
                {
                    // If magnitude is above 0, it means it successfully passed your Python threshold ratio
                    if (currentMag > 0f)
                    {
                        // Calculate visual energy distribution ratio relative to your fundamental peak
                        float energyRatio = (fundamentalMag > 0.001f) ? (currentMag / fundamentalMag) * 100f : 0f;
                        telemetryReport += $"  ↳ [{h}f Overtone] -> {currentFreq:F1} Hz | Mag Weight: {currentMag:F4} ({energyRatio:F1}% of Core)\n";
                    }
                    else
                    {
                        telemetryReport += $"  ↳ [{h}f Overtone] -> {currentFreq:F1} Hz | NOT DETECTED (Below Noise Floor Threshold)\n";
                    }
                }
            }
        }
        else
        {
            telemetryReport += "⏳ Awaiting WAV file stream extraction passing diagnostics pass...\n";
        }

        uiTextDisplay.text = telemetryReport;
    }

}

