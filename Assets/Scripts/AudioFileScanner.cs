using UnityEngine;
using UnityEngine.UI;
using System.IO;
using TMPro;

public class AudioFileScanner : MonoBehaviour
{
    [HideInInspector]
    public string folderPath; 

    public GameObject buttonPrefab;   
    public Transform contentContainer; 

    void Awake()
    {
        // 1. Grab the base folder system path
        string baseUserPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);

        // 2. Cross-platform check: If we are on Mac, "MyDocuments" often resolves to the root user folder ("/Users/username").
        // We append the actual "Documents" folder manually if it isn't already in the path string.
        if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer)
        {
            if (!baseUserPath.EndsWith("Documents"))
            {
                baseUserPath = Path.Combine(baseUserPath, "Documents");
            }
        }

        // 3. Cleanly stitch together the rest of the target folders
        folderPath = Path.Combine(baseUserPath, "wavesInCAVE", "Recordings");

        Debug.Log("[Acoustic ADT] Resolved platform-agnostic path: " + folderPath);
    }

    void Start()
    {
        // This is line 27 where the error was happening. It can now see the method below!
        RefreshFileList();
    }

    public void RefreshFileList()
    {
        // 1. Clear out old buttons from previous scans
        foreach (Transform child in contentContainer)
        {
            Destroy(child.gameObject);
        }

        // 2. Safety check: Create the folder if it doesn't exist yet
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        // 3. Gather all WAV files from the folder
        string[] audioFiles = Directory.GetFiles(folderPath, "*.wav");

        // 4. Loop through every file found and spawn a custom button for it
        foreach (string filePath in audioFiles)
        {
            string fileName = Path.GetFileName(filePath);

            // Instantiate a new button inside the layout group container
            GameObject newButton = Instantiate(buttonPrefab, contentContainer);

            // Update the button's text to display the actual file name
            newButton.GetComponentInChildren<TextMeshProUGUI>().text = fileName;

            // Setup the click event dynamically
            Button btnComponent = newButton.GetComponent<Button>();
            btnComponent.onClick.AddListener(() => LoadAudioIntoSimulation(filePath));
        }
    }

    void LoadAudioIntoSimulation(string selectedFilePath)
    {
        Debug.Log("[Acoustic ADT] User selected audio file: " + selectedFilePath);
    }
} // <--- Make sure this final closing bracket is present to close the class!