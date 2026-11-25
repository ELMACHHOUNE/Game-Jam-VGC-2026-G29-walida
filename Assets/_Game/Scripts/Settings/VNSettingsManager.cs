using System;
using UnityEngine;

public class VNSettingsManager : MonoBehaviour
{
    public const string BgmVolumeKey = "VN_Settings_BgmVolume";
    public const string SfxVolumeKey = "VN_Settings_SfxVolume";
    public const string VoiceVolumeKey = "VN_Settings_VoiceVolume";
    public const string TextSpeedKey = "VN_Settings_TextSpeed";

    public const float DefaultBgmVolume = 0.55f;
    public const float DefaultSfxVolume = 0.85f;
    public const float DefaultVoiceVolume = 0.45f;
    public const float DefaultTextSpeed = 45f;

    public const float MinTextSpeed = 15f;
    public const float MaxTextSpeed = 120f;

    public static VNSettingsManager Instance { get; private set; }

    public event Action OnSettingsChanged;

    public float BgmVolume { get; private set; } = DefaultBgmVolume;
    public float SfxVolume { get; private set; } = DefaultSfxVolume;
    public float VoiceVolume { get; private set; } = DefaultVoiceVolume;
    public float TextSpeed { get; private set; } = DefaultTextSpeed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        LoadFromPrefs();
    }

    public void LoadFromPrefs()
    {
        BgmVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(BgmVolumeKey, DefaultBgmVolume));
        SfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, DefaultSfxVolume));
        VoiceVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VoiceVolumeKey, DefaultVoiceVolume));
        TextSpeed = Mathf.Clamp(PlayerPrefs.GetFloat(TextSpeedKey, DefaultTextSpeed), MinTextSpeed, MaxTextSpeed);
    }

    public void SetBgmVolume(float value)
    {
        float clamped = Mathf.Clamp01(value);
        if (Mathf.Approximately(BgmVolume, clamped)) return;

        BgmVolume = clamped;
        PlayerPrefs.SetFloat(BgmVolumeKey, BgmVolume);
        PlayerPrefs.Save();
        OnSettingsChanged?.Invoke();
    }

    public void SetSfxVolume(float value)
    {
        float clamped = Mathf.Clamp01(value);
        if (Mathf.Approximately(SfxVolume, clamped)) return;

        SfxVolume = clamped;
        PlayerPrefs.SetFloat(SfxVolumeKey, SfxVolume);
        PlayerPrefs.Save();
        OnSettingsChanged?.Invoke();
    }

    public void SetVoiceVolume(float value)
    {
        float clamped = Mathf.Clamp01(value);
        if (Mathf.Approximately(VoiceVolume, clamped)) return;

        VoiceVolume = clamped;
        PlayerPrefs.SetFloat(VoiceVolumeKey, VoiceVolume);
        PlayerPrefs.Save();
        OnSettingsChanged?.Invoke();
    }

    public void SetTextSpeed(float charsPerSecond)
    {
        float clamped = Mathf.Clamp(charsPerSecond, MinTextSpeed, MaxTextSpeed);
        if (Mathf.Approximately(TextSpeed, clamped)) return;

        TextSpeed = clamped;
        PlayerPrefs.SetFloat(TextSpeedKey, TextSpeed);
        PlayerPrefs.Save();
        OnSettingsChanged?.Invoke();
    }

    public void ResetToDefaults()
    {
        BgmVolume = DefaultBgmVolume;
        SfxVolume = DefaultSfxVolume;
        VoiceVolume = DefaultVoiceVolume;
        TextSpeed = DefaultTextSpeed;

        PlayerPrefs.SetFloat(BgmVolumeKey, BgmVolume);
        PlayerPrefs.SetFloat(SfxVolumeKey, SfxVolume);
        PlayerPrefs.SetFloat(VoiceVolumeKey, VoiceVolume);
        PlayerPrefs.SetFloat(TextSpeedKey, TextSpeed);
        PlayerPrefs.Save();

        OnSettingsChanged?.Invoke();
    }
}