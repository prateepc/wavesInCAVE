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

        foreach (Transform child in contentContainer)
        {
            Destroy(child.gameObject);
        }

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
            
            TextMeshProUGUI label = newButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = fileName;
            }

            RectTransform rect = newButton.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.localScale = Vector3.one;
                Vector3 pos = rect.localPosition;
                pos.z = 0f;
                rect.localPosition = pos;
            }

            Button btnComponent = newButton.GetComponent<Button>();
            if (btnComponent != null)
            {
                string capturedPath = filePath; 
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
    /// Asynchronously streams local WAV audio, verifies speaker playback state,
    /// and passes the active audio feed over to AdvancedWaveManager2.
    /// </summary>
    IEnumerator LoadAudioClipCoroutine(string absolutePath)
    {
        System.Uri fileUri = new System.Uri(absolutePath);
        string uriPath = fileUri.AbsoluteUri;

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
                    if (simulationAudioSource == null)
                    {
                        UpdateScreenDiagnostic("<b>ERROR:</b> simulationAudioSource is unassigned!");
                    }
                    else
                    {
                        // Ensure the speaker GameObject is active in hierarchy
                        if (!simulationAudioSource.gameObject.activeInHierarchy)
                        {
                            simulationAudioSource.gameObject.SetActive(true);
                        }

                        // Assign clip and reset properties
                        simulationAudioSource.clip = loadedClip;
                        simulationAudioSource.volume = 1.0f;
                        simulationAudioSource.mute = false;
                        simulationAudioSource.bypassEffects = true;
                        simulationAudioSource.bypassListenerEffects = true;
                        
                        // Force Spatial Blend to 2D (0.0) so CAVE listener positioning doesn't attenuate sound to 0
                        simulationAudioSource.spatialBlend = 0.0f; 

                        loadedClip.LoadAudioData(); 
                        simulationAudioSource.Play();

                        if (!simulationAudioSource.isPlaying)
                        {
                            UpdateScreenDiagnostic("<b>ERROR:</b> Speaker exists, but AudioSource.isPlaying is FALSE!");
                        }
                        else
                        {
                            TransitionToSimulation();
                        }
                    }
                }
            }
        }
    }

    private void UpdateScreenDiagnostic(string message)
    {
        if (fileSelectorPanel != null) fileSelectorPanel.SetActive(true); 

        if (uiTextDisplay != null)
        {
            uiTextDisplay.text = message;
        }
        else
        {
            GameObject foundUIObject = GameObject.Find("TelemetryDisplay");
            if (foundUIObject != null)
            {
                TextMeshProUGUI textComp = foundUIObject.GetComponent<TextMeshProUGUI>();
                if (textComp != null) textComp.text = message;
            }
        }
    }

    void TransitionToSimulation()
    {
        if (fileSelectorPanel != null) 
        {
            fileSelectorPanel.SetActive(false);
        }

        if (simulationContent != null) 
        {
            simulationContent.SetActive(true);
        }

        // Pass the AudioSource over to AdvancedWaveManager2
        AdvancedWaveManager2 waveMgr2 = FindFirstObjectByType<AdvancedWaveManager2>();
        if (waveMgr2 != null)
        {
            // Link the playing AudioSource so AdvancedWaveManager2 can sample spectrum data
            waveMgr2.audioSource = simulationAudioSource; 
        }
        else
        {
            Debug.LogWarning("[AudioFileScanner] Could not find 'AdvancedWaveManager2' in active scene!");
        }
    }
}