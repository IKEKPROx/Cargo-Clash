using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps the face sliders of Astra's appearance files (Crew_*_Visual.fbx) as small as she made them.
///
/// Her bodies carry a UV map for the cloth's weave, so Unity calculates tangents, and it calculates them again for every
/// blend shape. That gives a tiny tangent difference on every vertex of the body, so each of the ten sliders stored all
/// ~100,000 vertices although only 500 to 6,500 of them move. With the sliders set (every bot has a face) the game
/// blended the whole body ten times a frame and ran at about half its frame rate.
///
/// A vertex that neither moves nor turns its normal under a slider keeps its shape, so its tangent stays the one the
/// body already has. Those vertices are dropped from the slider. "Doesn't move" allows the float noise her export and
/// Unity's recalculation leave behind: under 0.01 mm, and a normal turned by under 0.006 degrees. Every vertex that
/// really moves is kept as she made it.
/// </summary>
public sealed class CrewBlendShapeCleanup : AssetPostprocessor
{
    private const float StillMove = 1e-5f;    // metres
    private const float StillNormal = 1e-4f;  // change of a unit normal

    public override uint GetVersion() => 2;

    private void OnPostprocessModel(GameObject root)
    {
        string file = System.IO.Path.GetFileName(assetPath);
        if (!file.StartsWith("Crew_") || !file.EndsWith("_Visual.fbx")) return;
        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.sharedMesh != null && renderer.sharedMesh.blendShapeCount > 0) Clean(renderer.sharedMesh);
        }
    }

    private static void Clean(Mesh mesh)
    {
        int count = mesh.vertexCount;
        var shapes = new List<(string name, List<(float weight, Vector3[] moves, Vector3[] normals, Vector3[] tangents)> frames)>();
        int dropped = 0;
        float largestMove = 0f, largestTurn = 0f;
        for (int shape = 0; shape < mesh.blendShapeCount; shape++)
        {
            var frames = new List<(float, Vector3[], Vector3[], Vector3[])>();
            for (int frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
            {
                var moves = new Vector3[count];
                var normals = new Vector3[count];
                var tangents = new Vector3[count];
                mesh.GetBlendShapeFrameVertices(shape, frame, moves, normals, tangents);
                for (int vertex = 0; vertex < count; vertex++)
                {
                    if (moves[vertex].sqrMagnitude > StillMove * StillMove || normals[vertex].sqrMagnitude > StillNormal * StillNormal) continue;
                    if (IsZero(moves[vertex]) && IsZero(normals[vertex]) && IsZero(tangents[vertex])) continue;
                    largestMove = Mathf.Max(largestMove, moves[vertex].magnitude);
                    largestTurn = Mathf.Max(largestTurn, normals[vertex].magnitude);
                    moves[vertex] = normals[vertex] = tangents[vertex] = Vector3.zero;
                    dropped++;
                }
                frames.Add((mesh.GetBlendShapeFrameWeight(shape, frame), moves, normals, tangents));
            }
            shapes.Add((mesh.GetBlendShapeName(shape), frames));
        }
        if (dropped == 0) return;

        mesh.ClearBlendShapes();
        foreach (var (name, frames) in shapes)
        {
            foreach (var (weight, moves, normals, tangents) in frames) mesh.AddBlendShapeFrame(name, weight, moves, normals, tangents);
        }
        Debug.Log($"[Crew] {mesh.name}: dropped {dropped} still vertices from {mesh.blendShapeCount} face sliders " +
                  $"(largest move {largestMove * 1000f:0.0000} mm, largest normal change {largestTurn:0.000000})");
    }

    private static bool IsZero(Vector3 v) => v.x == 0f && v.y == 0f && v.z == 0f;
}
