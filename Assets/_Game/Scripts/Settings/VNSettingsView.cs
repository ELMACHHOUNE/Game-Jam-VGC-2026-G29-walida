using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class VNSettingsView : IDisposable
{
    private readonly Slider _bgmSlider;
    private readonly Label _bgmValueLabel;

    private readonly Slider _sfxSlider;
    private readonly Label _sfxValueLabel;

    private readonly Slider _voiceSlider;
    private readonly Label _voiceValueLabel;

    private readonly Slider _speedSlider;
    private readonly Label _speedValueLabel;

    public VNSettingsView(VisualElement settingsRoot, Action onCloseRequested)
    {
        _bgmSlider = settingsRoot.Q<Slider>("slider-bgm");
        _bgmValueLabel = settingsRoot.Q<Label>("val-bgm");

        _sfxSlider = settingsRoot.Q<Slider>("slider-sfx");
        _sfxValueLabel = settingsRoot.Q<Label>("val-sfx");

        _voiceSlider = settingsRoot.Q<Slider>("slider-voice");
        _voiceValueLabel = settingsRoot.Q<Label>("val-voice");

        _speedSlider = settingsRoot.Q<Slider>("slider-speed");
        _speedValueLabel = settingsRoot.Q<Label>("val-speed");

        var btnReset = settingsRoot.Q<Button>("btn-settings-reset");
        var btnClose = settingsRoot.Q<Button>("btn-close-settings");

        if (_bgmSlider != null)
        {
            _bgmSlider.focusable = false;
            _bgmSlider.RegisterValueChangedCallback(evt =>
            {
                VNSettingsManager.Instance?.SetBgmVolume(evt.newValue);
                UpdateLabels();
            });
        }

        if (_sfxSlider != null)
        {
            _sfxSlider.focusable = false;
            _sfxSlider.RegisterValueChangedCallback(evt =>
            {
                VNSettingsManager.Instance?.SetSfxVolume(evt.newValue);
                UpdateLabels();
            });
        }

        if (_voiceSlider != null)
        {
            _voiceSlider.focusable = false;
            _voiceSlider.RegisterValueChangedCallback(evt =>
            {
                VNSettingsManager.Instance?.SetVoiceVolume(evt.newValue);
                UpdateLabels();
            });
        }

        if (_speedSlider != null)
        {
            _speedSlider.focusable = false;
            _speedSlider.RegisterValueChangedCallback(evt =>
            {
                VNSettingsManager.Instance?.SetTextSpeed(evt.newValue);
                UpdateLabels();
            });
        }

        if (btnReset != null)
        {
            btnReset.focusable = false;
            btnReset.clicked += () => VNSettingsManager.Instance?.ResetToDefaults();
        }

        if (btnClose != null)
        {
            btnClose.focusable = false;
            btnClose.clicked += () => onCloseRequested?.Invoke();
        }

        if (VNSettingsManager.Instance != null)
            VNSettingsManager.Instance.OnSettingsChanged += SyncFromManager;

        SyncFromManager();
    }

    public void SyncFromManager()
    {
        var mgr = VNSettingsManager.Instance;
        float bgm = mgr != null ? mgr.BgmVolume : VNSettingsManager.DefaultBgmVolume;
        float sfx = mgr != null ? mgr.SfxVolume : VNSettingsManager.DefaultSfxVolume;
        float voice = mgr != null ? mgr.VoiceVolume : VNSettingsManager.DefaultVoiceVolume;
        float speed = mgr != null ? mgr.TextSpeed : VNSettingsManager.DefaultTextSpeed;

        _bgmSlider?.SetValueWithoutNotify(bgm);
        _sfxSlider?.SetValueWithoutNotify(sfx);
        _voiceSlider?.SetValueWithoutNotify(voice);
        _speedSlider?.SetValueWithoutNotify(speed);

        UpdateLabels();
    }

    private void UpdateLabels()
    {
        if (_bgmSlider != null && _bgmValueLabel != null)
            _bgmValueLabel.text = $"{Mathf.RoundToInt(_bgmSlider.value * 100f)}%";

        if (_sfxSlider != null && _sfxValueLabel != null)
            _sfxValueLabel.text = $"{Mathf.RoundToInt(_sfxSlider.value * 100f)}%";

        if (_voiceSlider != null && _voiceValueLabel != null)
            _voiceValueLabel.text = $"{Mathf.RoundToInt(_voiceSlider.value * 100f)}%";

        if (_speedSlider != null && _speedValueLabel != null)
            _speedValueLabel.text = $"{Mathf.RoundToInt(_speedSlider.value)}";
    }

    public void Dispose()
    {
        if (VNSettingsManager.Instance != null)
            VNSettingsManager.Instance.OnSettingsChanged -= SyncFromManager;
    }
}