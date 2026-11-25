using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CargoClash
{
    public sealed partial class RuinCourtyard
    {
        [SerializeField] private Texture2D cordonTape;
        private Mesh cordonMesh;

        private void BuildCordon()
        {
            var root = new GameObject("Ruin perimeter cordon").transform;
            root.SetParent(field, false);
            Color navy = new Color32(48, 57, 80, 255);
            Material posts = SpawnMarkerMaterial("Cordon navy", navy);
            Material yellow = SpawnMarkerMaterial("Cordon yellow", new Color32(217, 169, 40, 255));
            Material cream = SpawnMarkerMaterial("Cordon sign cream", new Color32(223, 216, 183, 255));
            Material tape = SpawnMarkerMaterial("Striped warning tape", Color.white);
            tape.SetTexture("_BaseMap", cordonTape);
            tape.SetFloat("_Cull", (float)CullMode.Off);

            const float inset = .38f;
            const float roadside = -10.5f;
            Vector3[] route =
            {
                new Vector3(CargoExitWest, 0, roadside), new Vector3(CargoExitWest, 0, inset),
                new Vector3(inset, 0, inset), new Vector3(inset, 0, data.size.y - inset),
                new Vector3(data.size.x - inset, 0, data.size.y - inset), new Vector3(data.size.x - inset, 0, inset),
                new Vector3(CargoExitEast, 0, inset), new Vector3(CargoExitEast, 0, roadside)
            };
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            for (int side = 0; side < route.Length - 1; side++)
            {
                Vector3 start = route[side], end = route[side + 1];
                int spans = Mathf.CeilToInt(Vector3.Distance(start, end) / 7.5f);
                Vector3 from = CordonFoot(start);
                for (int span = 0; span < spans; span++)
                {
                    Vector3 to = CordonFoot(Vector3.Lerp(start, end, (span + 1f) / spans));
                    BuildCordonPost(root, from, posts, yellow);
                    AddTapeSpan(vertices, uv, triangles, from, to, 1.22f, .17f, .14f);
                    AddTapeSpan(vertices, uv, triangles, from, to, .73f, .12f, .21f);
                    from = to;
                }
            }
            BuildCordonPost(root, CordonFoot(route[route.Length - 1]), posts, yellow);

            var ribbons = new GameObject("Warning ribbons with cargo entrance", typeof(MeshFilter), typeof(MeshRenderer));
            ribbons.transform.SetParent(root, false);
            cordonMesh = new Mesh { name = "Ruin warning ribbons" };
            cordonMesh.SetVertices(vertices);
            cordonMesh.SetUVs(0, uv);
            cordonMesh.SetTriangles(triangles, 0);
            cordonMesh.RecalculateNormals();
            ribbons.GetComponent<MeshFilter>().sharedMesh = cordonMesh;
            MeshRenderer renderer = ribbons.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = tape;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            BuildCordonSign(root, new Vector3(CargoExitWest - 2.1f, 0, 1.05f), posts, cream, navy);
            BuildCordonSign(root, new Vector3(data.size.x * .5f - 8f, 0, data.size.y - 1.05f), posts, cream, navy);

            foreach (float z in new[] { -7.1f, -3.2f })
            {
                Vector3 mark = CordonFoot(new Vector3(data.truckZone.x, 0, z)) + Vector3.up * .035f;
                SpawnPaintStroke(root, "Cargo access arrow", mark + new Vector3(-.72f, 0, -.36f), mark + Vector3.forward * .46f, .14f, yellow);
                SpawnPaintStroke(root, "Cargo access arrow", mark + new Vector3(.72f, 0, -.36f), mark + Vector3.forward * .46f, .14f, yellow);
            }

            foreach (DoorSpec door in data.doors)
            {
                foreach (float direction in new[] { -1f, 1f })
                {
                    Vector3 mark = CordonFoot(door.center + Vector3.forward * (2.1f * direction)) + Vector3.up * .025f;
                    Vector3 tip = mark - Vector3.forward * (.26f * direction);
                    SpawnPaintStroke(field, "Walk-up gate marking", mark + new Vector3(-.26f, 0, .13f * direction), tip, .09f, yellow);
                    SpawnPaintStroke(field, "Walk-up gate marking", mark + new Vector3(.26f, 0, .13f * direction), tip, .09f, yellow);
                }
            }
        }

        private static void BuildCordonPost(Transform root, Vector3 foot, Material post, Material yellow)
        {
            SpawnDecoration(root, "Weighted cordon foot", PrimitiveType.Cylinder,
                foot + Vector3.up * .055f, new Vector3(.46f, .055f, .46f), post);
            SpawnDecoration(root, "Cordon post", PrimitiveType.Cylinder,
                foot + Vector3.up * .65f, new Vector3(.12f, .65f, .12f), post);
            SpawnDecoration(root, "Rounded safety cap", PrimitiveType.Sphere,
                foot + Vector3.up * 1.32f, Vector3.one * .19f, yellow);
        }

        private static Vector3 CordonFoot(Vector3 point)
        {
            if (Physics.Raycast(point + Vector3.up * .55f, Vector3.down, out RaycastHit floor,
                    1.2f, GameLayers.LevelMask, QueryTriggerInteraction.Ignore))
                point.y = floor.point.y;
            return point;
        }

        private static void AddTapeSpan(List<Vector3> vertices, List<Vector2> uv, List<int> triangles,
            Vector3 start, Vector3 end, float height, float width, float sag)
        {
            const int segments = 8;
            float tiles = Mathf.Max(1, Mathf.Round(Vector3.Distance(start, end) / .62f));
            int first = vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 point = Vector3.Lerp(start, end, t) + Vector3.up * (height - 4f * sag * t * (1f - t));
                vertices.Add(point - Vector3.up * (width * .5f));
                vertices.Add(point + Vector3.up * (width * .5f));
                uv.Add(new Vector2(tiles * t, .01f));
                uv.Add(new Vector2(tiles * t, .99f));
                if (i == segments) continue;
                int v = first + i * 2;
                triangles.Add(v);
                triangles.Add(v + 2);
                triangles.Add(v + 1);
                triangles.Add(v + 1);
                triangles.Add(v + 2);
                triangles.Add(v + 3);
            }
        }

        private static void BuildCordonSign(Transform root, Vector3 point, Material posts, Material cream, Color ink)
        {
            var sign = new GameObject("Site closed sign").transform;
            sign.SetParent(root, false);
            sign.localPosition = CordonFoot(point);
            sign.localScale = Vector3.one * .55f;
            foreach (float x in new[] { -1.9f, 1.9f })
                SpawnDecoration(sign, "Sign support", PrimitiveType.Cylinder,
                    new Vector3(x, 1.28f, 0), new Vector3(.16f, 1.28f, .16f), posts, true);
            SpawnDecoration(sign, "Dark sign edge", PrimitiveType.Cube,
                new Vector3(0, 2.55f, 0), new Vector3(5.25f, 1.7f, .24f), posts, true);
            SpawnDecoration(sign, "Cream signboard", PrimitiveType.Cube,
                new Vector3(0, 2.55f, 0), new Vector3(5.06f, 1.52f, .3f), cream);
            BuildSignFace(sign, true, GameText.RuinCordonEnglish, GameText.RuinKeepOutEnglish, ink);
            BuildSignFace(sign, false, GameText.RuinCordonEnglish, GameText.RuinKeepOutEnglish, ink);
        }
    }
}
