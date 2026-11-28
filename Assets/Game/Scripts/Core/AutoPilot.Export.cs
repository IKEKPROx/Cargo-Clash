using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// -ccExportLevel folder: once the ruin level is built and playing, writes every visible level mesh as it
    /// stands in the game (after the clearing, the hidden walls, the built road and stones) to MayaRuins.obj and
    /// .mtl, in Blender's axes (x east, y north, z up), and the cargo and truck placements to placements.json.
    /// Tools/blender/build_ruin_delivery.py turns that into the .blend handed over. Tooling only.
    /// </summary>
    public sealed partial class AutoPilot
    {
        private string exportDirectory;

        private static string F(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        private IEnumerator ExportLevel()
        {
            while (MatchDirector.Instance == null || !MatchDirector.Instance.IsSpawned ||
                   MatchDirector.Instance.Phase.Value != MatchPhase.Playing || RuinCourtyard.Instance == null)
                yield return null;
            yield return new WaitForSeconds(1.5f);
            Directory.CreateDirectory(exportDirectory);
            var materials = new Dictionary<Material, string>();
            var skipped = new List<string>();
            int meshes = 0, triangles = 0, vertexBase = 1;
            using (var obj = new StreamWriter(Path.Combine(exportDirectory, "MayaRuins.obj"), false, new UTF8Encoding(false), 1 << 20))
            {
                obj.WriteLine("# Maya Ruins, exported from the running game. x east, y north, z up, metres.");
                obj.WriteLine("mtllib MayaRuins.mtl");
                foreach (MeshRenderer renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    int layer = renderer.gameObject.layer;
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || layer == GameLayers.Player ||
                        layer == GameLayers.ViewModel || layer == GameLayers.Debris || layer == GameLayers.Preview || layer == 5)
                        continue;
                    if (renderer.GetComponentInParent<CargoItem>() != null) continue;   // placed from placements.json
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    Mesh mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh == null) continue;
                    if (!mesh.isReadable)
                    {
                        skipped.Add(renderer.transform.root.name + "/" + renderer.name);
                        continue;
                    }
                    Matrix4x4 toWorld = renderer.localToWorldMatrix;
                    Vector3[] vertices = mesh.vertices;
                    Vector2[] uv = mesh.uv;
                    Color[] colours = mesh.colors;
                    bool hasUv = uv.Length == vertices.Length, hasColour = colours.Length == vertices.Length;
                    obj.WriteLine($"o {Clean(renderer.name)}_{meshes}");
                    var line = new StringBuilder(96);
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 p = toWorld.MultiplyPoint3x4(vertices[i]);
                        line.Clear().Append("v ").Append(F(p.x)).Append(' ').Append(F(p.z)).Append(' ').Append(F(p.y));
                        if (hasColour)
                            line.Append(' ').Append(F(colours[i].r)).Append(' ').Append(F(colours[i].g)).Append(' ').Append(F(colours[i].b));
                        obj.WriteLine(line.ToString());
                    }
                    if (hasUv)
                        foreach (Vector2 t in uv) obj.WriteLine($"vt {F(t.x)} {F(t.y)}");
                    Material[] shared = renderer.sharedMaterials;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        Material material = shared.Length > 0 ? shared[Mathf.Min(s, shared.Length - 1)] : null;
                        if (material != null && !materials.ContainsKey(material))
                            materials[material] = $"{Clean(material.name)}_{materials.Count}";
                        obj.WriteLine("usemtl " + (material != null ? materials[material] : "Missing"));
                        int[] tris = mesh.GetTriangles(s);
                        // Unity is left-handed: swapping y and z mirrors, so each triangle turns the other way.
                        for (int t = 0; t < tris.Length; t += 3)
                        {
                            int a = vertexBase + tris[t], b = vertexBase + tris[t + 1], c = vertexBase + tris[t + 2];
                            obj.WriteLine(hasUv ? $"f {a}/{a} {c}/{c} {b}/{b}" : $"f {a} {c} {b}");
                        }
                        triangles += tris.Length / 3;
                    }
                    vertexBase += vertices.Length;
                    meshes++;
                }
            }
            using (var mtl = new StreamWriter(Path.Combine(exportDirectory, "MayaRuins.mtl"), false, new UTF8Encoding(false)))
            {
                foreach (KeyValuePair<Material, string> entry in materials)
                {
                    Material m = entry.Key;
                    Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.color : Color.white;
                    mtl.WriteLine("newmtl " + entry.Value);
                    mtl.WriteLine($"Kd {F(c.r)} {F(c.g)} {F(c.b)}");
                    mtl.WriteLine($"d {F(c.a)}");
                    Texture texture = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                    if (texture != null && !texture.name.StartsWith("Unity", System.StringComparison.Ordinal) && texture.name.Length > 0)
                        mtl.WriteLine("map_Kd " + texture.name + ".png");
                    if (m.HasProperty("_UseVertexColor") && m.GetFloat("_UseVertexColor") > .5f) mtl.WriteLine("# vertexcolor");
                    mtl.WriteLine();
                }
            }
            var json = new StringBuilder("{\n  \"cargo\": [\n");
            int n = 0;
            foreach (CargoItem cargo in CargoItem.All)
            {
                Vector3 p = cargo.transform.position, e = cargo.transform.eulerAngles;
                json.Append(n++ > 0 ? ",\n" : "").Append($"    {{\"kind\": \"{cargo.Definition.kind}\", \"value\": {cargo.Definition.value}, ")
                    .Append($"\"x\": {F(p.x)}, \"y\": {F(p.z)}, \"z\": {F(p.y)}, \"yaw\": {F(e.y)}}}");
            }
            GameObject truck = GameObject.Find("Truck");
            Vector3 tp = truck != null ? truck.transform.position : Vector3.zero;
            json.Append("\n  ],\n").Append($"  \"truck\": {{\"x\": {F(tp.x)}, \"y\": {F(tp.z)}, \"z\": {F(tp.y)}, \"yaw\": {F(truck != null ? truck.transform.eulerAngles.y : 0)}}}\n}}\n");
            File.WriteAllText(Path.Combine(exportDirectory, "placements.json"), json.ToString());
            Log($"EXPORT meshes={meshes} triangles={triangles} materials={materials.Count} cargo={n} skipped={skipped.Count}: {string.Join(", ", skipped.GetRange(0, Mathf.Min(12, skipped.Count)))}");
            Expect(meshes > 100 && n == 43, "EXPORT wrote the level meshes and every cargo placement");
            Log($"DONE errors={errorCount} timedOut=False");
            Application.Quit(errorCount > 0 ? 1 : 0);
        }

        private static string Clean(string name)
        {
            var b = new StringBuilder(name.Length);
            foreach (char ch in name) b.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
            return b.ToString();
        }
    }
}
