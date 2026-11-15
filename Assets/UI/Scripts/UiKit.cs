// UiKit.cs — Elementis UI construction helpers (Phase 2b).
//
// The whole UI is built at runtime in C# (no hand-authored UGUI scene
// YAML): UIFlowManager creates one Screen Space - Overlay canvas and
// builds every screen from these primitives. That keeps the scene edit
// to a single GameObject (see Main.unity, "UI") and makes the layout
// verifiable by inspection — every control's size/position is a named
// constant in TouchControls.cs / FightHud.cs / UIFlowManager.cs, and the
// numbers are tabulated in Assets/UI/UI-NOTES.md.
//
// Font: the built-in Arial (Resources.GetBuiltinResource<Font>).
// No TextMeshPro in the project, no downloaded fonts — free only.
using UnityEngine;
using UnityEngine.UI;

public static class UiKit
{
    // Elementis palette (gold ornate title style, approved lobby look).
    public static readonly Color Gold = new Color(0.91f, 0.71f, 0.30f);
    public static readonly Color GoldDark = new Color(0.62f, 0.44f, 0.12f);
    public static readonly Color PanelDark = new Color(0.07f, 0.06f, 0.05f, 0.86f);
    public static readonly Color PanelDarkSolid = new Color(0.07f, 0.06f, 0.05f, 1f);
    public static readonly Color KataraBlue = new Color(0.35f, 0.75f, 0.95f);
    public static readonly Color KataraDeep = new Color(0.12f, 0.30f, 0.58f);
    public static readonly Color AzulaRed = new Color(0.82f, 0.14f, 0.12f);
    public static readonly Color AzulaBlueFire = new Color(0.30f, 0.45f, 1.00f);
    public static readonly Color HealthWhite = Color.white;
    public static readonly Color ManaBlue = new Color(0.25f, 0.55f, 1.00f);
    public static readonly Color EnemyRed = new Color(0.85f, 0.12f, 0.10f);
    public static readonly Color ButtonGrey = new Color(0.16f, 0.15f, 0.14f, 0.92f);
    public static readonly Color DisabledGrey = new Color(0.35f, 0.35f, 0.35f, 0.75f);

    static Font defaultFont;
    static Sprite circleSprite;
    static Sprite squareSprite;

    public static Font DefaultFont
    {
        get
        {
            if (defaultFont == null)
                defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (defaultFont == null)
                Debug.LogWarning("[UiKit] built-in Arial font not found — texts may render empty.");
            return defaultFont;
        }
    }

    // A soft-edged filled circle, generated once (no texture assets).
    public static Sprite Circle
    {
        get
        {
            if (circleSprite == null)
            {
                const int s = 128;
                var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                var px = new Color[s * s];
                float c = (s - 1) / 2f;
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                        float a = Mathf.Clamp01((1f - d) * 8f); // 1px-ish soft edge
                        px[y * s + x] = new Color(1f, 1f, 1f, a);
                    }
                tex.SetPixels(px);
                tex.Apply();
                circleSprite = Sprite.Create(tex, new Rect(0, 0, s, s),
                    new Vector2(0.5f, 0.5f), 100f);
            }
            return circleSprite;
        }
    }

    // 1x1 white square (tinted by the Image colour) for bars and panels.
    public static Sprite Square
    {
        get
        {
            if (squareSprite == null)
            {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Color[16];
                for (int i = 0; i < px.Length; i++) px[i] = Color.white;
                tex.SetPixels(px);
                tex.Apply();
                squareSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4),
                    new Vector2(0.5f, 0.5f), 100f);
            }
            return squareSprite;
        }
    }

    // --- RectTransform plumbing ------------------------------------------------
    // anchorMin/Max in 0..1 of the parent; anchoredPos in reference px from
    // the anchor point; size in reference px. All layout constants in the
    // UI scripts are expressed in this same 1920x1080 reference space.
    public static RectTransform Rect(GameObject go, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPos, Vector2 size, Vector2 pivot)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        return rt;
    }

    // Full-stretch rect (fills the parent).
    public static RectTransform Stretch(GameObject go)
    {
        return Rect(go, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            new Vector2(0.5f, 0.5f));
    }

    // --- Elements ---------------------------------------------------------------
    public static GameObject Panel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = Square;
        img.color = color;
        img.raycastTarget = false;
        Stretch(go);
        go.SetActive(false); // screens start hidden; the flow manager shows one
        return go;
    }

    // A non-interactive coloured rect (decorative plate, bar background...).
    public static Image Block(Transform parent, string name, Color color, bool raycast = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = Square;
        img.color = color;
        img.raycastTarget = raycast;
        return img;
    }

    public static Text Label(Transform parent, string name, string text, int fontSize,
        Color color, TextAnchor align = TextAnchor.MiddleCenter)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = DefaultFont;
        t.text = text;
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    // A button: circular or square background + centred label. Returns the
    // Button; the label Text is available via GetComponentInChildren<Text>().
    // The background Image is the raycast target; the label never blocks.
    public static Button MakeButton(Transform parent, string name, string label,
        Vector2 size, Color bg, int fontSize = 32, Color? textColor = null,
        bool circular = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = circular ? Circle : Square;
        img.color = bg;
        img.raycastTarget = true;
        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.ColorTint;
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
        colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        colors.colorMultiplier = 1f;
        btn.colors = colors;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        var t = Label(go.transform, "Label", label, fontSize,
            textColor ?? Color.white);
        Stretch(t.gameObject);
        return btn;
    }

    // Horizontal fill bar: returns (background, fill). Fill is a Filled
    // horizontal image; set fill.fillAmount 0..1. The background is NOT a
    // raycast target.
    public static (Image bg, Image fill) Bar(Transform parent, string name,
        Vector2 size, Color bgColor, Color fillColor)
    {
        var bgGo = new GameObject(name + "Bg");
        bgGo.transform.SetParent(parent, false);
        var bg = bgGo.AddComponent<Image>();
        bg.sprite = Square;
        bg.color = bgColor;
        bg.raycastTarget = false;
        bgGo.GetComponent<RectTransform>().sizeDelta = size;
        var fillGo = new GameObject(name + "Fill");
        fillGo.transform.SetParent(bgGo.transform, false);
        var fill = fillGo.AddComponent<Image>();
        fill.sprite = Square;
        fill.color = fillColor;
        fill.raycastTarget = false;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;
        Stretch(fillGo);
        return (bg, fill);
    }

    // Gold text with a dark offset shadow copy (the "ornate gold title"
    // treatment — two Text layers in one wrapper, no font asset needed).
    // The caller positions the wrapper; the shadow sits (+4,-4) behind.
    public static GameObject GoldTitle(Transform parent, string name, string text,
        int fontSize)
    {
        var wrap = new GameObject(name);
        wrap.transform.SetParent(parent, false);
        var shadow = Label(wrap.transform, "Shadow", text, fontSize,
            new Color(0.25f, 0.08f, 0.02f, 1f));
        Stretch(shadow.gameObject);
        shadow.rectTransform.anchoredPosition = new Vector2(4f, -4f);
        var main = Label(wrap.transform, "Main", text, fontSize, Gold);
        main.fontStyle = FontStyle.Bold;
        Stretch(main.gameObject);
        return wrap;
    }
}
