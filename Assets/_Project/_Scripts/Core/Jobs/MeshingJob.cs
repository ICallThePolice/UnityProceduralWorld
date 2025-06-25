using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[BurstCompile]
public struct MeshingJob : IJob
{
    [ReadOnly] public NativeArray<ushort> voxelIDs;
    [ReadOnly] public NativeArray<Vector2Int> voxelUvCoordinates;
    [ReadOnly] public bool hasNeighborPosX, hasNeighborNegX, hasNeighborPosZ, hasNeighborNegZ;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsPosX, neighborVoxelsNegX, neighborVoxelsPosZ, neighborVoxelsNegZ;
    [ReadOnly] public NativeArray<BiomeInstanceBurst> biomeInstances;
    [ReadOnly] public BiomeInstanceBurst neutralBiome;
    [ReadOnly] public FastNoiseLite biomeBlendNoise;
    [ReadOnly] public Vector3Int chunkPosition;

    public NativeList<Vertex> vertices;
    public NativeList<int> triangles;

    private const int AtlasSizeInTextures = 2; // У нас атлас 2x2 текстуры
    private const float TextureOffset = 1f / AtlasSizeInTextures;

    public void Execute()
    {
        for (int y = 0; y < Chunk.Height; y++)
            for (int z = 0; z < Chunk.Width; z++)
                for (int x = 0; x < Chunk.Width; x++)
                {
                    ushort currentVoxelId = GetVoxelID(x, y, z, true);
                    if (currentVoxelId == 0) continue; // Пропускаем воздух

                    // Проверяем каждую из 6 сторон
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
            Vector2 uv = VoxelData.FaceUVs[i];

            Vector2 worldPos = new Vector2(voxelLocalPosition.x + chunkPosition.x * Chunk.Width, voxelLocalPosition.z + chunkPosition.z * Chunk.Width);
            Color vertexColor = CalculateBiomeBlendColor(worldPos);

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
        // Находим два самых влиятельных биома (логика, похожая на ту, что была в GenerationJob)
        BiomeInstanceBurst biomeA = neutralBiome;
        BiomeInstanceBurst biomeB = neutralBiome;
        float influenceA = -1f;
        float influenceB = -1f;

        for (int i = 0; i < biomeInstances.Length; i++)
        {
            float distance = Vector2.Distance(worldPos, biomeInstances[i].position);
            if (distance < biomeInstances[i].influenceRadius)
            {
                float currentInfluence = Mathf.Pow(1f - (distance / biomeInstances[i].influenceRadius), biomeInstances[i].contrast);
                if (currentInfluence > influenceA)
                {
                    influenceB = influenceA;
                    biomeB = biomeA;
                    influenceA = currentInfluence;
                    biomeA = biomeInstances[i];
                }
                else if (currentInfluence > influenceB)
                {
                    influenceB = currentInfluence;
                    biomeB = biomeInstances[i];
                }
            }
        }

        // Упаковываем данные в цвет:
        // R = ID биома A, G = ID биома B, B = Сила смешивания, A = не используется
        float blendStrength = (influenceB > 0) ? (influenceB / (influenceA + influenceB)) : 0;
        
        return new Color(biomeA.surfaceVoxelID / 255f, biomeB.surfaceVoxelID / 255f, blendStrength, 1);
    }

    private bool IsVoxelTransparent(int x, int y, int z)
    {
        // Проверка выхода за пределы текущего чанка
        if (y < 0 || y >= Chunk.Height) return true;

        Vector3Int pos = new Vector3Int(x, y, z);
        Vector3Int neighborChunkOffset = Vector3Int.zero;

        if (x < 0) neighborChunkOffset.x = -1;
        else if (x >= Chunk.Width) neighborChunkOffset.x = 1;
        if (z < 0) neighborChunkOffset.z = -1;
        else if (z >= Chunk.Width) neighborChunkOffset.z = 1;

        if (neighborChunkOffset == Vector3Int.zero)
        {
            return voxelIDs[GetIndex(x, y, z)] == 0;
        }
        else if (neighborChunkOffset.x == 1 && hasNeighborPosX) return neighborVoxelsPosX[GetIndex(0, y, z)] == 0;
        else if (neighborChunkOffset.x == -1 && hasNeighborNegX) return neighborVoxelsNegX[GetIndex(Chunk.Width - 1, y, z)] == 0;
        else if (neighborChunkOffset.z == 1 && hasNeighborPosZ) return neighborVoxelsPosZ[GetIndex(x, y, 0)] == 0;
        else if (neighborChunkOffset.z == -1 && hasNeighborNegZ) return neighborVoxelsNegZ[GetIndex(x, y, Chunk.Width - 1)] == 0;
        else return true;
    }




    private ushort GetVoxelID(int x, int y, int z, bool isOwn)
    {
        if (isOwn) return voxelIDs[z + x * Chunk.Width + y * (Chunk.Width * Chunk.Width)];

        // Эта логика сейчас не используется из-за упрощения в IsVoxelTransparent, но она здесь на будущее
        if (x < 0) return hasNeighborNegX ? neighborVoxelsNegX[z + (Chunk.Width - 1) * Chunk.Width + y * (Chunk.Width * Chunk.Width)] : (ushort)0;
        if (x >= Chunk.Width) return hasNeighborPosX ? neighborVoxelsPosX[z + 0 * Chunk.Width + y * (Chunk.Width * Chunk.Width)] : (ushort)0;
        if (z < 0) return hasNeighborNegZ ? neighborVoxelsNegZ[(Chunk.Width - 1) + x * Chunk.Width + y * (Chunk.Width * Chunk.Width)] : (ushort)0;
        if (z >= Chunk.Width) return hasNeighborPosZ ? neighborVoxelsPosZ[0 + x * Chunk.Width + y * (Chunk.Width * Chunk.Width)] : (ushort)0;

        return 0;
    }

    private int GetIndex(int x, int y, int z) => z + x * Chunk.Width + y * Chunk.Width * Chunk.Width;

}
    /// <summary>
    /// Статический класс-помощник, содержащий константные данные о геометрии одного вокселя.
    /// Используется только внутри MeshingJob.
    /// </summary>
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
        public static readonly Vector2[] FaceUVs = new Vector2[4]
        {
            new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1)
        };
    }