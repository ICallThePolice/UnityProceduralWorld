using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;

[BurstCompile]
public struct MeshingJob : IJob
{
    // --- Входные данные: Готовые карты свойств ---
    [ReadOnly] public NativeArray<ushort> primaryBlockIDs;
    [ReadOnly] public NativeArray<Color32> finalColors;
    [ReadOnly] public NativeArray<float2> finalUv0s;
    [ReadOnly] public NativeArray<float2> finalUv1s;
    [ReadOnly] public NativeArray<float> finalTexBlends;
    [ReadOnly] public NativeArray<float4> finalEmissionData;
    [ReadOnly] public NativeArray<float4> finalGapColors;
    [ReadOnly] public NativeArray<float2> finalMaterialProps;
    [ReadOnly] public NativeArray<float> finalGapWidths;
    [ReadOnly] public NativeArray<float3> finalBevelData;

    // --- Данные о соседях (только ID для определения видимости граней) ---
    [ReadOnly] public NativeArray<ushort> neighborVoxelsPosX;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsNegX;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsPosY;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsNegY;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsPosZ;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsNegZ;

    // --- Карта типов вокселей (нужна только для флага isSolid) ---
    [ReadOnly] public NativeArray<VoxelTypeDataBurst> voxelTypeMap;
    [ReadOnly] public float2 atlasSizeInTiles;

    // --- Выходные данные для меша ---
    public NativeList<Vertex> vertices;
    public NativeList<int> triangles;

    public void Execute()
    {
        for (int y = 0; y < Chunk.Height; y++)
            for (int x = 0; x < Chunk.Width; x++)
                for (int z = 0; z < Chunk.Width; z++)
                {
                    int voxelIndex = Chunk.GetVoxelIndex(x, y, z);
                    ushort blockID = primaryBlockIDs[voxelIndex];

                    // Пропускаем пустые воксели (воздух)
                    if (blockID == 0) continue;

                    // Проверяем каждого соседа и создаем грань, если сосед прозрачный
                    if (IsVoxelTransparent(x, y + 1, z)) CreateFace(voxelIndex, new int3(x, y, z), Direction.Top);
                    if (IsVoxelTransparent(x, y - 1, z)) CreateFace(voxelIndex, new int3(x, y, z), Direction.Bottom);
                    if (IsVoxelTransparent(x + 1, y, z)) CreateFace(voxelIndex, new int3(x, y, z), Direction.Right);
                    if (IsVoxelTransparent(x - 1, y, z)) CreateFace(voxelIndex, new int3(x, y, z), Direction.Left);
                    if (IsVoxelTransparent(x, y, z + 1)) CreateFace(voxelIndex, new int3(x, y, z), Direction.Front);
                    if (IsVoxelTransparent(x, y, z - 1)) CreateFace(voxelIndex, new int3(x, y, z), Direction.Back);
                }
    }

    private void CreateFace(int voxelIndex, int3 localPos, Direction direction)
    {
        int vertexIndexOffset = vertices.Length;
        int dirIndex = (int)direction;

        // Создаем 4 вершины для грани
        for (int i = 0; i < 4; i++)
        {
            vertices.Add(new Vertex
            {
                position = localPos + VoxelData.FaceVertices[dirIndex * 4 + i],
                normal = VoxelData.FaceNormals[dirIndex],
                
                // ИСПРАВЛЕНО: Правильный расчет UV
                uv0 = (finalUv0s[voxelIndex] + VoxelData.FaceUVs[i]) / atlasSizeInTiles,
                uv1 = (finalUv1s[voxelIndex] + VoxelData.FaceUVs[i]) / atlasSizeInTiles,

                // Остальные данные просто берем из готовых массивов
                color = finalColors[voxelIndex],
                texBlend = finalTexBlends[voxelIndex],
                emissionData = finalEmissionData[voxelIndex],
                gapColor = finalGapColors[voxelIndex],
                materialProps = finalMaterialProps[voxelIndex],
                gapWidth = finalGapWidths[voxelIndex],
                bevelData = finalBevelData[voxelIndex]
            });
        }

        triangles.Add(vertexIndexOffset);
        triangles.Add(vertexIndexOffset + 1);
        triangles.Add(vertexIndexOffset + 2);
        triangles.Add(vertexIndexOffset);
        triangles.Add(vertexIndexOffset + 2);
        triangles.Add(vertexIndexOffset + 3);
    }

    private bool IsVoxelTransparent(int x, int y, int z)
    {
        if (y < 0 || y >= Chunk.Height) return true;
        
        ushort id = GetVoxelID(new int3(x, y, z));
        
        if (id == 0) return true;
        if (id >= voxelTypeMap.Length) return false; // Безопасность
        
        return !voxelTypeMap[id].isSolid;
    }

    private ushort GetVoxelID(int3 pos)
    {
        if (pos.x >= 0 && pos.x < Chunk.Width && pos.y >= 0 && pos.y < Chunk.Height && pos.z >= 0 && pos.z < Chunk.Width)
        {
            return primaryBlockIDs[Chunk.GetVoxelIndex(pos.x, pos.y, pos.z)];
        }
        
        if (pos.x < 0)           return (neighborVoxelsNegX.Length > 0) ? neighborVoxelsNegX[Chunk.GetVoxelIndex(Chunk.Width + pos.x, pos.y, pos.z)] : (ushort)0;
        if (pos.x >= Chunk.Width)  return (neighborVoxelsPosX.Length > 0) ? neighborVoxelsPosX[Chunk.GetVoxelIndex(pos.x - Chunk.Width, pos.y, pos.z)] : (ushort)0;
        if (pos.z < 0)           return (neighborVoxelsNegZ.Length > 0) ? neighborVoxelsNegZ[Chunk.GetVoxelIndex(pos.x, pos.y, Chunk.Width + pos.z)] : (ushort)0;
        if (pos.z >= Chunk.Width)  return (neighborVoxelsPosZ.Length > 0) ? neighborVoxelsPosZ[Chunk.GetVoxelIndex(pos.x, pos.y, pos.z - Chunk.Width)] : (ushort)0;
        if (pos.y < 0)           return (neighborVoxelsNegY.Length > 0) ? neighborVoxelsNegY[Chunk.GetVoxelIndex(pos.x, Chunk.Height + pos.y, pos.z)] : (ushort)0;
        if (pos.y >= Chunk.Height) return (neighborVoxelsPosY.Length > 0) ? neighborVoxelsPosY[Chunk.GetVoxelIndex(pos.x, pos.y - Chunk.Height, pos.z)] : (ushort)0;

        return 0;
    }
}