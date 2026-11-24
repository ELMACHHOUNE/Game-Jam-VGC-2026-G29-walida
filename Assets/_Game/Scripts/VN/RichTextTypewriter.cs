using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class RichTextTypewriter : IDisposable
{
    private readonly Label _targetLabel;
    private readonly List<int> _cutIndices = new();
    private CancellationTokenSource _cts;
    private string _fullText = string.Empty;

    public bool IsTyping { get; private set; }
    public event Action OnCompleted;
    public event Action<char> OnCharacterRevealed;
 
    public RichTextTypewriter(Label targetLabel)
    {
        _targetLabel = targetLabel;
    }

    public void Play(string text, float charsPerSecond = 45f)
    {
        CancelInternal();

        _fullText = text ?? string.Empty;
        BuildCutIndices(_fullText);

        if (_cutIndices.Count == 0 || charsPerSecond <= 0f)
        {
            _targetLabel.text = _fullText;
            IsTyping = false;
            OnCompleted?.Invoke();
            return;
        }

        _cts = new CancellationTokenSource();
        IsTyping = true;
        RunTypewriterAsync(charsPerSecond, _cts.Token);
    }

    public void CompleteImmediately()
    {
        if (!IsTyping) return;

        CancelInternal();
        _targetLabel.text = _fullText;
        IsTyping = false;
        OnCompleted?.Invoke();
    }

    private async void RunTypewriterAsync(float charsPerSecond, CancellationToken token)
    {
        float baseDelay = 1f / charsPerSecond;

        try
        {
            for (int step = 0; step < _cutIndices.Count; step++)
            {
                int cut = _cutIndices[step];
                _targetLabel.text = _fullText.Insert(cut, "<alpha=#00>");

                char revealedChar = _fullText[cut - 1];
                OnCharacterRevealed?.Invoke(revealedChar);
                float delay = GetPunctuationMultiplier(revealedChar) * baseDelay;

                await Awaitable.WaitForSecondsAsync(delay, token);
            }



            if (token.IsCancellationRequested) return;

            _targetLabel.text = _fullText;
            IsTyping = false;
            OnCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Expected when skipped or interrupted by a new line
        }
    }

    private void BuildCutIndices(string source)
    {
        _cutIndices.Clear();
        int i = 0;
        int len = source.Length;

        while (i < len)
        {
            if (source[i] == '<')
            {
                int closeIdx = source.IndexOf('>', i + 1);
                if (closeIdx != -1)
                {
                    i = closeIdx + 1;
                    continue;
                }
            }

            i++;
            _cutIndices.Add(i);
        }
    }

    private static float GetPunctuationMultiplier(char c) => c switch
    {
        '.' or '!' or '?' => 5.0f,
        ',' or ';' or ':' => 2.5f,
        _ => 1.0f
    };

    private void CancelInternal()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
    }

    public void Dispose()
    {
        CancelInternal();
        IsTyping = false;
    }
}