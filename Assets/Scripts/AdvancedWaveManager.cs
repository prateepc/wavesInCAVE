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
    
    // Core extracted analytical variables
    private float principalFrequency = 343f;
    private float calculatedRMS = 0f;
    private float peakPressure = 0f;
    private float[] harmonicAmplitudes = new float[] { 0f, 0f, 0f, 0f };
    private bool analysisComplete = false;

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

        DiscoverAndAnalyzeLatestWav();
    }

    void Update()
    {
        UpdateUIScreen();
        HandleWaveInterrogation();
    }

    void DiscoverAndAnalyzeLatestWav()
    {
        string streamingPath = Application.streamingAssetsPath;
        if (!Directory.Exists(streamingPath)) return;

        string[] discoveredFiles = Directory.GetFiles(streamingPath, "*.wav");
        if (discoveredFiles.Length == 0) return;

        // Automatically sort by latest modification timestamp
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

            // Extract native floating-point sample arrays from the clip asset channels
            float[] totalSamples = new float[clip.samples * clip.channels];
            clip.GetData(totalSamples, 0);

            // 1. ESTIMATE RMS AND PEAK PRESSURES
            float sumOfSquares = 0f;
            float maxPeak = 0f;
            int analysisLength = Mathf.Min(totalSamples.Length, 131072); // Read a wide data window for clean sample coverage

            for (int i = 0; i < analysisLength; i++)
            {
                float sampleValue = totalSamples[i];
                sumOfSquares += sampleValue * sampleValue;
                if (Mathf.Abs(sampleValue) > maxPeak) maxPeak = Mathf.Abs(sampleValue);
            }
            calculatedRMS = Mathf.Sqrt(sumOfSquares / analysisLength);
            peakPressure = maxPeak;

            // 2. NATIVE FAST FOURIER TRANSFORM (FFT) MATRIX SETUP
            int fftSize = 16384; 
            float sampleRate = clip.frequency;

            double[] realBuffer = new double[fftSize];
            double[] imagBuffer = new double[fftSize];
            for (int i = 0; i < fftSize; i++)
            {
                // Apply a Hanning window function to reduce side-lobe grid leakage artifacts
                float hanningWindow = 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * i / (fftSize - 1)));
                realBuffer[i] = totalSamples[i] * hanningWindow;
                imagBuffer[i] = 0.0;
            }

            // Execute Cooley-Tukey Radix-2 FFT calculation pass
            ExecuteInPlaceCooleyTukeyFFT(realBuffer, imagBuffer, fftSize);

            // 3. IDENTIFY PRINCIPAL PEAK BIN LOCATION
            float binWidth = sampleRate / fftSize;
            float highestMagnitude = 0f;
            int peakBinIndex = 0;

            int minBin = Mathf.CeilToInt(30f / binWidth);
            int maxBin = Mathf.FloorToInt(4000f / binWidth);

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
            harmonicAmplitudes[0] = highestMagnitude; // 1f Magnitude base

            // 4. ANALYZE AND EXTRACT THREE UPPER HARMONIC OVERTONES (2f, 3f, 4f)
            for (int h = 2; h <= 4; h++)
            {
                float targetHarmonicFreq = principalFrequency * h;
                int targetBin = Mathf.RoundToInt(targetHarmonicFreq / binWidth);
                
                float bestHarmonicMag = 0f;
                for (int offset = -2; offset <= 2; offset++)
                {
                    int currentCheckBin = targetBin + offset;
                    if (currentCheckBin < fftSize / 2)
                    {
                        float magnitude = Mathf.Sqrt((float)(realBuffer[currentCheckBin] * realBuffer[currentCheckBin] + imagBuffer[currentCheckBin] * imagBuffer[currentCheckBin]));
                        if (magnitude > bestHarmonicMag) bestHarmonicMag = magnitude;
                    }
                }
                harmonicAmplitudes[h - 1] = (bestHarmonicMag > highestMagnitude * 0.05f) ? bestHarmonicMag : 0f;
            }

            analysisComplete = true;

            // Trigger instant dynamic layout generation matching true wavelength physics
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
            double wlenReal = System.Math.Cos(angle); double wlenImag = System.Math.Sin(angle);
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
        foreach (GameObject wave in activeWaves) { if (wave != null) Destroy(wave); }
        activeWaves.Clear();

        if (wavePrefab == null) return;

        float roomFadeMaxDistance = chamberMax.z - chamberMin.z; // 10.0 meters total long
        float baseWavelength = SPEED_OF_SOUND / principalFrequency; // 343 / 343 = 1.0 meter physical wave cycles
        
        float totalWavelengthsInChamber = roomFadeMaxDistance / baseWavelength; // Fits exactly 10 waves
        int slicesPerWavelength = 6; // Spawns 6 flat timeline indicators per meter to completely capture phase
        
        int totalSlices = Mathf.CeilToInt(totalWavelengthsInChamber * slicesPerWavelength); // Exactly 60 flat slices
        if (totalSlices < 2) totalSlices = 2;

        float spatialStepDistance = roomFadeMaxDistance / totalSlices;
        MaterialPropertyBlock propBlock = new MaterialPropertyBlock();

        // Establish an ultra-small offset to prevent a divide-by-zero crash at r = 0
        float sourceOriginSafetyOffset = 0.05f; 

        for (int i = 1; i <= totalSlices; i++)
        {
            // r represents the actual physical radius from the sound source center in meters
            float r = (i * spatialStepDistance) + sourceOriginSafetyOffset;

            // FIXED POINT-SOURCE CALIBRATION: All shells are centered on the speaker's true transform location
            Vector3 spawnPosition = transform.position;

            float complexAcousticWave = 0f;
            float totalWeights = 0f;
            float fundamentalPhaseSign = 0f;

            // 1. SPHERICAL PROPAGATION PROP ENGINE: P(r) = (P_peak / r) * sin(k * r)
            for (int h = 1; h <= 4; h++)
            {
                float amplitudeWeight = harmonicAmplitudes[h - 1];
                if (amplitudeWeight > 0f)
                {
                    float currentFrequency = principalFrequency * h;
                    
                    // Calculate Wave Number (k = 2 * PI / lambda) for this specific harmonic tier
                    float k = (2f * Mathf.PI * currentFrequency) / SPEED_OF_SOUND;

                    // SPHERICAL ACOUSTIC PROPAGATION FORMULA: P(r) = (P_peak / r) * sin(k * r)
                    float layerPressure = (amplitudeWeight / r) * Mathf.Sin(k * r);

                    complexAcousticWave += layerPressure;
                    totalWeights += (amplitudeWeight / r); // Attenuate maximum relative scaling weights by 1/r as well

                    if (h == 1) fundamentalPhaseSign = layerPressure;
                }
            }

            // Normalize the combined complex wave pressure to a safe [-1.0, +1.0] visual range
            float normalizedPressure = (totalWeights > 0f) ? (complexAcousticWave / totalWeights) : 0f;
            normalizedPressure = Mathf.Clamp(normalizedPressure, -1f, 1f);

            // 2. MIX BIPOLAR SPECTRAL PALETTES BASED ON WEIGHTS
            Color combinedAcousticColor = Color.white;
            if (totalWeights > 0f)
            {
                BipolarSpectrumPair palette = harmonicColorPalettes[0]; // Map directly to fundamental palette layout template
                if (normalizedPressure >= 0f)
                {
                    // Positive compression crest: Blend smoothly from White (0.0) up to Red (+1.0)
                    combinedAcousticColor = Color.Lerp(palette.zeroPressureEquilibrium, palette.highPressureCrest, normalizedPressure);
                }
                else
                {
                    // Negative rarefaction trough: Blend smoothly from White (0.0) down to Blue (-1.0)
                    combinedAcousticColor = Color.Lerp(palette.zeroPressureEquilibrium, palette.lowPressureTrough, Mathf.Abs(normalizedPressure));
                }
            }

            // Factor in standard fading depth rules and master opacity choices
            float distanceDecay = Mathf.Clamp01(1.0f - (r / roomFadeMaxDistance));
            combinedAcousticColor.a = waveOpacity * distanceDecay;

            // 3. INSTANTIATE AND EXPAND CONCENTRIC 4*PI SPHERICAL SHELL
            GameObject frozenWave = Instantiate(wavePrefab, spawnPosition, Quaternion.identity);
            frozenWave.name = $"Static_Spherical_Shell_{i}_Radius_{r:F2}m_P_{normalizedPressure:F2}";
            
            // MAP RADIUS DIRECTLY TO SCALE: Multiplied by 2.0f because localScale governs diameter sizing
            frozenWave.transform.localScale = new Vector3(r * 2f, r * 2f, r * 2f);
            ConfigureShaderBoundaries(frozenWave);

            Collider c = frozenWave.GetComponent<Collider>();
            if (c != null) c.isTrigger = true;

            // 4. MAP DETAILS FOR THE INTERACTION WAND
            WaveDataIdentifier identifier = frozenWave.AddComponent<WaveDataIdentifier>();
            identifier.waveFrequency = Mathf.RoundToInt(principalFrequency);
            identifier.harmonicOrder = fundamentalPhaseSign > 0.02f ? "Fourier Compression Zone (High Pressure Crest)" :
                                       fundamentalPhaseSign < -0.02f ? "Fourier Rarefaction Zone (Low Pressure Trough)" : "Acoustic Equilibrium Node (Zero Pressure)";

            // 5. INJECT MATRIX PARAMETERS VIA PROPERTY BLOCK TO COMPLETELY PREVENT MATERIAL LEAKS
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
        
        uiTextDisplay.text = $"<b>NIST CHAMBER SYSTEM (DIRECT AUDIO PIPELINE)</b>\n" +
                             $"Extracted Pitch Core: {freqText}\n" +
                             $"Total Displayed Slices: {activeWaves.Count}\n" +
                             $"Status: Wavelength Geometry Locked.";
    }
}

