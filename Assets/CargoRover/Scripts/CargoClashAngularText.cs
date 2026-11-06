using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A compact, all-straight-stroke wordmark. It needs no platform font or external texture.
/// Slight per-letter shearing gives Cargo Clash its deliberately uneven cut-metal lettering.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CargoClashAngularText : MaskableGraphic
{
    [SerializeField] private string lettering = "CARGO CLASH";
    [SerializeField, Range(0.07f, 0.18f)] private float strokeWidth = 0.12f;
    [SerializeField, Range(0f, 0.2f)] private float characterSpacing = 0.11f;

    private static readonly Vector4[] Segments =
    {
        new Vector4(0.16f, 0.9f, 0.84f, 0.9f),
        new Vector4(0.12f, 0.86f, 0.08f, 0.54f),
        new Vector4(0.08f, 0.5f, 0.14f, 0.1f),
        new Vector4(0.16f, 0.1f, 0.84f, 0.1f),
        new Vector4(0.88f, 0.86f, 0.86f, 0.54f),
        new Vector4(0.86f, 0.5f, 0.84f, 0.14f),
        new Vector4(0.18f, 0.5f, 0.82f, 0.5f),
        new Vector4(0.12f, 0.1f, 0.46f, 0.9f),
        new Vector4(0.46f, 0.9f, 0.88f, 0.1f),
        new Vector4(0.48f, 0.5f, 0.9f, 0.1f),
    };

    public void SetLettering(string value)
    {
        lettering = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        if (string.IsNullOrEmpty(lettering)) return;

        float units = 0f;
        foreach (char letter in lettering)
            units += letter == ' ' ? 0.56f : 1f + characterSpacing;

        Rect bounds = rectTransform.rect;
        float scaleX = bounds.width / units;
        float scaleY = bounds.height;
        float thickness = strokeWidth * Mathf.Min(scaleX, scaleY);
        float cursor = bounds.xMin;
        int index = 0;

        foreach (char raw in lettering)
        {
            char letter = char.ToUpperInvariant(raw);
            if (letter == ' ')
            {
                cursor += 0.56f * scaleX;
                continue;
            }

            int[] strokes = StrokesFor(letter);
            float skew = ((index * 7) % 5 - 2) * 0.025f;
            float rise = ((index * 3) % 4 - 1.5f) * 0.025f;
            foreach (int segmentIndex in strokes)
            {
                Vector4 segment = Segments[segmentIndex];
                Vector2 start = Point(segment.x, segment.y, cursor, skew, rise, bounds, scaleX, scaleY);
                Vector2 end = Point(segment.z, segment.w, cursor, skew, rise, bounds, scaleX, scaleY);
                AddStroke(vertexHelper, start, end, thickness, color);
            }

            cursor += (1f + characterSpacing) * scaleX;
            index++;
        }
    }

    private static int[] StrokesFor(char letter)
    {
        switch (letter)
        {
            case 'C': return new[] { 0, 1, 2, 3 };
            case 'A': return new[] { 7, 8, 6 };
            case 'R': return new[] { 0, 1, 2, 4, 6, 9 };
            case 'G': return new[] { 0, 1, 2, 3, 5, 6 };
            case 'O': return new[] { 0, 1, 2, 3, 4, 5 };
            case 'L': return new[] { 1, 2, 3 };
            case 'S': return new[] { 0, 1, 6, 5, 3 };
            case 'H': return new[] { 1, 2, 4, 5, 6 };
            default: return new[] { 0, 3 };
        }
    }

    private static Vector2 Point(float x, float y, float cursor, float skew, float rise,
        Rect bounds, float scaleX, float scaleY)
    {
        return new Vector2(cursor + (x + skew * (y - 0.5f)) * scaleX,
            bounds.yMin + (y + rise) * scaleY);
    }

    private static void AddStroke(VertexHelper vertexHelper, Vector2 from, Vector2 to,
        float thickness, Color tint)
    {
        Vector2 normal = new Vector2(-(to.y - from.y), to.x - from.x).normalized * (thickness * 0.5f);
        int first = vertexHelper.currentVertCount;
        vertexHelper.AddVert(from - normal, tint, Vector2.zero);
        vertexHelper.AddVert(from + normal, tint, Vector2.zero);
        vertexHelper.AddVert(to + normal, tint, Vector2.zero);
        vertexHelper.AddVert(to - normal, tint, Vector2.zero);
        vertexHelper.AddTriangle(first, first + 1, first + 2);
        vertexHelper.AddTriangle(first, first + 2, first + 3);
    }
}
