using TMPro;
using UnityEngine;

/// <summary>
/// A HUD side panel (e.g. ROUND / BEST ROUND) that sits next to the maze in landscape and, in portrait,
/// becomes a wide, short strip above the score row: its children are stacked title/value pairs,
/// and each pair turns into a column
///
///   landscape     portrait
///   ┌───────┐     ┌─────────────────────────┐
///   │ ROUND │     │  ROUND     BEST ROUND   │
///   │  01   │     │   01           05       │
///   │ BEST  │     └─────────────────────────┘
///   │ ROUND │
///   │  05   │
///   └───────┘
/// The landscape layout is captured on creation and restored exactly when going back
/// </summary>
public class PortraitPanel
{
    private const float ColumnWidth = 140f; // canvas units
    private const float RowHeight = 24f;
    private const float Padding = 8f;
    private const float MinFontScale = 0.6f; // long titles shrink instead of wrapping

    private struct ChildLayout
    {
        public RectTransform rect;
        public Vector2 position, size, anchorMin, anchorMax;
        public TMP_Text text;
        public TextWrappingModes wrapping;
        public bool autoSize;
        public float fontSize, fontMin, fontMax;
    }

    private readonly RectTransform panel;
    private readonly RectTransform canvas;
    private readonly Vector2 landscapePosition, landscapeSize;
    private readonly ChildLayout[] children;

    public PortraitPanel(RectTransform panel, RectTransform canvas)
    {
        this.panel = panel;
        this.canvas = canvas;
        landscapePosition = panel.anchoredPosition;
        landscapeSize = panel.sizeDelta;

        children = new ChildLayout[panel.childCount];
        for (int i = 0; i < children.Length; i++)
        {
            var rect = (RectTransform)panel.GetChild(i);
            var text = rect.GetComponent<TMP_Text>();
            children[i] = new ChildLayout
            {
                rect = rect,
                position = rect.anchoredPosition,
                size = rect.sizeDelta,
                anchorMin = rect.anchorMin,
                anchorMax = rect.anchorMax,
                text = text,
                wrapping = text != null ? text.textWrappingMode : default,
                autoSize = text != null && text.enableAutoSizing,
                fontSize = text != null ? text.fontSize : 0f,
                fontMin = text != null ? text.fontSizeMin : 0f,
                fontMax = text != null ? text.fontSizeMax : 0f,
            };
        }
    }

    public bool IsValid => panel != null;

    private int Columns => (children.Length + 1) / 2;
    private Vector2 PortraitSize => new(Columns * ColumnWidth + 2f * Padding, 2f * RowHeight + 2f * Padding);

    /// <summary>Panel height in portrait, in canvas units (to make room for it)</summary>
    public float PortraitHeight => PortraitSize.y;

    public void ApplyLandscape()
    {
        panel.sizeDelta = landscapeSize;
        panel.anchoredPosition = landscapePosition;

        foreach (var c in children)
        {
            if (c.rect == null) continue;
            c.rect.anchorMin = c.anchorMin;
            c.rect.anchorMax = c.anchorMax;
            c.rect.anchoredPosition = c.position;
            c.rect.sizeDelta = c.size;
            if (c.text == null) continue;
            c.text.textWrappingMode = c.wrapping;
            c.text.enableAutoSizing = c.autoSize;
            c.text.fontSize = c.fontSize;
            c.text.fontSizeMin = c.fontMin;
            c.text.fontSizeMax = c.fontMax;
        }
    }

    /// <summary>
    /// Lays the panel out as a strip, centred horizontally, with its bottom edge at bottomY (canvas space)
    /// </summary>
    public void ApplyPortrait(float bottomY)
    {
        var size = PortraitSize;
        panel.sizeDelta = size;

        for (int i = 0; i < children.Length; i++)
        {
            var c = children[i];
            if (c.rect == null) continue;

            int column = i / 2;
            bool isTitle = i % 2 == 0;
            float x = -size.x / 2f + Padding + ColumnWidth * (column + 0.5f);
            float y = isTitle ? RowHeight / 2f : -RowHeight / 2f;

            // Children are centre-anchored in the panel; keep them that way so the offsets are from its centre
            c.rect.anchorMin = c.rect.anchorMax = new Vector2(0.5f, 0.5f);
            c.rect.sizeDelta = new Vector2(ColumnWidth, RowHeight);
            c.rect.anchoredPosition = new Vector2(x, y) + (c.rect.pivot - new Vector2(0.5f, 0.5f)) * c.rect.sizeDelta;

            if (c.text == null) continue;
            c.text.textWrappingMode = TextWrappingModes.NoWrap;
            c.text.fontSizeMax = c.fontSize;
            c.text.fontSizeMin = c.fontSize * MinFontScale;
            c.text.enableAutoSizing = true;
        }

        // Canvas-space target for the panel's pivot, converted through world space into its parent's space
        var pivot = panel.pivot;
        var target = canvas.TransformPoint(new Vector3((pivot.x - 0.5f) * size.x, bottomY + pivot.y * size.y, 0f));
        target.z = panel.position.z;
        panel.position = target;
    }
}
