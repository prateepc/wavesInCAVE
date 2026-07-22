using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections;
using UnityEngine.Networking;
using TMPro;

public class AudioFileScanner : MonoBehaviour
{
    [Header("Path Settings")]
    [HideInInspector]
    public string folderPath; 

    [Header("UI References")]
    public TextMeshProUGUI uiTextDisplay;
    [Tooltip("Prefab instantiated inside the ScrollView Content container for each found WAV file.")]
    public GameObject buttonPrefab;   
    [Tooltip("The Content Transform inside the ScrollView Viewport.")]
    public Transform contentContainer; 
    [Tooltip("Drag your Panel_FileSelector here so it can be hidden upon file selection.")]
    public GameObject fileSelectorPanel; 
    [Tooltip("Drag your simulation manager/3D container here so it can be revealed upon file selection.")]
    public GameObject simulationContent; 

    [Header("Audio Setup")]
    [Tooltip("Drag your CAVE Simulation speaker AudioSource here.")]
    public AudioSource simulationAudioSource; 
    

    void Awake()
    {
        string baseUserPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);

        if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer)
        {
            if (!baseUserPath.EndsWith("Documents"))
            {
                baseUserPath = Path.Combine(baseUserPath, "Documents");
            }
        }

        folderPath = Path.Combine(baseUserPath, "wavesInCAVE", "Recordings");
    }

    void Start()
    {
        AudioListener.pause = false;
        AudioListener.volume = 1.0f;
    
        RefreshFileList();
    }

    /// <summary>
    /// Scans the target folder for .wav files and populates the UI list.
    /// </summary>
    public void RefreshFileList()
    {
        if (contentContainer == null)
        {
            Debug.LogError("[AudioFileScanner] CRITICAL: 'Content Container' is missing in the Inspector!");
            return;
        }

        // Clean out existing UI button elements inside the scroll list
        foreach (Transform child in contentContainer)
        {
            Destroy(child.gameObject);
        }

        // Ensure directory exists
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
            Debug.Log($"[AudioFileScanner] Created directory at path: {folderPath}");
        }

        string[] audioFiles = Directory.GetFiles(folderPath, "*.wav");

        if (audioFiles.Length == 0)
        {
            Debug.LogWarning($"[AudioFileScanner] No .wav files found in directory: {folderPath}");
            return;
        }

        foreach (string filePath in audioFiles)
        {
            if (buttonPrefab == null)
            {
                Debug.LogError("[AudioFileScanner] CRITICAL: 'Button Prefab' is missing in the Inspector!");
                break;
            }

            string fileName = Path.GetFileName(filePath);
            GameObject newButton = Instantiate(buttonPrefab, contentContainer);
            
            // Assign button text label
            TextMeshProUGUI label = newButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = fileName;
            }

            // Ensure button scale/position resets nicely inside layout groups
            RectTransform rect = newButton.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.localScale = Vector3.one;
                Vector3 pos = rect.localPosition;
                pos.z = 0f;
                rect.localPosition = pos;
            }

            // Attach dynamic click event to initiate loading on button tap
            Button btnComponent = newButton.GetComponent<Button>();
            if (btnComponent != null)
            {
                string capturedPath = filePath; // Capture local variable for lambda delegate
                btnComponent.onClick.AddListener(() => LoadAudioIntoSimulation(capturedPath));
            }
        }
    }

    void LoadAudioIntoSimulation(string selectedFilePath)
    {
        Debug.Log($"[AudioFileScanner] File selection triggered: {selectedFilePath}");
        StartCoroutine(LoadAudioClipCoroutine(selectedFilePath));
    }

    /// <summary>
    /// Asynchronously streams and decodes local WAV audio data off the disk without stalling CAVE frame rates.
    /// </summary>
    IEnumerator LoadAudioClipCoroutine(string absolutePath)
    {
        System.Uri fileUri = new System.Uri(absolutePath);
        string uriPath = fileUri.AbsoluteUri;

        // Show loading status on the Telemetry Display immediately upon click
        if (uiTextDisplay != null) 
        {
            uiTextDisplay.text = $"<b>LOADING AUDIO...</b>\nFile: {Path.GetFileName(absolutePath)}";
        }

        using (UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip(uriPath, AudioType.WAV))
        {
            yield return uwr.SendWebRequest();

            if (uwr.result == UnityWebRequest.Result.ConnectionError || uwr.result == UnityWebRequest.Result.ProtocolError)
            {
                UpdateScreenDiagnostic($"<b>LOAD ERROR:</b>\n{uwr.error}\nURI: {uriPath}");
            }
            else
            {
                AudioClip loadedClip = DownloadHandlerAudioClip.GetContent(uwr);

                if (loadedClip == null)
                {
                    UpdateScreenDiagnostic("<b>ERROR:</b> Downloaded AudioClip is NULL!");
                }
                else if (loadedClip.samples == 0)
                {
                    UpdateScreenDiagnostic($"<b>ERROR: 0 SAMPLES DECODED!</b>\nFile: {loadedClip.name}\nCheck WAV encoding (Must be 16-bit PCM).");
                }
                else
                {
                    if (simulationAudioSource != null)
                    {
                        simulationAudioSource.clip = loadedClip;
                        
                        // Explicitly enforce audio playback properties for MiddleVR
                        simulationAudioSource.volume = 1.0f;
                        simulationAudioSource.mute = false;
                        simulationAudioSource.bypassEffects = true;
                        simulationAudioSource.bypassListenerEffects = true;
                        
                        loadedClip.LoadAudioData(); 
                        simulationAudioSource.Play();

                        TransitionToSimulation();
                    }
                    else
                    {
                        UpdateScreenDiagnostic("<b>ERROR:</b> AudioSource missing on Scanner!");
                    }
                }
            }
        }
    }

    // Helper function to print diagnostics to your screen canvas
    private void UpdateScreenDiagnostic(string message)
    {
        // 1. Keep or re-enable the file panel so you can read the error in the CAVE
        if (fileSelectorPanel != null) fileSelectorPanel.SetActive(true); 

        // 2. Print error directly onto your TelemetryDisplay UI
        if (uiTextDisplay != null)
        {
            uiTextDisplay.text = message;
        }
        else
        {
            // Fallback: try finding it automatically if unassigned
            GameObject foundUIObject = GameObject.Find("TelemetryDisplay");
            if (foundUIObject != null)
            {
                TextMeshProUGUI textComp = foundUIObject.GetComponent<TextMeshProUGUI>();
                if (textComp != null) textComp.text = message;
            }
        }
    }

    IEnumerator VerifyPlaybackCoroutine(AudioSource source)
    {
        yield return null; // Wait 1 frame for audio playback initialization

        Debug.Log($"[Acoustic Diagnostics] --- SPEAKER STATE CHECK ---");
        Debug.Log($"Speaker Clip Assigned: {(source.clip != null ? source.clip.name : "NULL!")}");
        Debug.Log($"Speaker Volume Level: {source.volume}");
        Debug.Log($"Is Speaker Physically Playing? {source.isPlaying}");
        Debug.Log($"Mute Active? {source.mute}");
        Debug.Log($"----------------------------------------");
        
        if (source.clip != null && source.isPlaying)
        {
            Debug.Log("[Acoustic Diagnostics] SUCCESS! Audio is playing and sending signals to simulation.");
        }
        else if (!source.isPlaying)
        {
            Debug.LogError("[Acoustic Diagnostics] FAIL: AudioSource is NOT playing! Check if the Speaker object is disabled.");
        }
    }

    void TransitionToSimulation()
    {
        if (fileSelectorPanel != null) 
        {
            fileSelectorPanel.SetActive(false);
        }
        else
        {
            Debug.LogWarning("[AudioFileScanner] 'File Selector Panel' field is unassigned in the Inspector!");
        }

        if (simulationContent != null) 
        {
            simulationContent.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[AudioFileScanner] 'Simulation Content' field is unassigned in the Inspector!");
        }
    }
}