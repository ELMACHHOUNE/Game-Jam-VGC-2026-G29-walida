using UnityEngine;

/// <summary>
/// Fonts for the code-built uGUI screens (minigame HUDs, phone screen), so they share the
/// typography of the UI Toolkit screens: Cinzel for headers, Lora for body text.
/// Loaded from Resources/MedinaUITheme by <see cref="MedinaUIKit"/>.
/// </summary>
[CreateAssetMenu(fileName = "MedinaUITheme", menuName = "Medina/UI Theme")]
public class MedinaUITheme : ScriptableObject
{
    [Tooltip("Headers, labels and titles (Cinzel Bold).")]
    public Font headerFont;
    [Tooltip("Body text, subtitles and captions (Lora).")]
    public Font bodyFont;
    [Tooltip("Emphasised body text, buttons and numbers (Lora Medium).")]
    public Font bodyStrongFont;
}
