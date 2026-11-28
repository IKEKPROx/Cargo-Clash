using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CargoClash
{
    /// <summary>
    /// Maya landmarks, the extra ruins, the jungle backdrop and the painted ground (road, tyre tracks).
    /// The geometry and paths come from Tools/blender/make_ruin_maya.py. Everything outside the play area is
    /// only drawn; the six structures inside it get blockers before the navigation is baked.
    /// </summary>
    public sealed partial class RuinCourtyard
    {
        [Serializable] private sealed class MayaTrack { public Vector3[] points; }
        [Serializable] private sealed class MayaRamp { public string name; public float width; public Vector3 foot, top; }
        [Serializable] private sealed class MayaSolid { public string name; public Vector3 centre, size; public float yaw; }
        [Serializable] private sealed class MayaData
        {
            public Block[] blockers;
            public MayaRamp[] ramps;
            public MayaSolid[] solids;
            public WallSpec[] walls;
            public MayaClearing[] clearings;
            public Vector2[] road;
            public float roadSouth, roadNorth;
            public Vector2 roadReach;
            public MayaTrack[] tracks;
        }

        // Strips of RuinDecals.png, as (v min, v max).
        private static readonly Vector2 TyreStrip = new Vector2(.01f, .24f);
        private static readonly Vector2 RutStrip = new Vector2(.26f, .49f);
        internal static readonly Vector2 PaintStrip = new Vector2(.51f, .74f);
        private static readonly Vector2 ScuffStrip = new Vector2(.76f, .99f);
        private const float RoadTextureMetres = 32f, RoadTextureSouth = -17f, RoadTextureWidth = 12f;

        private MayaData maya;
        private Material roadPaint, groundPaint;
        private VolumeProfile atmosphere;
        private readonly List<Mesh> paintMeshes = new List<Mesh>();

        internal int MayaBlockerCount => maya?.blockers?.Length ?? 0;
        /// <summary>Foot and summit of each climbable stair, for the runtime checks.</summary>
        internal IEnumerable<(Vector3 foot, Vector3 top)> MayaRamps
        {
            get { foreach (MayaRamp ramp in maya.ramps) yield return (ramp.foot, ramp.top); }
        }
        internal int MayaTrackCount { get; private set; }
        internal bool DepthOfFieldReady { get; private set; }

        /// <summary>Visible models and the blockers of the inside structures. Runs before the navigation bake.</summary>
        private Transform BuildMaya(Transform orientation)
        {
            GameObject model = SpawnModel("RuinCourtyard_Maya");
            model.transform.SetParent(orientation, false);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || !material.name.StartsWith("MayaTint", StringComparison.Ordinal)) continue;
                    // Terrain, foliage and bark carry their colour per vertex.
                    material.SetTexture("_BaseMap", Texture2D.whiteTexture);
                    material.SetColor("_BaseColor", Color.white);
                    material.SetFloat("_UseVertexColor", 1f);
                    bool leaf = material.name.Contains("Leaf");
                    material.SetFloat("_LeafSoftness", leaf ? 1f : 0f);
                    material.SetFloat("_DetailStrength", leaf ? 0f : .01f);
                    if (material.name.Contains("Metal"))
                    {
                        // Galvanised steel of the road guardrails.
                        material.SetFloat("_Smoothness", .45f);
                        material.SetFloat("_Metallic", .3f);
                    }
                }
                // The jungle canopy and far hills are too large and too distant to be worth a shadow.
                if (renderer.name.StartsWith("Maya_JungleSurface", StringComparison.Ordinal))
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            foreach (Block block in maya.blockers)
                AddBox(block.name + " Blocker", RectBounds(block.rect, block.minY, block.maxY - block.minY));
            // Each stair is walked as a smooth ramp; the tiers above are stacked boxes.
            const float thickness = .4f;
            foreach (MayaRamp ramp in maya.ramps)
            {
                Vector3 along = ramp.top - ramp.foot;
                Quaternion pitch = Quaternion.LookRotation(along, Vector3.up);
                BoxCollider box = AddBox(ramp.name + " Blocker", new Bounds(Vector3.zero, Vector3.one));
                box.transform.SetPositionAndRotation((ramp.foot + ramp.top) * .5f - pitch * Vector3.up * (thickness * .5f), pitch);
                box.size = new Vector3(ramp.width, thickness, along.magnitude + .2f);
            }
            // Barricades, cones and the loose axle that stand inside the widened play area.
            foreach (MayaSolid prop in maya.solids)
            {
                BoxCollider box = AddBox(prop.name + " Blocker", new Bounds(Vector3.zero, Vector3.one));
                box.transform.SetPositionAndRotation(prop.centre, Quaternion.Euler(0f, prop.yaw, 0f));
                box.size = prop.size;
            }
            return model.transform;
        }

        /// <summary>Road surface, tyre tracks and the far-distance blur. Runs once the colliders exist.</summary>
        private void BuildMayaGround()
        {
            roadPaint = new Material(Load<Shader>("RuinDecal")) { name = "Ruin road surface", renderQueue = 2600 };
            roadPaint.SetTexture("_BaseMap", Load<Texture2D>("RuinRoad"));
            roadPaint.SetFloat("_Smoothness", .22f);
            Material groundPaint = GroundPaint;

            var paint = new GameObject("Ruin painted ground").transform;
            paint.SetParent(field, false);
            AddPaint(paint, "Road surface", RoadMesh(), roadPaint);
            MayaTrackCount = 0;
            for (int i = 0; i < maya.tracks.Length; i++)
            {
                Vector3[] wheel = maya.tracks[i].points;
                var onRoad = new List<Vector3>();
                var onGrass = new List<Vector3>();
                foreach (Vector3 p in wheel)
                {
                    bool road = p.z > maya.roadSouth + .15f && p.z < maya.roadNorth - .15f;
                    // Overlap the two pieces by a point so the rubber runs into the torn grass.
                    if (road || p.z < maya.roadNorth + .4f) onRoad.Add(new Vector3(p.x, RoadHeight(p.x) + .085f, p.z));
                    if (!road || p.z > maya.roadNorth - .6f) onGrass.Add(new Vector3(p.x, p.y + .07f, p.z));
                }
                if (onRoad.Count > 1)
                    AddPaint(paint, "Tyre scrub", Ribbon(onRoad, .3f, TyreStrip, 3.5f, false, new Color(1, 1, 1, .9f), 2.5f), groundPaint);
                if (onGrass.Count > 1)
                    AddPaint(paint, "Rut through grass", Ribbon(onGrass, .5f, RutStrip, 2.6f, false, Color.white, .6f), groundPaint);
                MayaTrackCount++;
                if (i % 2 == 1)
                {
                    // A broad smear under each pair of dual tyres where the truck braked.
                    var smear = new List<Vector3>();
                    Vector3[] other = maya.tracks[i - 1].points;
                    for (int k = 0; k < wheel.Length && k < other.Length; k++)
                    {
                        Vector3 mid = (wheel[k] + other[k]) * .5f;
                        if (mid.z > maya.roadSouth + .3f && mid.z < maya.roadNorth - .3f)
                            smear.Add(new Vector3(mid.x, RoadHeight(mid.x) + .08f, mid.z));
                    }
                    if (smear.Count > 1)
                        AddPaint(paint, "Braking smear", Ribbon(smear, 1.05f, ScuffStrip, 6f, false, Color.white, 4f), groundPaint);
                }
            }
            BuildDepthOfField();
        }

        /// <summary>Tyre tracks and safety paint share one material; the depot asks for it before the road exists.</summary>
        private Material GroundPaint
        {
            get
            {
                if (groundPaint != null) return groundPaint;
                groundPaint = new Material(Load<Shader>("RuinDecal")) { name = "Ruin ground paint", renderQueue = 2610 };
                groundPaint.SetTexture("_BaseMap", Load<Texture2D>("RuinDecals"));
                return groundPaint;
            }
        }

        private void AddPaint(Transform parent, string label, Mesh mesh, Material material)
        {
            var node = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            node.transform.SetParent(parent, false);
            node.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = node.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            paintMeshes.Add(mesh);
        }

        private float RoadHeight(float x)
        {
            Vector2[] road = maya.road;
            if (x <= road[0].x) return road[0].y;
            if (x >= road[road.Length - 1].x) return road[road.Length - 1].y;
            int i = Mathf.Clamp(Mathf.FloorToInt((x - road[0].x) / (road[1].x - road[0].x)), 0, road.Length - 2);
            return Mathf.Lerp(road[i].y, road[i + 1].y, Mathf.InverseLerp(road[i].x, road[i + 1].x, x));
        }

        private Mesh RoadMesh()
        {
            // One metre per column; the painted texture already carries the shoulders and their soft edge.
            int columns = Mathf.CeilToInt(maya.roadReach.y - maya.roadReach.x);
            var vertices = new Vector3[(columns + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var colours = new Color[vertices.Length];
            var triangles = new int[columns * 6];
            for (int c = 0; c <= columns; c++)
            {
                float x = maya.roadReach.x + c;
                float y = RoadHeight(x) + .06f;
                for (int side = 0; side < 2; side++)
                {
                    float z = RoadTextureSouth + side * RoadTextureWidth;
                    int v = c * 2 + side;
                    vertices[v] = new Vector3(x, y, z);
                    uv[v] = new Vector2(x / RoadTextureMetres, side);
                    normals[v] = Vector3.up;
                    colours[v] = Color.white;
                }
                if (c == columns) continue;
                int a = c * 2, t = c * 6;
                // Clockwise seen from above: north-west, north-east, south-west, then the other half.
                triangles[t] = a + 1; triangles[t + 1] = a + 3; triangles[t + 2] = a;
                triangles[t + 3] = a; triangles[t + 4] = a + 3; triangles[t + 5] = a + 2;
            }
            var mesh = new Mesh { name = "Ruin road surface" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.normals = normals;
            mesh.colors = colours;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A flat strip along points (in the space of whatever it is parented to). V spans one strip of the
        /// decal texture; U repeats every metresPerTile. Open strips fade in and out over fade metres.
        /// </summary>
        internal static Mesh Ribbon(IList<Vector3> points, float width, Vector2 strip, float metresPerTile, bool loop,
            Color tint, float fade = 0f)
        {
            int n = points.Count, count = loop ? n + 1 : n;
            var vertices = new Vector3[count * 2];
            var uv = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var colours = new Color[vertices.Length];
            var triangles = new int[(count - 1) * 6];
            float total = 0f;
            for (int i = 1; i < count; i++) total += Flat(points[i % n] - points[i - 1]).magnitude;
            float travelled = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = points[i % n];
                if (i > 0) travelled += Flat(p - points[i - 1]).magnitude;
                Vector3 previous = points[loop ? (i - 1 + n) % n : Mathf.Max(i - 1, 0)];
                Vector3 next = points[loop ? (i + 1) % n : Mathf.Min(i + 1, n - 1)];
                Vector3 side = Vector3.Cross(Vector3.up, Flat(next - previous).normalized) * (width * .5f);
                vertices[i * 2] = p - side;
                vertices[i * 2 + 1] = p + side;
                float u = travelled / metresPerTile;
                uv[i * 2] = new Vector2(u, strip.y);
                uv[i * 2 + 1] = new Vector2(u, strip.x);
                normals[i * 2] = normals[i * 2 + 1] = Vector3.up;
                Color c = tint;
                if (!loop && fade > 0f) c.a *= Mathf.Clamp01(Mathf.Min(travelled, total - travelled) / fade);
                colours[i * 2] = colours[i * 2 + 1] = c;
                if (i == count - 1) continue;
                int a = i * 2, t = i * 6;
                triangles[t] = a; triangles[t + 1] = a + 2; triangles[t + 2] = a + 1;
                triangles[t + 3] = a + 1; triangles[t + 4] = a + 2; triangles[t + 5] = a + 3;
            }
            var mesh = new Mesh { name = "Ruin ground paint" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.normals = normals;
            mesh.colors = colours;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>Softens only the far distance: the jungle, the landmarks and the hills, never the play area.</summary>
        private void BuildDepthOfField()
        {
            FollowCamera follow = FindAnyObjectByType<FollowCamera>(FindObjectsInactive.Include);
            if (follow == null || follow.view == null) return;
            UniversalAdditionalCameraData data = follow.view.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            atmosphere = ScriptableObject.CreateInstance<VolumeProfile>();
            atmosphere.name = "Ruin far blur";
            DepthOfField blur = atmosphere.Add<DepthOfField>(true);
            blur.mode.Override(DepthOfFieldMode.Gaussian);
            blur.gaussianStart.Override(config.ruinFarBlurStart);
            blur.gaussianEnd.Override(config.ruinFarBlurEnd);
            blur.gaussianMaxRadius.Override(config.ruinFarBlurRadius);
            var volume = new GameObject("Ruin far blur").AddComponent<Volume>();
            volume.transform.SetParent(transform, false);
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = atmosphere;
            DepthOfFieldReady = true;
        }

        private void DisposeMaya()
        {
            foreach (Mesh mesh in paintMeshes) Destroy(mesh);
            if (roadPaint != null) Destroy(roadPaint);
            if (groundPaint != null) Destroy(groundPaint);
            if (atmosphere != null) Destroy(atmosphere);
        }
    }
}
