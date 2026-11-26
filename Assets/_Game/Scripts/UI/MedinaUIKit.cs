using UnityEngine;
using UnityEngine.UI;

public enum MedinaTextRole
{
    /// <summary>Cinzel, Midnight navy: panel titles.</summary>
    Title,
    /// <summary>Cinzel, terracotta: small section labels.</summary>
    Label,
    /// <summary>Lora, iron-gall ink: normal text.</summary>
    Body,
    /// <summary>Lora Medium, ink: emphasised text.</summary>
    BodyStrong,
    /// <summary>Lora italic, faded ink: captions and secondary text.</summary>
    Caption,
    /// <summary>Lora italic, terracotta: hints and shortcuts.</summary>
    Hint
}

/// <summary>
/// Shared look for code-built uGUI screens, matching the game's UI direction
/// (README art direction + the Medina HUD / VN UI Toolkit styles):
/// die-cut storybook cards (white halo, cream paper, Midnight-navy stroke), Cinzel headers,
/// Lora body text, terracotta accents and eight-point zellige stars as list markers.
/// All sprites are generated once at runtime, so no art import is needed.
/// </summary>
public static class MedinaUIKit
{
    // ---- Palette -----------------------------------------------------------------------------
    public static readonly Color Ink = Hex(0x231D18);
    public static readonly Color InkMuted = new(0.137f, 0.114f, 0.094f, 0.72f);
    public static readonly Color Navy = Hex(0x153B56);
    public static readonly Color Midnight = Hex(0x183744);
    public static readonly Color OudayaBlue = Hex(0x276B8C);
    public static readonly Color Cream = Hex(0xF3EBDD);
    public static readonly Color Paper = Hex(0xEBE0CB);
    public static readonly Color PaperLight = Hex(0xFAF6EE);
    public static readonly Color Chalk = Hex(0xF2EDE3);
    public static readonly Color Sand = Hex(0xC8A477);
    public static readonly Color Sepia = Hex(0xA87C52);
    public static readonly Color Terracotta = Hex(0x9E533B);
    public static readonly Color Olive = Hex(0x66765A);
    public static readonly Color Charcoal = Hex(0x252827);
    public static readonly Color Scrim = new(16f / 255f, 24f / 255f, 32f / 255f, 0.68f);

    public static Color Hex(int rgb, float alpha = 1f) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    public static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

    // ---- Fonts -------------------------------------------------------------------------------
    private static MedinaUITheme _theme;
    private static bool _themeLoaded;
    private static Font _fallbackFont;

    private static MedinaUITheme Theme
    {
        get
        {
            if (!_themeLoaded)
            {
                _theme = Resources.Load<MedinaUITheme>("MedinaUITheme");
                _themeLoaded = true;
                if (_theme == null)
                    Debug.LogWarning("[MedinaUIKit] Resources/MedinaUITheme not found; falling back to the built-in font.");
            }
            return _theme;
        }
    }

