using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioListener))]
public class AudioMixCaptureTap : MonoBehaviour
{
    private readonly object _lock = new object();
    private readonly List<float> _samples = new List<float>(65536);
    private bool _isCapturing;
    private int _channels = 2;
    private int _sampleRate = 48000;
    private float _currentRms;

    public float CurrentRmsLevel
    {
        get
        {
            lock (_lock)
            {
                return _currentRms;
            }
        }
    }

    public bool IsCapturing
    {
        get
        {
            lock (_lock)
            {
                return _isCapturing;
            }
        }
    }

    public int Channels
    {
        get
        {
            lock (_lock)
            {
                return _channels;
            }
        }
    }

    public int SampleRate
    {
        get
        {
            lock (_lock)
            {
                return _sampleRate;
            }
        }
    }

    public void StartCapture()
    {
        int sr = AudioSettings.outputSampleRate;
        lock (_lock)
        {
            _samples.Clear();
            _sampleRate = sr > 0 ? sr : 48000;
            _isCapturing = true;
        }
    }

    public float[] StopCapture(out int channels, out int sampleRate)
    {
        lock (_lock)
        {
            _isCapturing = false;
            channels = _channels > 0 ? _channels : 2;
            sampleRate = _sampleRate > 0 ? _sampleRate : 48000;
            float[] result = _samples.ToArray();
            _samples.Clear();
            return result;
        }
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (data == null || data.Length == 0)
        {
            return;
        }

        float sumSq = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float s = data[i];
            sumSq += s * s;
        }
        float rms = Mathf.Sqrt(sumSq / data.Length);

        lock (_lock)
        {
            _channels = channels > 0 ? channels : 2;
            _currentRms = rms;
            if (!_isCapturing)
            {
                return;
            }

            for (int i = 0; i < data.Length; i++)
            {
                _samples.Add(data[i]);
            }
        }
    }
}
