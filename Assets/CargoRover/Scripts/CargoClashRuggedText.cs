using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gives a complete bold font a subtle, hand-set industrial rhythm without
/// breaking its glyphs into separate graphic pieces.
/// </summary>
[RequireComponent(typeof(Text))]
public sealed class CargoClashRuggedText : BaseMeshEffect
{
    public override void ModifyMesh(VertexHelper vertices)
    {
        if (!IsActive() || vertices.currentVertCount < 4) return;

        // UI Shadow converts Text's quads to triangle-stream vertices first.
        // Transform complete six-vertex glyphs so triangle seams remain intact.
        var stream = new List<UIVertex>();
        vertices.GetUIVertexStream(stream);
        int groupCount = stream.Count / 6;
        bool hasShadow = GetComponent<Shadow>() != null && GetComponent<Shadow>().enabled;
        int glyphCount = hasShadow ? groupCount / 2 : groupCount;
        if (glyphCount == 0) return;
        for (int group = 0; group < groupCount; group++)
        {
            int glyph = group % glyphCount;
            // Deterministic per-glyph offsets keep the lettering readable and
            // stable during animation while avoiding a mechanically even line.
            float height = (Mathf.Repeat(glyph * 0.618f, 1f) - 0.5f) * 16f;
            float lean = (Mathf.Repeat(glyph * 0.37f, 1f) - 0.5f) * 0.1f;
            float xNudge = (Mathf.Repeat(glyph * 0.73f, 1f) - 0.5f) * 4f;

            float centerY = 0f;
            for (int corner = 0; corner < 6; corner++)
                centerY += stream[group * 6 + corner].position.y;
            centerY /= 6f;

            for (int corner = 0; corner < 6; corner++)
            {
                int index = group * 6 + corner;
                UIVertex vertex = stream[index];
                Vector3 position = vertex.position;
                position.x += xNudge + (position.y - centerY) * lean;
                position.y += height;
                vertex.position = position;
                stream[index] = vertex;
            }
        }
        vertices.Clear();
        vertices.AddUIVertexTriangleStream(stream);
    }
}
