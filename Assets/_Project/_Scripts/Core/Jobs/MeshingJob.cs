using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[BurstCompile]
public struct MeshingJob : IJob
{
    [ReadOnly] public Vector3Int chunkPosition;
    [ReadOnly] public NativeArray<ushort> voxelIDs;
    [ReadOnly] public NativeArray<Vector2Int> voxelUvCoordinates;
    [ReadOnly] public bool hasNeighborPosX, hasNeighborNegX, hasNeighborPosZ, hasNeighborNegZ;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsPosX, neighborVoxelsNegX, neighborVoxelsPosZ, neighborVoxelsNegZ;
    [ReadOnly] public NativeArray<BiomeInstanceBurst> biomeInstances;
    [ReadOnly] public BiomeInstanceBurst neutralBiome;
    [ReadOnly] public FastNoiseLite biomeBlendNoise;

    public NativeList<Vertex> vertices;
    public NativeList<int> triangles;

    private const int AtlasSizeInTextures = 2;
    private const float TextureOffset = 1f / AtlasSizeInTextures;

    public void Execute()
    {
        for (int y = 0; y < Chunk.Height; y++)
            for (int z = 0; z < Chunk.Width; z++)
                for (int x = 0; x < Chunk.Width; x++)
                {
                    ushort currentVoxelId = voxelIDs[GetIndex(x, y, z)];
                    if (currentVoxelId == 0) continue;

                    if (IsVoxelTransparent(x, y + 1, z)) CreateFace(Direction.Top, new Vector3(x, y, z), currentVoxelId);
                    if (IsVoxelTransparent(x, y - 1, z)) CreateFace(Direction.Bottom, new Vector3(x, y, z), currentVoxelId);
                    if (IsVoxelTransparent(x + 1, y, z)) CreateFace(Direction.Right, new Vector3(x, y, z), currentVoxelId);
                    if (IsVoxelTransparent(x - 1, y, z)) CreateFace(Direction.Left, new Vector3(x, y, z), currentVoxelId);
                    if (IsVoxelTransparent(x, y, z + 1)) CreateFace(Direction.Front, new Vector3(x, y, z), currentVoxelId);
                    if (IsVoxelTransparent(x, y, z - 1)) CreateFace(Direction.Back, new Vector3(x, y, z), currentVoxelId);
                }
    }

    private void CreateFace(Direction direction, Vector3 voxelLocalPosition, ushort voxelId)
    {
        int vertexIndex = vertices.Length;
        int vertStartIndex = (int)direction * 4;
        Vector2Int atlasCoord = (voxelId < voxelUvCoordinates.Length) ? voxelUvCoordinates[voxelId] : new Vector2Int(0, 0);

        for (int i = 0; i < 4; i++)
        {
            Vector3 vertexPosition = voxelLocalPosition + VoxelData.FaceVertices[vertStartIndex + i];
            Vector3 vertexWorldPos3D = vertexPosition + (Vector3)chunkPosition * Chunk.Width;
            Color vertexColor = CalculateBiomeBlendColor(new Vector2(vertexWorldPos3D.x, vertexWorldPos3D.z));

            Vector2 uv = VoxelData.FaceUVs[i];
            float uv_x = (atlasCoord.x + uv.x) * TextureOffset;
            float uv_y = (atlasCoord.y + uv.y) * TextureOffset;

            vertices.Add(new Vertex(vertexPosition, new Vector2(uv_x, uv_y), vertexColor));
        }

        triangles.Add(vertexIndex + VoxelData.TriangleIndices[0]);
        triangles.Add(vertexIndex + VoxelData.TriangleIndices[1]);
        triangles.Add(vertexIndex + VoxelData.TriangleIndices[2]);
        triangles.Add(vertexIndex + VoxelData.TriangleIndices[3]);
        triangles.Add(vertexIndex + VoxelData.TriangleIndices[4]);
        triangles.Add(vertexIndex + VoxelData.TriangleIndices[5]);
    }

    private Color CalculateBiomeBlendColor(Vector2 worldPos)
    {
        BiomeInstanceBurst biomeA = neutralBiome;
        BiomeInstanceBurst biomeB = neutralBiome;
        float influenceA = 0f;
        float influenceB = -1f;

        for (int i = 0; i < biomeInstances.Length; i++)
        {
            var biome = biomeInstances[i];
            float distance = Vector2.Distance(worldPos, biome.position);
            if (distance < biome.influenceRadius)
            {
                float currentInfluence = 1f - (distance / biome.influenceRadius);
                if (currentInfluence > influenceA)
                {
                    influenceB = influenceA;
                    biomeB = biomeA;
                    influenceA = currentInfluence;
                    biomeA = biome;
                }
                else if (currentInfluence > influenceB)
                {
                    influenceB = currentInfluence;
                    biomeB = biome;
                }
            }
        }

        float blendStrength = (influenceB > 0) ? Mathf.Clamp01(influenceB / influenceA) : 0;

        return new Color(biomeA.surfaceVoxelID / 255f, biomeB.surfaceVoxelID / 255f, blendStrength, 1);
    }

    private bool IsVoxelTransparent(int x, int y, int z)
    {
        if (y < 0 || y >= Chunk.Height) return true;

        if (x >= 0 && x < Chunk.Width && z >= 0 && z < Chunk.Width)
        {
            return voxelIDs[GetIndex(x, y, z)] == 0;
        }
        if (x < 0) return !hasNeighborNegX || neighborVoxelsNegX[GetIndex(Chunk.Width - 1, y, z)] == 0;
        if (x >= Chunk.Width) return !hasNeighborPosX || neighborVoxelsPosX[GetIndex(0, y, z)] == 0;
        if (z < 0) return !hasNeighborNegZ || neighborVoxelsNegZ[GetIndex(x, y, Chunk.Width - 1)] == 0;
        if (z >= Chunk.Width) return !hasNeighborPosZ || neighborVoxelsPosZ[GetIndex(x, y, 0)] == 0;

        return true;
    }

    private int GetIndex(int x, int y, int z) => z + x * Chunk.Width + y * Chunk.Width * Chunk.Width;
}
public static class VoxelData
{
    // Порядок вершин для каждой грани:
    // 0: Нижний-Левый, 1: Верхний-Левый, 2: Нижний-Правый, 3: Верхний-Правый
    public static readonly Vector3[] FaceVertices = new Vector3[24]
    {
        // Direction.Back (-Z)
        new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), 
        // Direction.Front (+Z)
        new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 0, 1), new Vector3(0, 1, 1),
        // Direction.Top (+Y)
        new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 0), new Vector3(1, 1, 1),
        // Direction.Bottom (-Y)
        new Vector3(0, 0, 1), new Vector3(0, 0, 0), new Vector3(1, 0, 1), new Vector3(1, 0, 0),
        // Direction.Left (-X)
        new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 0), new Vector3(0, 1, 0),
        // Direction.Right (+X)
        new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 1), new Vector3(1, 1, 1)
    };

    // Порядок индексов для создания двух треугольников из 4 вершин
    public static readonly int[] TriangleIndices = new int[6] { 0, 1, 2, 2, 1, 3 };
    
    // UV-координаты для одной грани
    public static readonly Vector2[] FaceUVs = new Vector2[4]
    {
        new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1)
    };
}