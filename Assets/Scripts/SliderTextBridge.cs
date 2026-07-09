using UnityEngine;
using TMPro;

public class SliderTextBridge : MonoBehaviour
{
    [Header("Target UI Label")]
    public TMP_Text textDisplay;

    [Header("Decimal Formatting")]
    [Tooltip("Check this if the slider needs decimal points (like RMS or Power). Leave unchecked for whole numbers (Frequency, Count).")]
    public bool showDecimals = false;

    /// <summary>
    /// This method will now appear in your Slider's Dynamic float dropdown!
    /// </summary>
    public void UpdateTextValue(float value)
    {
        if (textDisplay == null) return;

        // "F2" outputs 2 decimal places (e.g., 0.50). "F0" rounds to whole integers.
        string formatSpecifier = showDecimals ? "F2" : "F0";
        textDisplay.text = value.ToString(formatSpecifier);
    }
}