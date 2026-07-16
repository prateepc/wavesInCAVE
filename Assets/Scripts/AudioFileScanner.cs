using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections; // Required for Coroutines (IEnumerator)
using UnityEngine.Networking; // Required for Web Requests
using TMPro;

public class AudioFileScanner : MonoBehaviour
{
    [Header("Path Settings")]
    [HideInInspector]
    public string folderPath; 

    [Header("UI References")]
    public GameObject buttonPrefab;   
    public Transform contentContainer; 
    public GameObject fileSelectorPanel; // Drag your Panel_FileSelector here
    public GameObject simulationContent; // Drag your simulation manager/mesh here

    [Header("Audio Setup")]
    public AudioSource simulationAudioSource; // Drag your simulation's AudioSource here

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

    public void RefreshFileList()
    {
        foreach (Transform child in contentContainer)
        {
            Destroy(child.gameObject);
        }

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        string[] audioFiles = Directory.GetFiles(folderPath, "*.wav");

        foreach (string filePath in audioFiles)
        {
            string fileName = Path.GetFileName(filePath);
            GameObject newButton = Instantiate(buttonPrefab, contentContainer);
            newButton.GetComponentInChildren<TextMeshProUGUI>().text = fileName;

            Button btnComponent = newButton.GetComponent<Button>();
            
            // Set up click action to load and play this specific file
            btnComponent.onClick.AddListener(() => LoadAudioIntoSimulation(filePath));
        }
    }

    // This triggers the asynchronous loading process
    void LoadAudioIntoSimulation(string selectedFilePath)
    {
        StartCoroutine(LoadAudioClipCoroutine(selectedFilePath));
    }

    // Coroutine that runs in the background to load the audio without freezing your screen
    IEnumerator LoadAudioClipCoroutine(string absolutePath)
    {
        // Formulate a robust macOS-friendly URI path (file:///...)
        string uriPath;
        #if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            uriPath = "file://" + absolutePath; 
            if (!uriPath.StartsWith("file:///"))
            {
                uriPath = uriPath.Replace("file://", "file:///");
            }
        #else
            uriPath = "file:///" + absolutePath;
        #endif

        // Clean up spaces/special characters in the path string
        uriPath = System.Uri.EscapeUriString(uriPath);

        Debug.Log("[Acoustic Diagnostics] Attempting to fetch audio from: " + uriPath);

        // Send request to load the audio clip as a WAV file
        using (UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip(uriPath, AudioType.WAV))
        {
            yield return uwr.SendWebRequest();

            if (uwr.result == UnityWebRequest.Result.ConnectionError || uwr.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("[Acoustic Diagnostics] Web Request Error: " + uwr.error);
            }
            else
            {
                // Extract the downloaded AudioClip
                AudioClip loadedClip = DownloadHandlerAudioClip.GetContent(uwr);
                
                if (loadedClip == null)
                {
                    Debug.LogError("[Acoustic Diagnostics] CRITICAL: Downloaded AudioClip reference is NULL!");
                }
                else
                {
                    loadedClip.name = Path.GetFileName(absolutePath);

                    // --- STEP 4 AUDIO FILE FORMAT VALIDATION ---
                    Debug.Log($"[Acoustic Diagnostics] --- CLIP VERIFICATION ---");
                    Debug.Log($"Name: {loadedClip.name}");
                    Debug.Log($"Load State: {loadedClip.loadState}"); 
                    Debug.Log($"Channels: {loadedClip.channels}");
                    Debug.Log($"Frequency: {loadedClip.frequency} Hz");
                    Debug.Log($"Length: {loadedClip.length:F2} seconds");
                    Debug.Log($"Samples Count: {loadedClip.samples}");
                    Debug.Log($"----------------------------------------");

                    if (loadedClip.samples == 0)
                    {
                        Debug.LogError("[Acoustic Diagnostics] FAIL: The file was successfully retrieved, but it contains 0 audio samples. Unity failed to decode this WAV format! Your WAV encoding may be unsupported.");
                    }

                    // Assign the clip to your visualizer's AudioSource and play it
                    if (simulationAudioSource != null)
                    {
                        simulationAudioSource.clip = loadedClip;
                        // Inside LoadAudioClipCoroutine, right before simulationAudioSource.Play();
                        loadedClip.LoadAudioData(); // Force immediate decompression
                        simulationAudioSource.Play();
                        
                        // Run active playback check on the next frame
                        StartCoroutine(VerifyPlaybackCoroutine(simulationAudioSource));

                        TransitionToSimulation();
                    }
                    else
                    {
                        Debug.LogError("[Acoustic Diagnostics] FAIL: Missing an assigned AudioSource component in the Inspector!");
                    }
                }
            }
        }
    }

    IEnumerator VerifyPlaybackCoroutine(AudioSource source)
    {
        yield return null; // Wait 1 frame for AudioSource.Play() to register on the main thread

        Debug.Log($"[Acoustic Diagnostics] --- SPEAKER STATE CHECK ---");
        Debug.Log($"Speaker Clip Assigned: {(source.clip != null ? source.clip.name : "NULL!")}");
        Debug.Log($"Speaker Volume Level: {source.volume}");
        Debug.Log($"Is Speaker Physically Playing? {source.isPlaying}");
        Debug.Log($"Mute Active? {source.mute}");
        Debug.Log($"----------------------------------------");
        
        if (source.clip != null && source.isPlaying)
        {
            Debug.Log("[Acoustic Diagnostics] SUCCESS! Audio is decoded, loaded, and actively playing!");
        }
        else if (!source.isPlaying)
        {
            Debug.LogError("[Acoustic Diagnostics] FAIL: AudioSource is NOT playing! Check if the Speaker GameObject itself or its parent is being deactivated during the panel transition.");
        }
    }

    void TransitionToSimulation()
    {
        // Turn off the file browser and turn on the simulation visual objects
        if (fileSelectorPanel != null) fileSelectorPanel.SetActive(false);
        if (simulationContent != null) simulationContent.SetActive(true);
    }
}