using System;
using System.Collections.Generic;
using UnityEngine;

namespace CargoClash
{
    public sealed partial class RuinCourtyard
    {
        /// <summary>Floor-to-ground walking routes, one per pit: up a side flight, across the landing, up the central
        /// flight to the ground outside the south rim (make_ruin_pit_stairs.py).</summary>
        internal Vector3[][] PitStairPaths { get; private set; }
        internal Bounds[] PitStairBounds { get; private set; }

        /// <summary>Imported stonework with explicit collider/path locators, built before navigation.</summary>
        private void BuildPitStairs()
        {
            GameObject model = SpawnModel("RuinCourtyard_PitStairs");
            Transform source = model.transform;
            Vector3 origin = Find(source, "PitStairs_Coordinate_Origin").position;
            Vector3 east = (Find(source, "PitStairs_Coordinate_East").position-origin)/5f;
            Vector3 north = (Find(source, "PitStairs_Coordinate_North").position-origin)/5f;
            Vector3 up = (Find(source, "PitStairs_Coordinate_Up").position-origin)/5f;
            if (Mathf.Abs(east.magnitude-1) > .02f || Mathf.Abs(north.magnitude-1) > .02f || Mathf.Abs(up.magnitude-1) > .02f)
                throw new InvalidOperationException("Pit stairs FBX must use metre units.");
            float handedness = Mathf.Sign(Vector3.Dot(Vector3.Cross(up, north), east));
            Quaternion rotation = Quaternion.Inverse(Quaternion.LookRotation(north, up));
            Vector3 scale = new Vector3(handedness, 1, 1);
            Transform corrected = new GameObject("Pit stair axis correction").transform;
            corrected.SetParent(field, false);
            corrected.localScale = scale;
            corrected.localPosition = -Vector3.Scale(rotation*origin, scale);
            Transform oriented = new GameObject("Pit stair orientation").transform;
            oriented.SetParent(corrected, false);
            oriented.localRotation = rotation;
            model.transform.SetParent(oriented, false);

            // These materials are unique to this FBX; they do not use the combined V09 atlas.
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                {
                    material.SetTexture("_BaseMap", Texture2D.whiteTexture);
                    Color color = material.name.StartsWith("PitStair_Cap", StringComparison.Ordinal)
                        ? new Color(223f/255f, 216f/255f, 183f/255f)
                        : material.name.StartsWith("PitStair_Safety", StringComparison.Ordinal)
                            ? new Color(217f/255f, 169f/255f, 40f/255f)
                            : material.name.StartsWith("PitStair_Shade", StringComparison.Ordinal)
                                ? new Color(156f/255f, 149f/255f, 130f/255f)
                                : new Color(182f/255f, 176f/255f, 154f/255f);
                    material.SetColor("_BaseColor", color);
                    material.SetFloat("_Smoothness", .18f);
                }

            if (Vector3.Distance(Find(source, "PitStairs_Coordinate_East").position, Vector3.right*5) > .02f ||
                Vector3.Distance(Find(source, "PitStairs_Coordinate_North").position, Vector3.forward*5) > .02f ||
                Vector3.Distance(Find(source, "PitStairs_Coordinate_Up").position, Vector3.up*5) > .02f)
                throw new InvalidOperationException("Pit stair frame did not align with the level.");

            PitStairPaths = new Vector3[2][];
            var stairBounds = new List<Bounds>();
            for (int pit = 0; pit < 2; pit++)
            {
                string prefix = "PitStairs_"+(pit == 0 ? "West" : "East");
                var path = new List<Vector3>();
                for (Transform point; (point = TryFind(source, $"{prefix}_Path_{path.Count:00}")) != null;) path.Add(point.position);
                if (path.Count < 2) throw new InvalidOperationException("Pit stair path missing: " + prefix);
                PitStairPaths[pit] = path.ToArray();
                for (int i = 0; ; i++)
                {
                    Transform low = TryFind(source, $"{prefix}_Box_{i:00}_Min");
                    if (low == null) break;
                    Vector3 min = low.position;
                    Vector3 max = Find(source, $"{prefix}_Box_{i:00}_Max").position;
                    Bounds bounds = new Bounds((min+max)*.5f, max-min);
                    if (bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0)
                        throw new InvalidOperationException("Invalid pit stair collider " + prefix + i);
                    stairBounds.Add(bounds);
                    AddBox($"{prefix} stair {i:00}", bounds);
                }
            }
            PitStairBounds = stairBounds.ToArray();
        }
    }
}