    private static Font Fallback =>
        _fallbackFont != null ? _fallbackFont : (_fallbackFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

    public static Font HeaderFont => Theme != null && Theme.headerFont != null ? Theme.headerFont : Fallback;
    public static Font BodyFont => Theme != null && Theme.bodyFont != null ? Theme.bodyFont : Fallback;
    public static Font BodyStrongFont => Theme != null && Theme.bodyStrongFont != null ? Theme.bodyStrongFont : BodyFont;

    // ---- Sprites -----------------------------------------------------------------------------
    private static Sprite _card, _paper, _print, _pill, _star, _starOutline;

    /// <summary>Die-cut storybook card: white halo, Midnight-navy stroke, cream paper. 9-sliced.</summary>
    public static Sprite CardSprite => _card != null ? _card
        : (_card = BuildLayeredRoundRect("MedinaCard", 96, 20f, new[] { (4f, Color.white), (2f, Navy) }, Cream, 28));

    /// <summary>Journal page: warm paper with a faint ink edge. 9-sliced.</summary>
    public static Sprite PaperSprite => _paper != null ? _paper
        : (_paper = BuildLayeredRoundRect("MedinaPaper", 64, 8f, new[] { (2f, WithAlpha(Ink, 0.38f)) }, Paper, 16));

    /// <summary>Aged photo print border (white-ish paper, hairline edge). 9-sliced.</summary>
    public static Sprite PrintSprite => _print != null ? _print
        : (_print = BuildLayeredRoundRect("MedinaPrint", 48, 3f, new[] { (1.5f, WithAlpha(Ink, 0.30f)) }, PaperLight, 10));

    /// <summary>Plain white rounded pill for tinting. 9-sliced.</summary>
    public static Sprite PillSprite => _pill != null ? _pill
        : (_pill = BuildLayeredRoundRect("MedinaPill", 64, 30f, System.Array.Empty<(float, Color)>(), Color.white, 30));

    /// <summary>Eight-point zellige star (filled).</summary>
    public static Sprite StarSprite => _star != null ? _star : (_star = BuildStar("MedinaStar", 64, outline: false));

    /// <summary>Eight-point zellige star (outline).</summary>
    public static Sprite StarOutlineSprite => _starOutline != null ? _starOutline : (_starOutline = BuildStar("MedinaStarOutline", 64, outline: true));

    // ---- Factories ---------------------------------------------------------------------------

    public static RectTransform CreateRect(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return rt;
    }

    public static Image CreateImage(string name, Transform parent, Sprite sprite, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size, bool sliced = true)
    {
        RectTransform rt = CreateRect(name, parent, anchorMin, anchorMax, pivot, anchoredPos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.type = sliced && sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>Storybook card anchored at a single point (anchor == pivot), with a soft drop shadow.</summary>
    public static Image CreateCard(string name, Transform parent, Vector2 anchor, Vector2 anchoredPos, Vector2 size)
    {
        Image card = CreateImage(name, parent, CardSprite, Color.white, anchor, anchor, anchor, anchoredPos, size);
        AddSoftShadow(card);
        return card;
    }

    /// <summary>Image that stretches over its parent with padding (left, top, right, bottom).</summary>
    public static Image CreateFillImage(string name, Transform parent, Sprite sprite, Color color, Vector4 padding)
    {
        Image img = CreateImage(name, parent, sprite, color, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        img.rectTransform.offsetMin = new Vector2(padding.x, padding.w);
        img.rectTransform.offsetMax = new Vector2(-padding.z, -padding.y);
        return img;
    }

    public static Text CreateText(string name, Transform parent, MedinaTextRole role, string content, int fontSize,
        TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
    {
        RectTransform rt = CreateRect(name, parent, anchorMin, anchorMax, pivot, anchoredPos, size);
        var text = rt.gameObject.AddComponent<Text>();
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = true;
        text.raycastTarget = false;
        ApplyRole(text, role);
        return text;
    }

    /// <summary>Text anchored top-left of its parent (the layout most HUD rows use).</summary>
    public static Text CreateTopLeftText(string name, Transform parent, MedinaTextRole role, string content, int fontSize,
        TextAnchor alignment, Vector2 anchoredPos, Vector2 size)
    {
        Vector2 tl = new(0f, 1f);
        return CreateText(name, parent, role, content, fontSize, alignment, tl, tl, tl, anchoredPos, size);
    }

    /// <summary>Text stretched over its parent with padding (left, top, right, bottom).</summary>
    public static Text CreateFillText(string name, Transform parent, MedinaTextRole role, string content, int fontSize,
        TextAnchor alignment, Vector4 padding)
    {
        Text text = CreateText(name, parent, role, content, fontSize, alignment,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        text.rectTransform.offsetMin = new Vector2(padding.x, padding.w);
        text.rectTransform.offsetMax = new Vector2(-padding.z, -padding.y);
        return text;
    }

    public static void ApplyRole(Text text, MedinaTextRole role)
    {
        if (text == null) return;

        switch (role)
        {
            case MedinaTextRole.Title:
                text.font = HeaderFont;
                text.fontStyle = FontStyle.Normal; // Cinzel Bold is already bold
                text.color = Navy;
                break;
            case MedinaTextRole.Label:
                text.font = HeaderFont;
                text.fontStyle = FontStyle.Normal;
                text.color = Terracotta;
                break;
            case MedinaTextRole.Body:
                text.font = BodyFont;
                text.fontStyle = FontStyle.Normal;
                text.color = Ink;
                break;
            case MedinaTextRole.BodyStrong:
                text.font = BodyStrongFont;
                text.fontStyle = FontStyle.Normal;
                text.color = Ink;
                break;
            case MedinaTextRole.Caption:
                text.font = BodyFont;
                text.fontStyle = FontStyle.Italic;
                text.color = InkMuted;
                break;
            case MedinaTextRole.Hint:
                text.font = BodyFont;
                text.fontStyle = FontStyle.Italic;
                text.color = Terracotta;
                break;
        }
    }

    public static void AddSoftShadow(Graphic graphic, float distance = 4f, float alpha = 0.28f)
    {
        if (graphic == null) return;
        var shadow = graphic.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.08f, 0.06f, 0.05f, alpha);
        shadow.effectDistance = new Vector2(0f, -distance);
        shadow.useGraphicAlpha = true;
    }

    /// <summary>
    /// "Abbass: That's not it." -> speaker name in terracotta small caps style, line in ink (rich text).
    /// Lines without a short "Name:" prefix are returned unchanged.
    /// </summary>
    public static string FormatSpeakerLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return string.Empty;

        int colon = line.IndexOf(':');
        if (colon <= 0 || colon > 24) return line;

        string name = line.Substring(0, colon).Trim();
        if (name.Length == 0 || name.Contains(".") || name.Contains(",")) return line;

        string rest = line.Substring(colon + 1).TrimStart();
        return $"<color=#{ColorUtility.ToHtmlStringRGB(Terracotta)}><b>{name.ToUpperInvariant()}</b></color>   {rest}";
    }

    // ---- Sprite generation -------------------------------------------------------------------

    private static Sprite BuildLayeredRoundRect(string name, int size, float radius, (float width, Color color)[] rings, Color fill, int border)
    {
        var tex = NewTexture(name, size);
        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        float inner = half - 1f; // keep 1px clear for anti-aliasing
        float r = Mathf.Min(radius, inner);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = Mathf.Abs(x + 0.5f - half) - (inner - r);
                float py = Mathf.Abs(y + 0.5f - half) - (inner - r);
                float outside = Mathf.Sqrt(Mathf.Max(px, 0f) * Mathf.Max(px, 0f) + Mathf.Max(py, 0f) * Mathf.Max(py, 0f));
                float d = outside + Mathf.Min(Mathf.Max(px, py), 0f) - r; // < 0 inside
                float coverage = Mathf.Clamp01(0.5f - d);
                float depth = -d;

                // Walk the layers from the outer edge inwards, blending 1px between them
                Color c = rings.Length > 0 ? rings[0].color : fill;
                float edge = 0f;
                for (int i = 0; i < rings.Length; i++)
                {
                    edge += rings[i].width;
                    Color next = i + 1 < rings.Length ? rings[i + 1].color : fill;
                    c = Color.Lerp(c, next, Mathf.Clamp01(depth - edge + 0.5f));
                }

                c.a *= coverage;
                pixels[y * size + x] = c;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }

    private static Sprite BuildStar(string name, int size, bool outline)
    {
        var tex = NewTexture(name, size);
        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        float a = half * 0.64f;            // square half-size: rotated corners reach ~0.9 of the half extent
        const float invSqrt2 = 0.70710678f;
        float stroke = size * 0.07f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f - half;
                float py = y + 0.5f - half;
                float d1 = Mathf.Max(Mathf.Abs(px), Mathf.Abs(py)) - a;
                float rx = (px - py) * invSqrt2;
                float ry = (px + py) * invSqrt2;
                float d2 = Mathf.Max(Mathf.Abs(rx), Mathf.Abs(ry)) - a;
                float d = Mathf.Min(d1, d2); // union of the two squares = khatam star

                float alpha = outline
                    ? Mathf.Clamp01(0.5f - (Mathf.Abs(d + stroke * 0.5f) - stroke * 0.5f))
                    : Mathf.Clamp01(0.5f - d);

                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Texture2D NewTexture(string name, int size) =>
        new(size, size, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
}
