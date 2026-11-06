using UnityEngine;

/// <summary>Streams a deterministic, collider-backed terrain strip around the rover.</summary>
[DefaultExecutionOrder(-200)]
public sealed class CargoClashEndlessTerrain : MonoBehaviour
{
    private const int ChunkSize = 64;
    private const int HalfChunkCount = 4;
    private const int ZSegments = 160;
    private const float HalfWidthZ = 160f;

    [SerializeField] private Transform target;
    [SerializeField] private MeshRenderer previewRenderer;
    [SerializeField] private MeshCollider previewCollider;

    private readonly GameObject[] chunks = new GameObject[HalfChunkCount * 2 + 1];
    private readonly Mesh[] meshes = new Mesh[HalfChunkCount * 2 + 1];
    private int centerChunk = int.MinValue;

    public void Configure(Transform followTarget, MeshRenderer editorPreview, MeshCollider editorCollider)
    {
        target = followTarget;
        previewRenderer = editorPreview;
        previewCollider = editorCollider;
    }

    private void Awake()
    {
        if (target == null || previewRenderer == null)
        {
            Debug.LogError("Endless terrain needs a target and preview material.", this);
            enabled = false;
            return;
        }

        previewRenderer.enabled = false;
        if (previewCollider != null) previewCollider.enabled = false;
        for (int i = 0; i < chunks.Length; i++)
        {
            GameObject chunk = new GameObject("TerrainChunk_Unassigned_" + i);
            chunk.transform.SetParent(transform, false);
            chunk.layer = previewRenderer.gameObject.layer;
            chunk.AddComponent<MeshFilter>();
            chunk.AddComponent<MeshRenderer>().sharedMaterial = previewRenderer.sharedMaterial;
            chunk.AddComponent<MeshCollider>();
            chunks[i] = chunk;
        }
        RefreshNow();
    }

    private void Update() => RefreshNow();

    private void OnEnable()
    {
        // Unity can reload scripts during Play without re-running Awake. Recover
        // the already-rendered chunk objects before the next frame updates them.
        if (!Application.isPlaying || chunks[0] != null) return;
        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith("TerrainChunk_") ||
                !int.TryParse(child.name.Substring("TerrainChunk_".Length), out int chunkIndex))
                continue;
            int slot = (int)(((long)chunkIndex % chunks.Length + chunks.Length) % chunks.Length);
            chunks[slot] = child.gameObject;
            meshes[slot] = child.GetComponent<MeshFilter>().sharedMesh;
        }
        if (chunks[0] == null) return;
        centerChunk = int.MinValue;
        RefreshNow();
    }

    public void RefreshNow()
    {
        if (target == null || chunks[0] == null) return;
        int nextCenter = Mathf.FloorToInt(target.position.x / ChunkSize);
        if (nextCenter == centerChunk) return;

        centerChunk = nextCenter;
        for (int offset = -HalfChunkCount; offset <= HalfChunkCount; offset++)
        {
            int chunkIndex = nextCenter + offset;
            int slot = (int)(((long)chunkIndex % chunks.Length + chunks.Length) % chunks.Length);
            GameObject chunk = chunks[slot];
            if (chunk.name == "TerrainChunk_" + chunkIndex) continue;

            chunk.name = "TerrainChunk_" + chunkIndex;
            MeshCollider collider = chunk.GetComponent<MeshCollider>();
            collider.sharedMesh = null;
            if (meshes[slot] != null) Destroy(meshes[slot]);
            Mesh mesh = CreateChunkMesh(chunkIndex);
            meshes[slot] = mesh;
            chunk.GetComponent<MeshFilter>().sharedMesh = mesh;
            chunk.transform.localPosition = new Vector3(chunkIndex * ChunkSize, 0f, 0f);
            collider.sharedMesh = mesh;
        }
        Physics.SyncTransforms();
    }

    private void OnDestroy()
    {
        foreach (Mesh mesh in meshes)
            if (mesh != null) Destroy(mesh);
    }

    public static float HeightAt(float x, float z)
    {
        return -0.055f +
            0.42f * Mathf.Sin(z * 0.22f + x * 0.19f) +
            0.25f * Mathf.Sin(z * 0.53f - x * 0.37f) +
            0.14f * Mathf.Sin(x * 0.47f + z * 0.29f) +
            0.18f * Mathf.Sin(x * 0.075f + z * 0.06f);
    }

    private static Mesh CreateChunkMesh(int chunkIndex)
    {
        const int xSegments = ChunkSize;
        const int row = xSegments + 1;
        Vector3[] vertices = new Vector3[row * (ZSegments + 1)];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[xSegments * ZSegments * 6];
        float centerX = chunkIndex * ChunkSize;

        for (int z = 0; z <= ZSegments; z++)
        {
            float worldZ = -HalfWidthZ + 2f * HalfWidthZ * z / ZSegments;
            for (int x = 0; x <= xSegments; x++)
            {
                float localX = x - ChunkSize * 0.5f;
                float worldX = centerX + localX;
                int index = z * row + x;
                vertices[index] = new Vector3(localX, HeightAt(worldX, worldZ), worldZ);
                const float step = 0.1f;
                float slopeX = (HeightAt(worldX + step, worldZ) -
                    HeightAt(worldX - step, worldZ)) / (2f * step);
                float slopeZ = (HeightAt(worldX, worldZ + step) -
                    HeightAt(worldX, worldZ - step)) / (2f * step);
                normals[index] = new Vector3(-slopeX, 1f, -slopeZ).normalized;
                uvs[index] = new Vector2(worldX * 0.02f, worldZ * 0.02f);
            }
        }

        int t = 0;
        for (int z = 0; z < ZSegments; z++)
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
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }
}
