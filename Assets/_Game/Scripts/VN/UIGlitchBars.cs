using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Full-screen "digital tear" overlay for UI Toolkit: a handful of horizontal bars (RGB-split colours,
/// black dropouts, white noise) that are re-randomised every glitch step. Never blocks input.
/// </summary>
public sealed class UIGlitchBars
{
    private static readonly Color[] Palette =
    {
        new(0.00f, 1.00f, 1.00f, 0.38f), // cyan split
        new(1.00f, 0.05f, 0.75f, 0.38f), // magenta split
        new(1.00f, 1.00f, 1.00f, 0.22f), // white noise
        new(0.00f, 0.00f, 0.00f, 0.70f), // black dropout
        new(0.85f, 0.10f, 0.10f, 0.30f), // red bleed
    };

    private readonly VisualElement _container;
    private readonly List<VisualElement> _bars = new();

    public VisualElement Container => _container;

    public UIGlitchBars(VisualElement parent, int barCount = 8)
    {
        _container = new VisualElement { name = "glitch-bars", pickingMode = PickingMode.Ignore };
        _container.style.position = Position.Absolute;
        _container.style.left = 0;
        _container.style.top = 0;
        _container.style.right = 0;
        _container.style.bottom = 0;
        _container.style.overflow = Overflow.Hidden;
        _container.style.display = DisplayStyle.None;

        for (int i = 0; i < barCount; i++)
        {
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.style.position = Position.Absolute;
            _container.Add(bar);
            _bars.Add(bar);
        }

        parent.Add(_container);
    }

    public bool IsAttachedTo(VisualElement parent) => _container.parent == parent;

    /// <summary>Re-rolls every bar. intensity 0..1 scales how many bars show and how thick they are.</summary>
    public void Randomize(float intensity)
    {
        intensity = Mathf.Clamp01(intensity);
        _container.style.display = DisplayStyle.Flex;
        _container.BringToFront();

        foreach (VisualElement bar in _bars)
        {
            bool show = Random.value < Mathf.Lerp(0.25f, 0.75f, intensity);
            bar.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) continue;

            bar.style.top = Length.Percent(Random.Range(0f, 98f));
            bar.style.height = Length.Percent(Random.Range(0.3f, Mathf.Lerp(2f, 7f, intensity)));
            bar.style.left = Length.Percent(Random.Range(-15f, 25f));
            bar.style.width = Length.Percent(Random.Range(35f, 120f));
            bar.style.backgroundColor = Palette[Random.Range(0, Palette.Length)];
        }
    }

    public void Hide()
    {
        _container.style.display = DisplayStyle.None;
    }

    public void Detach()
    {
        _container.RemoveFromHierarchy();
    }
}
