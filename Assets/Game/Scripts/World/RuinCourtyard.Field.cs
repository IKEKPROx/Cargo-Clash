using System;
using System.Collections.Generic;
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

            foreach (DoorSpec spec in data.doors)
            {
                string mountName = spec.id.Substring(0, 2) + "_LampLightMount";
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
            AddBox("West play boundary", new Bounds(new Vector3(-.5f, 5f, data.size.y*.5f), new Vector3(1, 10, data.size.y+2)));
            AddBox("East play boundary", new Bounds(new Vector3(data.size.x+.5f, 5f, data.size.y*.5f), new Vector3(1, 10, data.size.y+2)));
            AddBox("North play boundary", new Bounds(new Vector3(data.size.x*.5f, 5f, data.size.y+.5f), new Vector3(data.size.x, 10, 1)));
            AddBox("South play boundary", new Bounds(new Vector3(data.size.x*.5f, 5f, -.5f), new Vector3(data.size.x, 10, 1)));

            // Thin landing tops preserve the walkable west descent and the continuous upper deck.
            AddBox("Terrace upper deck", RectBounds(new Vector4(56.4f, 37.38f, 71.6f, 44.62f), 2.47f, .03f));
            AddBox("Terrace west landing", RectBounds(new Vector4(54f, 41.8f, 56.28f, 44.2f), 0f, 1.65f));
            AddBox("Terrace lower landing", RectBounds(new Vector4(52.7f, 41.8f, 54f, 44.2f), 0f, .8f));

            GameObject wallModel = SpawnModel("RuinCourtyard_Breakables");
            wallModel.transform.SetParent(rotated, false);
            GameObject doorModel = SpawnModel("RuinCourtyard_OperableDoors");
            doorModel.transform.SetParent(rotated, false);
            ValidateAxisAlignment(environment.transform, doorModel.transform);
            BuildWalls(wallModel.transform);
            BuildDoors(doorModel.transform);
            BuildVines(environment.transform, rotated);
            ConfigureLayout();

            navigation = gameObject.AddComponent<NavMeshSurface>();
            navigation.navMeshData = null;
            navigation.collectObjects = CollectObjects.Volume;
            navigation.center = new Vector3(data.size.x*.5f, 1f, data.size.y*.5f);
            navigation.size = new Vector3(data.size.x, 4f, data.size.y);
            navigation.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            navigation.layerMask = GameLayers.LevelMask;
            Physics.SyncTransforms();
            navigation.BuildNavMesh();
            runtimeNavMesh = navigation.navMeshData;
            ShowState();
        }

        private void ValidateAxisAlignment(Transform environment, Transform doorModel)
        {
            if ((Find(environment, "Coordinate_Origin").position).sqrMagnitude > .01f ||
                Vector3.Distance(Find(environment, "Coordinate_EastX").position, Vector3.right*5f) > .06f ||
                Vector3.Distance(Find(environment, "Coordinate_NorthZ").position, Vector3.forward*5f) > .06f ||
                Vector3.Distance(Find(doorModel, "B1_Doors").position, data.doors[0].center) > .15f)
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

        private void BuildWalls(Transform model)
        {
            walls = new Wall[data.walls.Length];
            for (int i = 0; i < walls.Length; i++)
            {
                WallSpec spec = data.walls[i];
                Transform intact = Find(model, spec.id + "_Intact");
                Transform broken = Find(model, spec.id + "_Broken");
                Transform cracked = spec.id == "R7" ? Find(model, spec.id + "_Cracked") : null;
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
            }
        }

        private void BuildDoors(Transform model)
        {
            doors = new Door[data.doors.Length];
            for (int i = 0; i < doors.Length; i++)
            {
                DoorSpec spec = data.doors[i];
                var door = new Door { Hinges = new Transform[2], Leaves = new Transform[2], Boxes = new BoxCollider[2], Open = new Quaternion[2], Closed = new Quaternion[2],
                    Sweep = new Bounds(spec.center + new Vector3(0, spec.height*.5f, -.78f),
                                       new Vector3(spec.width+.45f, spec.height, 2.65f)) };
                for (int j = 0; j < 2; j++)
                {
                    Transform hinge = Find(model, spec.hinges[j]);
                    Transform leaf = Find(hinge, spec.leaves[j]);
                    door.Hinges[j] = hinge;
                    door.Leaves[j] = leaf;
                    door.Open[j] = hinge.localRotation;
                    // Remove the authored open angle about the hinge's world-up direction.
                    Vector3 axis = hinge.parent.InverseTransformDirection(Vector3.up);
                    float orientation = Mathf.Sign(hinge.parent.localToWorldMatrix.determinant);
                    // The authored leaf centre points toward the opposite hinge in the closed pose.
                    Vector3 toCentre = spec.center - hinge.position;
                    toCentre.y = 0;
                    Vector3 toLeaf = leaf.position - hinge.position;
                    toLeaf.y = 0;
                    float closeBy = Vector3.SignedAngle(toLeaf, toCentre, Vector3.up);
                    if (orientation < 0) closeBy = -closeBy;
                    door.Closed[j] = Quaternion.AngleAxis(closeBy, axis) * door.Open[j];
                    BoxCollider box = AddBox(spec.id + " leaf Blocker",
                        new Bounds(leaf.position, new Vector3(spec.width*.5f, spec.height*.88f, .28f)));
                    NavMeshObstacle obstacle = box.gameObject.AddComponent<NavMeshObstacle>();
                    obstacle.shape = NavMeshObstacleShape.Box;
                    obstacle.size = box.size;
                    obstacle.carving = true;
                    box.enabled = false;
                    door.Boxes[j] = box;
                }
                doors[i] = door;
            }
        }

        private void BuildVines(Transform environment, Transform orientation)
        {
            coils = new GameObject[data.vinePickups.Length];
            pickupReadyAt = new double[coils.Length];
            for (int i = 0; i < coils.Length; i++)
                coils[i] = Find(environment, $"VinePickup_{i+1:00}").gameObject;
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
            level.truckZone.position = data.truckZone;
            level.truckZone.rotation = Quaternion.identity;
            level.truckZoneHalfSize = data.truckZoneHalfSize;
            Transform zoneFloor = level.truckZone.Find("ZoneFloor");
            if (zoneFloor != null) zoneFloor.localScale = new Vector3(data.truckZoneHalfSize.x*2, .01f, data.truckZoneHalfSize.y*2);
            level.bedHalfSize = data.bedHalfSize;
            level.mapArea = new Rect(0, 0, data.size.x, data.size.y);
            level.mapPicture = Load<Texture2D>("RuinMinimap");
            MiniMap map = FindAnyObjectByType<MiniMap>(FindObjectsInactive.Include);
            if (map != null && map.transform is RectTransform frame)
            {
                const float height = 165f, inset = 18f;
                Vector2 size = new Vector2(height*data.size.x/data.size.y+inset, height+inset);
                frame.sizeDelta = size;
                frame.anchoredPosition = new Vector2(-30f-size.x*.5f, -30f-size.y*.5f);
            }
        }
    }
}
