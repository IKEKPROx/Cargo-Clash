using UnityEngine;
using UnityEngine.UI;

/// <summary>A clipped-corner metal-plate silhouette with a crisp white keyline.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CargoClashButtonFrame : MaskableGraphic
{
    [SerializeField] private float cornerCut = 12f;
    [SerializeField] private float lineWidth = 3f;
    [SerializeField] private Color fillColor = new Color(0.035f, 0.055f, 0.075f, 0.78f);

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect rect = rectTransform.rect;
        float cut = Mathf.Min(cornerCut, Mathf.Min(rect.width, rect.height) * 0.2f);
        Vector2[] corners =
        {
            new Vector2(rect.xMin + cut, rect.yMin),
            new Vector2(rect.xMax - cut, rect.yMin),
            new Vector2(rect.xMax, rect.yMin + cut),
            new Vector2(rect.xMax, rect.yMax - cut),
            new Vector2(rect.xMax - cut, rect.yMax),
            new Vector2(rect.xMin + cut, rect.yMax),
            new Vector2(rect.xMin, rect.yMax - cut),
            new Vector2(rect.xMin, rect.yMin + cut),
        };

        Color fill = fillColor;
        for (int i = 0; i < corners.Length; i++)
            vertexHelper.AddVert(corners[i], fill, Vector2.zero);
        for (int i = 1; i < corners.Length - 1; i++)
            vertexHelper.AddTriangle(0, i, i + 1);

        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 from = corners[i];
            Vector2 to = corners[(i + 1) % corners.Length];
            Vector2 inward = new Vector2(-(to.y - from.y), to.x - from.x).normalized * lineWidth;
            int first = vertexHelper.currentVertCount;
            vertexHelper.AddVert(from, color, Vector2.zero);
            vertexHelper.AddVert(to, color, Vector2.zero);
            vertexHelper.AddVert(to + inward, color, Vector2.zero);
            vertexHelper.AddVert(from + inward, color, Vector2.zero);
            vertexHelper.AddTriangle(first, first + 1, first + 2);
            vertexHelper.AddTriangle(first, first + 2, first + 3);
        }
    }
}
