using UnityEngine;

/// <summary>
/// Streams a deterministic, collider-backed strip of countryside around the truck: the ground, and
/// on top of it the road and the far fields, which follow the ground's bumps.
/// </summary>
[DefaultExecutionOrder(-200)]
public sealed class CargoClashEndlessTerrain : MonoBehaviour
{
    private const int ChunkSize = 64;
    private const int HalfChunkCount = 4;
    private const int ZSegments = 160;
    private const float HalfWidthZ = 160f;
    private const float RowSpacing = 2f * HalfWidthZ / ZSegments;

    private const float RoadHalfWidth = 10f;
    private const float RoadTileLength = 6f;
    private const float RoadLift = 0.02f;

    private const float FieldsNearZ = 46f;
    private const float FieldsTileLength = 184f;
    private const float FieldsLift = 0.05f;

    [SerializeField] private Transform target;
    [SerializeField] private Material groundMaterial;
    [SerializeField] private Material roadMaterial;
    [SerializeField] private Material fieldsMaterial;

    private sealed class Chunk
    {
        public GameObject root;
        public int index = int.MinValue;
        public MeshFilter ground;
        public MeshCollider collider;
        public MeshFilter road;
        public MeshFilter fields;
    }

    private readonly Chunk[] chunks = new Chunk[HalfChunkCount * 2 + 1];
    private int centerChunk = int.MinValue;

    public void Configure(Transform followTarget, Material ground, Material road, Material fields)
    {
        target = followTarget;
        groundMaterial = ground;
        roadMaterial = road;
        fieldsMaterial = fields;
    }

    private void Awake()
    {
        for (int i = 0; i < chunks.Length; i++)
        {
            GameObject root = new GameObject("TerrainChunk", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            root.transform.SetParent(transform, false);
            root.layer = gameObject.layer;
            root.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
            chunks[i] = new Chunk
            {
                root = root,
                ground = root.GetComponent<MeshFilter>(),
                collider = root.GetComponent<MeshCollider>(),
                road = AddStrip("Road", root.transform, roadMaterial),
                fields = AddStrip("Fields", root.transform, fieldsMaterial),
            };
        }
        RefreshChunks();
    }

    private static MeshFilter AddStrip(string name, Transform parent, Material material)
    {
        GameObject strip = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        strip.transform.SetParent(parent, false);
        MeshRenderer renderer = strip.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return strip.GetComponent<MeshFilter>();
    }

    private void Update() => RefreshChunks();

    private void RefreshChunks()
    {
        int nextCenter = Mathf.FloorToInt(target.position.x / ChunkSize);
        if (nextCenter == centerChunk) return;

        centerChunk = nextCenter;
        for (int offset = -HalfChunkCount; offset <= HalfChunkCount; offset++)
        {
            int chunkIndex = nextCenter + offset;
            Chunk chunk = chunks[(int)(((long)chunkIndex % chunks.Length + chunks.Length) % chunks.Length)];
            if (chunk.index == chunkIndex) continue;
            chunk.index = chunkIndex;
            chunk.root.name = "TerrainChunk_" + chunkIndex;
            chunk.root.transform.localPosition = new Vector3(chunkIndex * ChunkSize, 0f, 0f);

            chunk.collider.sharedMesh = null;
            Replace(chunk.ground, CreateGroundMesh(chunkIndex));
            chunk.collider.sharedMesh = chunk.ground.sharedMesh;
            Replace(chunk.road, CreateStripMesh(chunkIndex, -RoadHalfWidth, RoadHalfWidth, RoadLift, RoadUv));
            Replace(chunk.fields, CreateStripMesh(chunkIndex, FieldsNearZ, HalfWidthZ, FieldsLift, FieldsUv));
        }
        Physics.SyncTransforms();
    }

    private static void Replace(MeshFilter filter, Mesh mesh)
    {
        if (filter.sharedMesh != null) Destroy(filter.sharedMesh);
        filter.sharedMesh = mesh;
    }

    private void OnDestroy()
    {
        foreach (Chunk chunk in chunks)
        {
            if (chunk == null) continue;
            Destroy(chunk.ground.sharedMesh);
            Destroy(chunk.road.sharedMesh);
            Destroy(chunk.fields.sharedMesh);
        }
    }

    public static float HeightAt(float x, float z)
    {
        return -0.055f +
            0.42f * Mathf.Sin(z * 0.22f + x * 0.19f) +
            0.25f * Mathf.Sin(z * 0.53f - x * 0.37f) +
            0.14f * Mathf.Sin(x * 0.47f + z * 0.29f) +
            0.18f * Mathf.Sin(x * 0.075f + z * 0.06f);
    }

    private static Vector3 NormalAt(float worldX, float worldZ)
    {
        const float step = 0.1f;
        float slopeX = (HeightAt(worldX + step, worldZ) - HeightAt(worldX - step, worldZ)) / (2f * step);
        float slopeZ = (HeightAt(worldX, worldZ + step) - HeightAt(worldX, worldZ - step)) / (2f * step);
        return new Vector3(-slopeX, 1f, -slopeZ).normalized;
    }

    private static Vector2 GroundUv(float worldX, float worldZ) => new Vector2(worldX * 0.02f, worldZ * 0.02f);

    // Across the road, then along it.
    private static Vector2 RoadUv(float worldX, float worldZ) =>
        new Vector2((worldZ + RoadHalfWidth) / (2f * RoadHalfWidth), worldX / RoadTileLength);

    // A hair inside the top and bottom of the picture, so its two edges don't blend together.
    private static Vector2 FieldsUv(float worldX, float worldZ) =>
        new Vector2(worldX / FieldsTileLength, Mathf.Lerp(0.002f, 0.998f, (worldZ - FieldsNearZ) / (HalfWidthZ - FieldsNearZ)));

    private static Mesh CreateGroundMesh(int chunkIndex) =>
        CreateStripMesh(chunkIndex, -HalfWidthZ, HalfWidthZ, 0f, GroundUv);

    /// <summary>
    /// A band along the X axis from one Z to another, on the ground's own grid and lifted a little
    /// above it, so it can't dip under the ground anywhere.
    /// </summary>
    private static Mesh CreateStripMesh(int chunkIndex, float fromZ, float toZ, float lift, System.Func<float, float, Vector2> uv)
    {
        const int xSegments = ChunkSize;
        const int row = xSegments + 1;
        int zSegments = Mathf.RoundToInt((toZ - fromZ) / RowSpacing);
        Vector3[] vertices = new Vector3[row * (zSegments + 1)];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[xSegments * zSegments * 6];
        float centerX = chunkIndex * ChunkSize;

        for (int z = 0; z <= zSegments; z++)
        {
            float worldZ = fromZ + z * RowSpacing;
            for (int x = 0; x <= xSegments; x++)
            {
                float localX = x - ChunkSize * 0.5f;
                float worldX = centerX + localX;
                int index = z * row + x;
                vertices[index] = new Vector3(localX, HeightAt(worldX, worldZ) + lift, worldZ);
                normals[index] = NormalAt(worldX, worldZ);
                uvs[index] = uv(worldX, worldZ);
            }
        }

        int t = 0;
        for (int z = 0; z < zSegments; z++)
        for (int x = 0; x < xSegments; x++)
        {
            int a = z * row + x;
            int b = a + 1;
            int c = a + row;
            int d = c + 1;
            triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
            triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
        }

        Mesh mesh = new Mesh { name = "StreamedTerrain_" + chunkIndex };
        if (vertices.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }
}
