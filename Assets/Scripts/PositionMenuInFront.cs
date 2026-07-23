using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class PositionMenuInFront : MonoBehaviour
{
    [Header("UI References")]
    public GameObject sliderMenuPanel;

    [Header("Player Tracking")]
    [Tooltip("Drag the actual MOVING camera/player object here whose coordinates you want to copy.")]
    public Transform movingPlayerTransform;

    private float lastToggleTime;
    private const float TOGGLE_COOLDOWN = 0.25f;

    private GraphicRaycaster graphicRaycaster;
    private EventSystem eventSystem;

    void Start()
    {
        Debug.Log("--- POSITION MENU SCRIPT IS ALIVE AND RUNNING ---");

        if (sliderMenuPanel != null)
        {
            graphicRaycaster = sliderMenuPanel.GetComponentInParent<GraphicRaycaster>();
            
            if (sliderMenuPanel.activeSelf)
            {
                sliderMenuPanel.SetActive(false);
            }
        }

        eventSystem = EventSystem.current;
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            // If the menu is already open, do not handle clicks here (prevents closing or repositioning)
            if (sliderMenuPanel != null && sliderMenuPanel.activeSelf) return;

            TryOpenMenu();
        }
    }

    private void TryOpenMenu()
    {
        if (Time.time - lastToggleTime < TOGGLE_COOLDOWN) return;
        
        lastToggleTime = Time.time;
        OpenAndPositionMenu();
    }

    public void OpenAndPositionMenu()
    {
        if (sliderMenuPanel == null)
        {
            Debug.LogWarning("[PositionMenuInFront] sliderMenuPanel is missing!");
            return;
        }

        // Auto-detect player transform if not assigned in Inspector
        if (movingPlayerTransform == null)
        {
            if (Camera.main != null)
            {
                movingPlayerTransform = Camera.main.transform;
            }
            else
            {
                Debug.LogError("[PositionMenuInFront] No moving player or main camera found!");
                return;
            }
        }

        // Always enable the UI when clicked
        sliderMenuPanel.SetActive(true);

        Debug.Log($"[PositionMenuInFront] Copying Coordinates: {movingPlayerTransform.position}");

        // Directly assign the camera/player coordinates to the canvas with +2 added manually to World Z
        Vector3 camPos = movingPlayerTransform.position;
        sliderMenuPanel.transform.position = new Vector3(camPos.x, camPos.y, 2.0f + camPos.z);

        // Match rotation so it points in the same direction as your view
        sliderMenuPanel.transform.rotation = movingPlayerTransform.rotation;
    }

    /// <summary>
    /// Call this from your UI Button's OnClick() event to close the panel.
    /// </summary>
    public void CloseMenu()
    {
        if (sliderMenuPanel != null)
        {
            sliderMenuPanel.SetActive(false);
            Debug.Log("[PositionMenuInFront] Menu closed via Close Button.");
        }
    }
}