using UnityEngine;

public class SimulationLauncher : MonoBehaviour
{
    [Tooltip("Drag the Panel_StartMenu object here")]
    public GameObject startMenuPanel;

    [Tooltip("Drag the SimulationContent object here")]
    public GameObject simulationContent;

    void Start()
    {
        // Ensure we start the application in the correct state
        if (startMenuPanel != null) startMenuPanel.SetActive(true);
        if (simulationContent != null) simulationContent.SetActive(false);
    }

    /// <summary>
    /// This method will be triggered when the user clicks the Enter button
    /// </summary>
    public void LaunchSimulation()
    {
        if (startMenuPanel != null) startMenuPanel.SetActive(false);
        if (simulationContent != null) simulationContent.SetActive(true);
        
        Debug.Log("[Acoustic ADT] Simulation launched successfully.");
    }
}