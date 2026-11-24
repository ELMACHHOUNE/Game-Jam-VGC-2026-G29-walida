using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class VNShowcaseHarness : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PanelRenderer panelRenderer;
    [SerializeField] private VNStageView stageView;
    [SerializeField] private VNStoryController storyController;

    [Header("Options")]
    [SerializeField] private bool generatePlaceholderAssets = true;
    [SerializeField] private bool showDebugToolbar = true;

    private readonly List<Object> _generatedAssets = new();
    private VisualElement _toolbar;

    // Ad-hoc test toggles
    private int _bgToggleIndex;
    private int _ayaExprIndex;
    private bool _kenjiVisible;
    private bool _cgVisible;

    private static readonly string[] BgIds = { "bg_day", "bg_night", "bg_hallway" };
    private static readonly string[] Expressions = { "neutral", "smile", "angry" };

    private void Awake()
    {
        if (generatePlaceholderAssets)
            CreateAndRegisterPlaceholders();
    }

    private void OnEnable()
    {
        if (showDebugToolbar && panelRenderer != null)
            panelRenderer.RegisterUIReloadCallback(BuildDebugToolbar);
    }

    private void OnDisable()
    {
        if (panelRenderer != null)
            panelRenderer.UnregisterUIReloadCallback(BuildDebugToolbar);
    }

    private void OnDestroy()
    {
        foreach (var asset in _generatedAssets)
        {
            if (asset != null) Destroy(asset);
        }
        _generatedAssets.Clear();
    }

    // =========================================================================
    // PROCEDURAL PLACEHOLDER ASSETS (FullRect Sprites for UI Toolkit)
    // =========================================================================

    private void CreateAndRegisterPlaceholders()
    {
        // 1. Backgrounds (1920x1080 aspect ratio scaled down to fast 640x360 textures)
        storyController.RegisterBackground("bg_day", CreateGradientSprite(
            640, 360, new Color(0.28f, 0.52f, 0.82f), new Color(0.76f, 0.86f, 0.96f), addGrid: false));

        storyController.RegisterBackground("bg_night", CreateGradientSprite(
            640, 360, new Color(0.05f, 0.07f, 0.16f), new Color(0.14f, 0.18f, 0.34f), addGrid: false));

        storyController.RegisterBackground("bg_hallway", CreateGradientSprite(
            640, 360, new Color(0.18f, 0.20f, 0.22f), new Color(0.32f, 0.34f, 0.36f), addGrid: true));

        // 2. Event CG (Framed warm amber illustration placeholder)
        storyController.RegisterEventCG("cg_discovery", CreateFramedCGSprite(
            640, 360, new Color(0.65f, 0.38f, 0.14f), new Color(0.95f, 0.78f, 0.35f)));

        // 3. Characters (Portrait silhouettes with distinct colors per expression)
        stageView.RegisterCharacter(CreateCharacter("aya", "Aya", new Color(0.46f, 0.72f, 1.0f), new[]
        {
            ("neutral", new Color(0.24f, 0.44f, 0.72f)),
            ("smile",   new Color(0.22f, 0.66f, 0.68f)),
            ("angry",   new Color(0.78f, 0.24f, 0.30f))
        }));

        stageView.RegisterCharacter(CreateCharacter("kenji", "Kenji", new Color(0.96f, 0.68f, 0.36f), new[]
        {
            ("neutral", new Color(0.42f, 0.38f, 0.56f)),
            ("smile",   new Color(0.36f, 0.62f, 0.42f)),
            ("angry",   new Color(0.72f, 0.36f, 0.18f))
        }));
    }

    private VNCharacterData CreateCharacter(string id, string displayName, Color nameColor, (string key, Color color)[] exprs)
    {
        var data = ScriptableObject.CreateInstance<VNCharacterData>();
        data.characterId = id;
        data.displayName = displayName;
        data.nameColor = nameColor;

        foreach (var (key, color) in exprs)
        {
            data.expressions.Add(new VNCharacterData.ExpressionEntry
            {
                key = key,
                sprite = CreatePortraitSprite(340, 500, color)
            });
        }

        _generatedAssets.Add(data);
        return data;
    }

    private Sprite CreatePortraitSprite(int width, int height, Color bodyColor)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
        var pixels = new Color[width * height];

        int centerX = width / 2;
        int headCenterY = (int)(height * 0.76f);
        int headRadius = (int)(width * 0.20f);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;

                // Head circle
                float dx = x - centerX;
                float dy = y - headCenterY;
                bool inHead = (dx * dx + dy * dy) <= (headRadius * headRadius);

                // Torso trapezoid
                bool inBody = y < (int)(height * 0.62f) &&
                              Mathf.Abs(x - centerX) <= (width * 0.34f);

                if (inHead || inBody)
                {
                    bool isBorder = (inHead && (dx * dx + dy * dy) > ((headRadius - 4) * (headRadius - 4))) ||
                                    (inBody && (y >= (int)(height * 0.62f) - 4 || Mathf.Abs(x - centerX) >= (width * 0.34f) - 4));

                    pixels[idx] = isBorder ? Color.white : bodyColor;
                }
                else
                {
                    pixels[idx] = Color.clear;
                }
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        _generatedAssets.Add(tex);

        var sprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        _generatedAssets.Add(sprite);
        return sprite;
    }

    private Sprite CreateGradientSprite(int width, int height, Color top, Color bottom, bool addGrid)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
        var pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            float t = (float)y / height;
            Color rowColor = Color.Lerp(bottom, top, t);

            for (int x = 0; x < width; x++)
            {
                Color c = rowColor;
                if (addGrid && (x % 40 == 0 || y % 40 == 0))
                    c *= 1.18f;

                pixels[y * width + x] = c;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        _generatedAssets.Add(tex);

        var sprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        _generatedAssets.Add(sprite);
        return sprite;
    }

    private Sprite CreateFramedCGSprite(int width, int height, Color outer, Color inner)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
        var pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool border = x < 24 || x >= width - 24 || y < 24 || y >= height - 24;
                float radial = Vector2.Distance(new Vector2(x, y), new Vector2(width * 0.5f, height * 0.5f)) / (width * 0.5f);
                pixels[y * width + x] = border ? Color.white : Color.Lerp(inner, outer, Mathf.Clamp01(radial));
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        _generatedAssets.Add(tex);

        var sprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        _generatedAssets.Add(sprite);
        return sprite;
    }

    // =========================================================================
    // RUNTIME UI TOOLKIT DEBUG TOOLBAR
    // =========================================================================

    private void BuildDebugToolbar(PanelRenderer pr, VisualElement root, int version)
    {
        if (root == null) return;

        _toolbar?.RemoveFromHierarchy();
        _toolbar = new VisualElement();
        _toolbar.pickingMode = PickingMode.Position;
        _toolbar.style.position = Position.Absolute;
        _toolbar.style.top = 10;
        _toolbar.style.left = 10;
        _toolbar.style.right = 10;
        _toolbar.style.flexDirection = FlexDirection.Row;
        _toolbar.style.flexWrap = Wrap.Wrap;
        _toolbar.style.paddingTop = 6;
        _toolbar.style.paddingBottom = 6;
        _toolbar.style.paddingLeft = 10;
        _toolbar.style.paddingRight = 10;
        _toolbar.style.backgroundColor = new Color(0.05f, 0.06f, 0.09f, 0.85f);
        _toolbar.style.borderBottomWidth = 1;
        _toolbar.style.borderBottomColor = new Color(1f, 1f, 1f, 0.2f);
        _toolbar.style.borderTopLeftRadius = 6;
        _toolbar.style.borderTopRightRadius = 6;
        _toolbar.style.borderBottomLeftRadius = 6;
        _toolbar.style.borderBottomRightRadius = 6;

        // Section 1: Ink Knot Jumpers
        AddToolbarLabel("KNOTS:");
        AddToolbarButton("1: Solo", () => storyController.JumpToKnot("solo_showcase"));
        AddToolbarButton("2: Dual Slide", () => storyController.JumpToKnot("dual_character_showcase"));
        AddToolbarButton("3: BG & CG", () => storyController.JumpToKnot("cg_showcase"));
        AddToolbarButton("4: Rich Text", () => storyController.JumpToKnot("richtext_showcase"));
        AddToolbarButton("5: Choices", () => storyController.JumpToKnot("branching_showcase"));

        // Section 2: Direct Stage Overrides
        AddToolbarLabel("  |  STAGE:");
        AddToolbarButton("Swap Aya Expr", () =>
        {
            _ayaExprIndex = (_ayaExprIndex + 1) % Expressions.Length;
            stageView.ShowCharacter("aya", Expressions[_ayaExprIndex]);
            stageView.SetActiveSpeaker("aya");
        });

        AddToolbarButton("Toggle Kenji", () =>
        {
            _kenjiVisible = !_kenjiVisible;
            if (_kenjiVisible)
            {
                stageView.ShowCharacter("kenji", "neutral");
                stageView.SetActiveSpeaker("kenji");
            }
            else
            {
                stageView.HideCharacter("kenji");
            }
        });

        AddToolbarButton("Next BG", () =>
        {
            _bgToggleIndex = (_bgToggleIndex + 1) % BgIds.Length;
            // Triggers via Ink tag parser helper or direct stage call
            storyController.JumpToKnot(BgIds[_bgToggleIndex] == "bg_night" ? "cg_showcase" : "solo_showcase");
        });

        AddToolbarButton("Toggle CG", () =>
        {
            _cgVisible = !_cgVisible;
            if (_cgVisible)
                storyController.JumpToKnot("cg_showcase");
            else
                stageView.HideEventCG();
        });

        root.Add(_toolbar);
    }

    private void AddToolbarLabel(string text)
    {
        var lbl = new Label(text);
        lbl.style.color = new Color(0.65f, 0.72f, 0.85f);
        lbl.style.fontSize = 12;
        lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
        lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
        lbl.style.marginRight = 6;
        _toolbar.Add(lbl);
    }

    private void AddToolbarButton(string text, System.Action onClick)
    {
        var btn = new Button(onClick) { text = text };
        btn.style.fontSize = 12;
        btn.style.paddingLeft = 8;
        btn.style.paddingRight = 8;
        btn.style.paddingTop = 3;
        btn.style.paddingBottom = 3;
        btn.style.marginRight = 4;
        btn.style.backgroundColor = new Color(0.16f, 0.20f, 0.28f);
        btn.style.color = Color.white;
        btn.style.borderTopLeftRadius = 4;
        btn.style.borderTopRightRadius = 4;
        btn.style.borderBottomLeftRadius = 4;
        btn.style.borderBottomRightRadius = 4;
        _toolbar.Add(btn);
    }
}