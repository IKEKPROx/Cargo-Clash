using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace CargoClash
{
    public sealed partial class RuinCourtyard
    {
        [Serializable] private sealed class Palette { public Tint[] materials; }
        [Serializable] private sealed class Tint { public string name; public Color color; }

        private readonly Dictionary<Material, Material> renderMaterials = new Dictionary<Material, Material>();
        private readonly Dictionary<string, Color> palette = new Dictionary<string, Color>(StringComparer.Ordinal);
        private Transform field;
        private NavMeshData runtimeNavMesh;
        private NavMeshSurface navigation;
        private const float RoadApronDepth = 18f;
        // The road's north guardrail closes the cargo apron; see make_ruin_maya.py (GUARD_NORTH).
        internal const float GuardrailZ = -6.2f;
        private const float CargoExitHalfWidth = 6f;
        private float CargoExitWest => data.truckZone.x - CargoExitHalfWidth;
        private float CargoExitEast => data.truckZone.x + CargoExitHalfWidth;

        private static T Load<T>(string name) where T : UnityEngine.Object
        {
            T asset = Resources.Load<T>(ResourceRoot + name);
            if (asset == null) throw new InvalidOperationException("Ruin Courtyard is missing " + name);
            return asset;
        }

        private GameObject SpawnModel(string name)
        {
            GameObject instance = Instantiate(Load<GameObject>(name), field);
            instance.name = name;
            Material surface = Load<Material>("RuinSurface");
            Texture2D groundTexture = name == "RuinCourtyard_Environment" ? Load<Texture2D>("RuinGround") : null;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    Material source = slots[i];
                    if (source == null) continue;
                    if (!renderMaterials.TryGetValue(source, out Material material))
                    {
                        material = new Material(surface) { name = source.name };
                        if (palette.TryGetValue(source.name, out Color color)) material.SetColor("_BaseColor", color);
                        else if (source.HasProperty("_BaseColor")) material.SetColor("_BaseColor", source.GetColor("_BaseColor"));
                        else if (source.HasProperty("_Color")) material.SetColor("_BaseColor", source.color);
                        material.SetFloat("_Smoothness", .24f);
                        if (source.name.StartsWith("RuinGround", StringComparison.Ordinal))
                        {
                            material.SetColor("_BaseColor", Color.white);
                            material.SetTexture("_BaseMap", groundTexture);
                        }
                        renderMaterials.Add(source, material);
                    }
                    slots[i] = material;
                }
                renderer.sharedMaterials = slots;
            }
            return instance;
        }

        private static Transform TryFind(Transform root, string prefix)
        {
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                if (node.name.StartsWith(prefix, StringComparison.Ordinal)) return node;
            return null;
        }

        private static Transform Find(Transform root, string prefix)
        {
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                if (node.name.StartsWith(prefix, StringComparison.Ordinal)) return node;
            throw new InvalidOperationException("Ruin Courtyard model node missing: " + prefix);
        }

        private void BuildField()
        {
            foreach (Tint tint in JsonUtility.FromJson<Palette>(Load<TextAsset>("RuinPalette").text).materials)
                palette[tint.name] = tint.color;
            field = new GameObject("Ruin Courtyard field").transform;
            field.SetParent(transform, false);
            GameObject environment = SpawnModel("RuinCourtyard_Environment");
            Vector3 origin = Find(environment.transform, "Coordinate_Origin").position;
            Vector3 east = (Find(environment.transform, "Coordinate_EastX").position - origin) / 5f;
            Vector3 north = (Find(environment.transform, "Coordinate_NorthZ").position - origin) / 5f;
            Vector3 sample = data.largeSpots[0].position;
            Vector3 up = (Find(environment.transform, "B1_CargoCenter").position - origin - east*sample.x - north*sample.z) / sample.y;
            if (Mathf.Abs(east.magnitude - 1f) > .02f || Mathf.Abs(north.magnitude - 1f) > .02f ||
                Mathf.Abs(up.magnitude - 1f) > .04f)
                throw new InvalidOperationException("Ruin Courtyard FBX locator scale is incorrect.");
            float handedness = Mathf.Sign(Vector3.Dot(Vector3.Cross(up, north), east));
            Quaternion correction = Quaternion.Inverse(Quaternion.LookRotation(north, up));
            Vector3 axisScale = new Vector3(handedness, 1f, 1f);
            Vector3 translation = -Vector3.Scale(correction * origin, axisScale);
            // The shared FBX space is mapped by the locator frame, including a mirrored X if needed.
            Transform corrected = new GameObject("FBX axis correction").transform;
            corrected.SetParent(field, false);
            corrected.localPosition = translation;
            corrected.localScale = axisScale;
            Transform rotated = new GameObject("FBX orientation").transform;
            rotated.SetParent(corrected, false);
            rotated.localRotation = correction;
            environment.transform.SetParent(rotated, false);
            LoadMaya();
            ClearUnderStructures(environment.transform);

            foreach (string vault in new[] { "B1", "B2" })
            {
                string mountName = vault + "_LampLightMount";
                var lamp = new GameObject(mountName + " Interior Light");
                lamp.transform.SetParent(field, false);
                lamp.transform.position = Find(environment.transform, mountName).position;
                Light light = lamp.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, .89f, .7f);
                light.intensity = 2f;
                light.range = 7f;
                light.shadows = LightShadows.None;
            }

            BuildPitSigns();
            MeshFilter ground = Find(environment.transform, "Ground_Surface").GetComponent<MeshFilter>();
            AddGroundMesh(ground);
            foreach (Transform node in environment.GetComponentsInChildren<Transform>(true))
            {
                if (node.name.EndsWith("pit floor", StringComparison.Ordinal) ||
                    node.name.Contains("rock stratum"))
                {
                    MeshFilter mesh = node.GetComponent<MeshFilter>();
                    if (mesh != null) AddGroundMesh(mesh);
                }
            }
            foreach (Block block in data.blockers)
            {
                float height = block.maxY - block.minY;
                // A shipped tree cleared away for a Maya structure takes its blocker with it.
                if (block.round && InStructureClearing(block.circle.x, block.circle.y)) continue;
                // The same for any other shipped piece whose model the clearing took: no invisible walls.
                if (!block.round && BlocksClearedModel(block.rect, block.maxY))
                {
                    DroppedBlockers.Add(block.name);
                    continue;
                }
                if (block.round)
                {
                    GameObject node = new GameObject(block.name + " Blocker");
                    node.transform.SetParent(field, false);
                    node.transform.position = new Vector3(block.circle.x, block.minY + height*.5f, block.circle.y);
                    node.layer = GameLayers.Level;
                    CapsuleCollider c = node.AddComponent<CapsuleCollider>();
                    c.radius = block.circle.z;
                    c.height = height;
                }
                else AddBox(block.name + " Blocker", RectBounds(block.rect, block.minY, height));
            }
            // The play area runs south to the road's north guardrail along its whole width; low ruined walls
            // close the two corners (make_ruin_maya.py corner_walls).
            float south = GuardrailZ - .5f;
            // The play area was drawn in from the courtyard's edges (data.playArea); what is left outside is scenery.
            float playWest = data.playArea.x, playEast = data.playArea.z, playNorth = data.playArea.w;
            AddBox("West play boundary", RectBounds(new Vector4(playWest - 1, south, playWest, playNorth + 1), 0, 10));
            AddBox("East play boundary", RectBounds(new Vector4(playEast, south, playEast + 1, playNorth + 1), 0, 10));
            AddBox("North play boundary", RectBounds(new Vector4(playWest, playNorth, playEast, playNorth + 1), 0, 10));
            // The guardrail, intact or crushed low where the truck went through, is the visible edge here.
            AddBox("Road guardrail", RectBounds(new Vector4(0, GuardrailZ - .3f, data.size.x, GuardrailZ + .7f), 0, 10));

            // Thin landing tops preserve the walkable west descent and the continuous upper deck.
            AddBox("Terrace upper deck", RectBounds(new Vector4(56.4f, 37.38f, 71.6f, 44.62f), 2.47f, .03f));
            AddBox("Terrace west landing", RectBounds(new Vector4(54f, 41.8f, 56.28f, 44.2f), 0f, 1.65f));
            AddBox("Terrace lower landing", RectBounds(new Vector4(52.7f, 41.8f, 54f, 44.2f), 0f, .8f));

            GameObject wallModel = SpawnModel("RuinCourtyard_Breakables");
            wallModel.transform.SetParent(rotated, false);
            ValidateAxisAlignment(environment.transform);
            Transform mayaModel = BuildMaya(rotated);
            BuildWalls(wallModel.transform, mayaModel);
            BuildVines(environment.transform, rotated);
            BuildPitStairs();
            ConfigureLayout();

            navigation = gameObject.AddComponent<NavMeshSurface>();
            navigation.navMeshData = null;
            navigation.collectObjects = CollectObjects.Volume;
            // From the pit floors (-3.6 m) up to the summit of the great pyramid (7.5 m) and its shrine.
            navigation.center = new Vector3(data.size.x * .5f, 5f, (data.size.y - RoadApronDepth) * .5f);
            navigation.size = new Vector3(data.size.x, 20f, data.size.y + RoadApronDepth);
            navigation.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            navigation.layerMask = GameLayers.LevelMask;
            Physics.SyncTransforms();
            // Bots move with the same CharacterController as players. The project's default
            // .75 m navigation climb would otherwise route them up .74 m ruin foundations.
            NavMeshBuildSettings settings = navigation.GetBuildSettings();
            CharacterController body = config.avatarPrefab.GetComponent<CharacterController>();
            settings.agentClimb = Mathf.Min(settings.agentClimb, body.stepOffset);
            settings.agentRadius = Mathf.Max(settings.agentRadius, body.radius + body.skinWidth);
            settings.agentHeight = Mathf.Max(settings.agentHeight, body.height + body.skinWidth * 2f);
            var sources = new List<NavMeshBuildSource>();
            var volume = new Bounds(navigation.center, navigation.size);
            NavMeshBuilder.CollectSources(volume, GameLayers.LevelMask, navigation.useGeometry, 0, false,
                new List<NavMeshBuildMarkup>(), false, sources);
            // Keep operable geometry out of the bake; its obstacles carve the current state.
            sources.RemoveAll(source => source.component != null &&
                (source.component.GetComponent<NavMeshObstacle>() != null || source.component.GetComponent<NavMeshAgent>() != null));
            runtimeNavMesh = NavMeshBuilder.BuildNavMeshData(settings, sources, volume, transform.position, transform.rotation);
            if (runtimeNavMesh == null) throw new InvalidOperationException("Ruin Courtyard navigation build failed.");
            navigation.navMeshData = runtimeNavMesh;
            navigation.AddData();
            BuildCordon();
            BuildMayaGround();
            ShowState();
        }

        private void ValidateAxisAlignment(Transform environment)
        {
            if ((Find(environment, "Coordinate_Origin").position).sqrMagnitude > .01f ||
                Vector3.Distance(Find(environment, "Coordinate_EastX").position, Vector3.right*5f) > .06f ||
                Vector3.Distance(Find(environment, "Coordinate_NorthZ").position, Vector3.forward*5f) > .06f)
                throw new InvalidOperationException("Ruin Courtyard FBX positions do not align with the layout.");
        }

        private void AddGroundMesh(MeshFilter mesh)
        {
            // PhysX cannot reliably cook a mirrored triangle mesh. Bake world vertices and winding.
            Mesh source = mesh.sharedMesh;
            var matrix = mesh.transform.localToWorldMatrix;
            Vector3[] vertices = source.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            int[] triangles = source.triangles;
            if (matrix.determinant < 0f)
                for (int i = 0; i < triangles.Length; i += 3)
                    (triangles[i+1], triangles[i+2]) = (triangles[i+2], triangles[i+1]);
            Mesh baked = new Mesh { name = source.name + " collision" };
            baked.vertices = vertices;
            baked.triangles = triangles;
            baked.RecalculateBounds();
            GameObject node = new GameObject(source.name + " Collider");
            node.layer = GameLayers.Level;
            node.AddComponent<MeshCollider>().sharedMesh = baked;
            node.transform.SetParent(field, true);
            collisionMeshes.Add(baked);
        }

        private readonly List<Mesh> collisionMeshes = new List<Mesh>();

        private void DisposeField()
        {
            if (navigation != null) navigation.RemoveData();
            if (runtimeNavMesh != null) Destroy(runtimeNavMesh);
            foreach (Mesh mesh in collisionMeshes) Destroy(mesh);
            foreach (Material material in renderMaterials.Values) Destroy(material);
            if (cordonMesh != null) Destroy(cordonMesh);
            DisposeDepot();
            DisposeMaya();
            DisposeClearing();
        }

        private BoxCollider AddBox(string name, Bounds bounds)
        {
            GameObject node = new GameObject(name);
            node.transform.SetParent(field, false);
            node.layer = GameLayers.Level;
            node.transform.position = bounds.center;
            BoxCollider collider = node.AddComponent<BoxCollider>();
            collider.size = bounds.size;
            return collider;
        }

        private static Bounds RectBounds(Vector4 rect, float low, float height) =>
            new Bounds(new Vector3((rect.x+rect.z)*.5f, low+height*.5f, (rect.y+rect.w)*.5f),
                       new Vector3(rect.z-rect.x, height, rect.w-rect.y));

        private void BuildWalls(Transform model, Transform mayaModel)
        {
            // R1 to R12 come with the V09 art (the layout keeps five); the Maya pass may add more like them.
            data.walls = data.walls.Concat(maya.walls).ToArray();
            if (data.walls.Length > MaxWalls)
                throw new InvalidOperationException("Ruin Courtyard has more breakable walls than the replicated state holds.");
            walls = new Wall[data.walls.Length];
            for (int i = 0; i < walls.Length; i++)
            {
                WallSpec spec = data.walls[i];
                Transform source = TryFind(model, spec.id + "_Intact") != null ? model : mayaModel;
                Transform intact = Find(source, spec.id + "_Intact");
                Transform broken = Find(source, spec.id + "_Broken");
                // State 1 is a wall blown once now, not R7's cracked floor: R7 is a fixed wall (fixedWalls).
                if (TryFind(source, spec.id + "_Cracked") != null)
                    throw new InvalidOperationException("A cracked floor cannot be a breakable wall any more: " + spec.id);
                Transform cracked = null;
                if (Vector3.Distance(intact.parent.position, spec.position) > .12f)
                    throw new InvalidOperationException("Ruin wall pivot does not align: " + spec.id);
                float floor = cracked != null ? .57f : 0f;
                Bounds bounds = RectBounds(spec.footprintXZ, floor, spec.height);
                BoxCollider collider = AddBox(spec.id + " Blocker", bounds);
                NavMeshObstacle obstacle = null;
                if (cracked == null)
                {
                    obstacle = collider.gameObject.AddComponent<NavMeshObstacle>();
                    obstacle.shape = NavMeshObstacleShape.Box;
                    obstacle.size = bounds.size;
                    obstacle.carving = true;
                }
                collider.enabled = false;
                walls[i] = new Wall { Intact = intact, Broken = broken, Cracked = cracked,
                    Bounds = bounds, Collider = collider, Obstacle = obstacle };
                BuildWallStones(walls[i], mayaModel, spec.id);
            }
            // The shipped breakables the layout no longer lists. Fixed ones stand for good, intact and solid; the
            // rest are gone, foundation and all, so nothing is left without its collider.
            WallSpec[] fixedWalls = data.fixedWalls ?? Array.Empty<WallSpec>();
            foreach (WallSpec spec in fixedWalls)
            {
                Transform cracked = TryFind(model, spec.id + "_Cracked");
                Find(model, spec.id + "_Intact").gameObject.SetActive(true);
                Find(model, spec.id + "_Broken").gameObject.SetActive(false);
                if (cracked != null) cracked.gameObject.SetActive(false);
                AddBox(spec.id + " fixed Blocker", RectBounds(spec.footprintXZ, cracked != null ? .57f : 0f, spec.height));
            }
            foreach (Transform pivot in model.GetComponentsInChildren<Transform>(true))
            {
                if (!pivot.name.EndsWith(" / ground pivot", StringComparison.Ordinal)) continue;
                string id = pivot.name.Substring(0, pivot.name.IndexOf(' '));
                if (!Array.Exists(data.walls, wall => wall.id == id) && !Array.Exists(fixedWalls, wall => wall.id == id))
                    pivot.gameObject.SetActive(false);
            }
        }

        private void BuildVines(Transform environment, Transform orientation)
        {
            coils = new GameObject[data.vinePickups.Length];
            pickupReadyAt = new double[coils.Length];
            for (int i = 0; i < coils.Length; i++)
                coils[i] = Find(environment, $"VinePickup_{i+1:00}").gameObject;
            // Coils the layout no longer lists are not in the level at all.
            foreach (Transform node in environment.GetComponentsInChildren<Transform>(true))
                if (node.name.StartsWith("VinePickup_", StringComparison.Ordinal) &&
                    Array.IndexOf(coils, node.gameObject) < 0 && node.parent != null &&
                    !node.parent.name.StartsWith("VinePickup_", StringComparison.Ordinal))
                    node.gameObject.SetActive(false);
            var model = SpawnModel("RuinCourtyard_Vines");
            model.transform.SetParent(orientation, false);
            patchTemplate = Find(model.transform, "Vine_SlowPatch").gameObject;
            patchTemplate.transform.SetParent(field, true);
            patchTemplate.SetActive(false);
            ladder = Find(model.transform, "Vine_Ladder").gameObject;
            ladder.transform.SetParent(field, true);
            ladder.transform.position = data.ladderAnchor;
            Vector3 modelUp = (Find(ladder.transform, "Rung 01").position - Find(ladder.transform, "Rung 06").position).normalized;
            // Align the top knot and rungs with the east-facing, outward-leaning authored ladder.
            Vector3 desiredUp = new Vector3(-Mathf.Sin(.47f), Mathf.Cos(.47f), 0);
            Quaternion tilt = Quaternion.FromToRotation(modelUp, desiredUp);
            ladder.transform.rotation = tilt * ladder.transform.rotation;
            Vector3 rung = Find(ladder.transform, "Rung 01").right;
            Vector3 flatRung = Vector3.ProjectOnPlane(rung, desiredUp).normalized;
            if (flatRung.sqrMagnitude > .5f)
                ladder.transform.rotation = Quaternion.AngleAxis(
                    Vector3.SignedAngle(flatRung, Vector3.forward, desiredUp), desiredUp) * ladder.transform.rotation;
            ladder.SetActive(false);
            model.SetActive(false);
        }

        private Transform[] MakeSpots(Transform root, Spot[] dataSpots)
        {
            var spots = new Transform[dataSpots.Length];
            for (int i = 0; i < spots.Length; i++)
            {
                Spot spot = dataSpots[i];
                Transform node = new GameObject(spot.name).transform;
                node.SetParent(root, false);
                node.SetPositionAndRotation(spot.position, Quaternion.Euler(0, spot.yaw, 0));
                spots[i] = node;
            }
            return spots;
        }

        private void ConfigureLayout()
        {
            LevelLayout level = GetComponent<LevelLayout>();
            level.collectorSpawns = MakeSpots(transform, data.collectorSpawns);
            level.interrupterSpawns = MakeSpots(transform, data.interrupterSpawns);
            level.smallSpots = MakeSpots(transform, data.smallSpots);
            level.mediumSpots = MakeSpots(transform, data.mediumSpots);
            level.largeSpots = MakeSpots(transform, data.largeSpots);
            ConfigureDepot(level);
            level.bedHalfSize = data.bedHalfSize;
            // The minimap reaches south to the road guardrail (make_ruin_maya.py MAP_SOUTH_EXTRA).
            float mapSouth = GuardrailZ - .3f;
            level.mapArea = new Rect(data.playArea.x, mapSouth, data.playArea.z - data.playArea.x, data.playArea.w - mapSouth);
            level.mapPicture = Load<Texture2D>("RuinMinimap");
            MiniMap map = FindAnyObjectByType<MiniMap>(FindObjectsInactive.Include);
            if (map != null && map.transform is RectTransform frame)
            {
                const float height = 165f, inset = 18f;
                Vector2 size = new Vector2(height*level.mapArea.width/level.mapArea.height+inset, height+inset);
                frame.sizeDelta = size;
                frame.anchoredPosition = new Vector2(-30f-size.x*.5f, -30f-size.y*.5f);
            }
        }
    }
}
