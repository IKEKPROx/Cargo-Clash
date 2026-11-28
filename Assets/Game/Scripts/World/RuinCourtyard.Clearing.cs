using System;
using System.Collections.Generic;
using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// Strips shipped trees, rocks and plants from the V09 environment where the Maya pass put a structure, and
    /// the over-neat plants along the pit rims. Whole islands go (a tree, a rock, a tuft), never part of one, and
    /// never the ground or a building: the clearings are checked against the layout's building blockers.
    /// </summary>
    public sealed partial class RuinCourtyard
    {
        [Serializable] private sealed class MayaClearing { public string name; public Vector4 rect; public bool foliageOnly; }

        private readonly List<Mesh> clearedMeshes = new List<Mesh>();
        private readonly List<Bounds> clearedModels = new List<Bounds>();
        internal int ClearedIslands { get; private set; }
        /// <summary>Loose rocks and grass clumps taken off the ground everywhere, for the runtime checks.</summary>
        internal int ClearedClutter { get; private set; }
        /// <summary>Shipped blockers left out because the model they stood for was cleared away.</summary>
        internal readonly List<string> DroppedBlockers = new List<string>();
        /// <summary>The pit warning signs' pieces, kept by GroundClutter; they get a collider each (BuildPitSigns).</summary>
        private readonly List<Bounds> pitSigns = new List<Bounds>();
        internal int PitSignSolids { get; private set; }

        /// <summary>True when a cleared model stood inside this blocker: keeping it would be an invisible wall.</summary>
        private bool BlocksClearedModel(Vector4 rect, float top)
        {
            foreach (Bounds model in clearedModels)
                if (model.max.y > .3f && model.min.y < top && Inside(rect, model.center.x, model.center.z)) return true;
            return false;
        }

        private void LoadMaya() => maya = JsonUtility.FromJson<MayaData>(Load<TextAsset>("RuinMaya").text);

        /// <summary>True for a shipped tree's blocker that now stands inside a structure.</summary>
        private bool InStructureClearing(float x, float z)
        {
            foreach (MayaClearing clearing in maya.clearings)
                if (!clearing.foliageOnly && Inside(clearing.rect, x, z)) return true;
            return false;
        }

        private static bool Inside(Vector4 rect, float x, float z) => x >= rect.x && x <= rect.z && z >= rect.y && z <= rect.w;

        private static bool LeafUv(Vector2 uv) => uv.x > .25f && uv.x < .75f && uv.y < .25f;

        private void ClearUnderStructures(Transform environment)
        {
            ClearedIslands = 0;
            ClearedClutter = 0;
            clearedModels.Clear();
            DroppedBlockers.Clear();
            pitSigns.Clear();
            foreach (MeshFilter filter in environment.GetComponentsInChildren<MeshFilter>(true))
            {
                // Each chunk ships a detailed Near mesh and a coarse Far one, and nothing switches between them:
                // both draw all the time. The Far copy welds its simplified rocks and grass to the ground, so
                // they cannot be cleared from it; it is hidden wherever the Near copy is there.
                if (filter.name.EndsWith("_Far", StringComparison.Ordinal) && filter.transform.parent != null &&
                    filter.transform.parent.Find(filter.name.Substring(0, filter.name.Length - 4) + "_Near") != null)
                {
                    Renderer far = filter.GetComponent<Renderer>();
                    if (far != null) far.enabled = false;
                    continue;
                }
                // The ground mesh carries some boulders of its own, and its collider is built from it afterwards.
                if (!filter.name.StartsWith("RuinChunk_", StringComparison.Ordinal) &&
                    !filter.name.StartsWith("Ground_Surface", StringComparison.Ordinal)) continue;
                Mesh source = filter.sharedMesh;
                Renderer renderer = filter.GetComponent<Renderer>();
                // Every chunk: the loose rocks and grass clumps go everywhere, not only under the structures.
                if (source == null || !source.isReadable || renderer == null) continue;
                Mesh cleared = ClearIslands(source, filter.transform.localToWorldMatrix);
                if (cleared == null) continue;
                filter.sharedMesh = cleared;
                clearedMeshes.Add(cleared);
            }
        }

        private bool Touches(Bounds bounds)
        {
            foreach (MayaClearing clearing in maya.clearings)
                if (bounds.min.x <= clearing.rect.z && bounds.max.x >= clearing.rect.x &&
                    bounds.min.z <= clearing.rect.w && bounds.max.z >= clearing.rect.y) return true;
            return false;
        }

        /// <summary>A copy of the mesh without the islands that stand in a clearing, or null if none do.</summary>
        private Mesh ClearIslands(Mesh source, Matrix4x4 toWorld)
        {
            Vector3[] vertices = source.vertices;
            Vector2[] uv = source.uv;
            int count = vertices.Length;
            var world = new Vector3[count];
            for (int i = 0; i < count; i++) world[i] = toWorld.MultiplyPoint3x4(vertices[i]);

            // Islands by shared position, so seams split for UVs or normals still join.
            var parent = new int[count];
            var byPosition = new Dictionary<Vector3Int, int>(count);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = vertices[i] * 1000f;
                var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                if (byPosition.TryGetValue(key, out int first)) parent[i] = first;
                else
                {
                    byPosition.Add(key, i);
                    parent[i] = i;
                }
            }
            int Root(int i)
            {
                while (parent[i] != i) i = parent[i] = parent[parent[i]];
                return i;
            }
            int[][] submeshes = new int[source.subMeshCount][];
            for (int s = 0; s < submeshes.Length; s++)
            {
                int[] triangles = submeshes[s] = source.GetTriangles(s);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    int a = Root(triangles[t]), b = Root(triangles[t + 1]), c = Root(triangles[t + 2]);
                    parent[b] = a;
                    parent[Root(c)] = a;
                }
            }
            var min = new Dictionary<int, Vector3>();
            var max = new Dictionary<int, Vector3>();
            var leafy = new Dictionary<int, bool>();
            foreach (int[] triangles in submeshes)
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    int root = Root(triangles[t]);
                    bool leaf = uv.Length == count && LeafUv((uv[triangles[t]] + uv[triangles[t + 1]] + uv[triangles[t + 2]]) / 3f);
                    leafy[root] = leafy.TryGetValue(root, out bool all) ? all && leaf : leaf;
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 p = world[triangles[t + k]];
                        min[root] = min.TryGetValue(root, out Vector3 lo) ? Vector3.Min(lo, p) : p;
                        max[root] = max.TryGetValue(root, out Vector3 hi) ? Vector3.Max(hi, p) : p;
                    }
                }
            var remove = new HashSet<int>();
            foreach (KeyValuePair<int, Vector3> island in min)
            {
                Vector3 lo = island.Value, hi = max[island.Key], centre = (lo + hi) * .5f;
                // Ground, terrain and anything large stay; only props standing on the ground go.
                if (hi.x - lo.x > 14f || hi.z - lo.z > 14f) continue;
                // Under a structure: anything standing on the ground. Elsewhere: loose rocks, flat stones and
                // grass clumps, however flat (GroundClutter).
                foreach (MayaClearing clearing in maya.clearings)
                {
                    if (hi.y < .08f) break;
                    if (!Inside(clearing.rect, centre.x, centre.z)) continue;
                    if (clearing.foliageOnly && !leafy[island.Key]) continue;
                    remove.Add(island.Key);
                    break;
                }
                if (remove.Contains(island.Key) || !GroundClutter(lo, hi, leafy[island.Key])) continue;
                remove.Add(island.Key);
                ClearedClutter++;
            }
            if (remove.Count == 0) return null;
            ClearedIslands += remove.Count;
            foreach (int island in remove)
            {
                var model = new Bounds();
                model.SetMinMax(min[island], max[island]);
                clearedModels.Add(model);
            }
            Mesh copy = Instantiate(source);
            copy.name = source.name + " (cleared)";
            for (int s = 0; s < submeshes.Length; s++)
            {
                int[] triangles = submeshes[s];
                var keep = new List<int>(triangles.Length);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    if (remove.Contains(Root(triangles[t]))) continue;
                    keep.Add(triangles[t]);
                    keep.Add(triangles[t + 1]);
                    keep.Add(triangles[t + 2]);
                }
                copy.SetTriangles(keep, s, false);
            }
            return copy;
        }

        /// <summary>
        /// A loose rock or a grass clump lying on the ground: small, low, and not part of anything a shipped blocker
        /// stands for (the ruins' own low steps and stones stay). Tree crowns start well off the ground.
        /// </summary>
        private bool GroundClutter(Vector3 lo, Vector3 hi, bool leaf)
        {
            // Up to a metre off the ground: a rock's or a bush's loose top plates float there once the rest is gone.
            if (lo.y > 1f || hi.y < .005f) return false;
            float width = Mathf.Max(hi.x - lo.x, hi.z - lo.z);
            Vector3 middle = (lo + hi) * .5f;
            // The warning signs round the pits, posts and plates: they stand among the rocks there, taller than
            // any rock. They stay, and each piece gets a collider (BuildPitSigns).
            if (!leaf && hi.y > .45f && hi.y < 2.5f && width < 1.5f)
                foreach (Vector4 pit in data.pits)
                    if (middle.x > pit.x - 3f && middle.x < pit.z + 3f && middle.z > pit.y - 3f && middle.z < pit.w + 3f)
                    {
                        var sign = new Bounds();
                        sign.SetMinMax(lo, hi);
                        pitSigns.Add(sign);
                        return false;
                    }
            // Grass clumps; rocks and boulders, squat ones only (a tree trunk is tall for its width).
            bool clutter = leaf ? width < 3f && hi.y < 1.6f : width < 3.5f && hi.y < 1.6f && hi.y - lo.y < width * 1.2f;
            if (!clutter) return false;
            Vector3 centre = (lo + hi) * .5f;
            foreach (Block block in data.blockers)
            {
                if (!block.round && Inside(block.rect, centre.x, centre.z)) return false;
                // A short trunk stays with its crown: never anything standing at a tree's blocker.
                if (block.round && new Vector2(centre.x - block.circle.x, centre.z - block.circle.y).sqrMagnitude <
                    (block.circle.z + .6f) * (block.circle.z + .6f)) return false;
            }
            // The roadside barricades and cones have collision of their own (RuinMaya.json solids): they stay.
            foreach (MayaSolid solid in maya.solids)
            {
                float reach = Mathf.Max(solid.size.x, solid.size.z) * .5f + .4f;
                if (new Vector2(centre.x - solid.centre.x, centre.z - solid.centre.z).sqrMagnitude < reach * reach) return false;
            }
            return true;
        }

        /// <summary>A solid box round each piece of the pit warning signs, so nobody walks through them.</summary>
        private void BuildPitSigns()
        {
            PitSignSolids = 0;
            foreach (Bounds piece in pitSigns)
            {
                Bounds solid = piece;
                solid.size = Vector3.Max(solid.size, new Vector3(.18f, .1f, .18f));
                AddBox("Pit warning sign", solid);
                PitSignSolids++;
            }
        }

        private void DisposeClearing()
        {
            foreach (Mesh mesh in clearedMeshes) Destroy(mesh);
            clearedMeshes.Clear();
        }
    }
}
