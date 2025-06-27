using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;

[BurstCompile]
public struct MeshingJob : IJob
{
    [ReadOnly] public NativeArray<ushort> primaryBlockIDs;
    [ReadOnly] public NativeArray<ushort> secondaryBlockIDs;
    [ReadOnly] public NativeArray<float> blendFactors;
    [ReadOnly] public NativeArray<Vector2> voxelIdToUvMap;
    [ReadOnly] public NativeArray<Color32> voxelIdToColorMap;
    [ReadOnly] public NativeArray<Vector4> voxelIdToEmissionDataMap;
    [ReadOnly] public NativeArray<Vector4> voxelIdToGapColorMap;
    [ReadOnly] public NativeArray<Vector2> voxelIdToMaterialPropsMap;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsPosX;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsNegX;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsPosZ;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsNegZ;
    [ReadOnly] public NativeArray<VoxelCategory> voxelIdToCategoryMap;
    
    [ReadOnly] public Vector2 atlasTileSize;

    public NativeList<Vertex> vertices;
    public NativeList<int> triangles;

    public void Execute()
    {
        for (int y = 0; y < Chunk.Height; y++)
        for (int x = 0; x < Chunk.Width; x++)
        for (int z = 0; z < Chunk.Width; z++)
        {
            // ИЗМЕНЕНО: Используем i вместо index
            int i = Chunk.GetVoxelIndex(x, y, z);
            if (primaryBlockIDs[i] == 0) continue;
            
            // ИЗМЕНЕНО: Передаем 'i' в CreateFace
            if (IsVoxelTransparent(x, y + 1, z)) CreateFace(Direction.Top,    new Vector3(x, y, z), i);
            if (IsVoxelTransparent(x, y - 1, z)) CreateFace(Direction.Bottom, new Vector3(x, y, z), i);
            if (IsVoxelTransparent(x + 1, y, z)) CreateFace(Direction.Right,  new Vector3(x, y, z), i);
            if (IsVoxelTransparent(x - 1, y, z)) CreateFace(Direction.Left,   new Vector3(x, y, z), i);
            if (IsVoxelTransparent(x, y, z + 1)) CreateFace(Direction.Front,  new Vector3(x, y, z), i);
            if (IsVoxelTransparent(x, y, z - 1)) CreateFace(Direction.Back,   new Vector3(x, y, z), i);
        }
    }

    private void CreateFace(Direction direction, Vector3 voxelLocalPosition, int voxelIndex)
    {
        int vertexIndex = vertices.Length;

            // --- ПОДГОТОВКА ДАННЫХ (без изменений) ---
            ushort pBlockID = primaryBlockIDs[voxelIndex];
            ushort sBlockID = secondaryBlockIDs[voxelIndex];
            float blend = blendFactors[voxelIndex];

            Color32 pColor = voxelIdToColorMap[pBlockID];
            Color32 sColor = sBlockID > 0 ? voxelIdToColorMap[sBlockID] : pColor;
            Color32 finalColor = Color32.Lerp(pColor, sColor, blend);

            Vector2 uv0_base = voxelIdToUvMap[pBlockID];
            Vector2 uv1_base = sBlockID > 0 ? voxelIdToUvMap[sBlockID] : uv0_base;

            Vector4 pEmission = voxelIdToEmissionDataMap[pBlockID]; 
            Vector4 sEmission = sBlockID > 0 ? voxelIdToEmissionDataMap[sBlockID] : pEmission;
            Vector4 finalEmission = Vector4.Lerp(pEmission, sEmission, blend);
            
            Vector4 pGapColor = voxelIdToGapColorMap[pBlockID];
            Vector4 sGapColor = sBlockID > 0 ? voxelIdToGapColorMap[sBlockID] : pGapColor;
            Vector4 finalGapColor = Vector4.Lerp(pGapColor, sGapColor, blend);

            Vector2 pMaterialProps = voxelIdToMaterialPropsMap[pBlockID];
            Vector2 sMaterialProps = sBlockID > 0 ? voxelIdToMaterialPropsMap[sBlockID] : pMaterialProps;
            Vector2 finalMaterialProps = Vector2.Lerp(pMaterialProps, sMaterialProps, blend);

            // --- НОВЫЙ КОД: Получаем нормаль и тангент для текущей грани ---
            float3 normal = VoxelData.FaceNormals[(int)direction];
            float4 tangent = VoxelData.FaceTangents[(int)direction];

        for (int i = 0; i < 4; i++)
        {
            vertices.Add(new Vertex
            {
                position = voxelLocalPosition + VoxelData.FaceVertices[(int)direction * 4 + i],
                normal = normal,
                tangent = tangent,
                color = finalColor,
                uv0 = uv0_base + VoxelData.FaceUVs[i] * atlasTileSize,
                uv1 = uv1_base + VoxelData.FaceUVs[i] * atlasTileSize,
                texBlend = blend,
                emissionData = finalEmission,
                gapColor = finalGapColor,
                materialProps = finalMaterialProps
            });
        }

        // ИЗМЕНЕНО: Стандартная триангуляция для квадрата
        triangles.Add(vertexIndex + 0);
        triangles.Add(vertexIndex + 1);
        triangles.Add(vertexIndex + 2);
        triangles.Add(vertexIndex + 0);
        triangles.Add(vertexIndex + 2);
        triangles.Add(vertexIndex + 3);
    }

    private bool IsVoxelTransparent(int x, int y, int z)
    {
        // Проверка по высоте (без изменений)
        if (y < 0 || y >= Chunk.Height) return true;
        
        // Проверка внутри текущего чанка (без изменений)
        if (x >= 0 && x < Chunk.Width && z >= 0 && z < Chunk.Width)
        {
            return primaryBlockIDs[Chunk.GetVoxelIndex(x, y, z)] == 0;
        }

        // --- ИСПРАВЛЕННАЯ, НАДЕЖНАЯ ЛОГИКА ПРОВЕРКИ СОСЕДЕЙ ---
        if (x < 0) {
            // Сначала проверяем, что массив соседа вообще существует и не пустой
            if (!neighborVoxelsNegX.IsCreated || neighborVoxelsNegX.Length == 0) return true;
            return neighborVoxelsNegX[Chunk.GetVoxelIndex(Chunk.Width - 1, y, z)] == 0;
        }
        if (x >= Chunk.Width) {
            if (!neighborVoxelsPosX.IsCreated || neighborVoxelsPosX.Length == 0) return true;
            return neighborVoxelsPosX[Chunk.GetVoxelIndex(0, y, z)] == 0;
        }
        if (z < 0) {
            if (!neighborVoxelsNegZ.IsCreated || neighborVoxelsNegZ.Length == 0) return true;
            return neighborVoxelsNegZ[Chunk.GetVoxelIndex(x, y, Chunk.Width - 1)] == 0;
        }
        if (z >= Chunk.Width) {
            if (!neighborVoxelsPosZ.IsCreated || neighborVoxelsPosZ.Length == 0) return true;
            return neighborVoxelsPosZ[Chunk.GetVoxelIndex(x, y, 0)] == 0;
        }
        
        return true; // Эта строка не должна быть достигнута, но нужна для компилятора
    }
}

// Этот класс тоже без изменений
public static class VoxelData
{
    public static readonly Vector3[] FaceVertices = {
        // Back, Front, Top, Bottom, Left, Right
        new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0),
        new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1),
        new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0),
        new Vector3(0, 0, 1), new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1),
        new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0),
        new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1)
    };
    public static readonly float3[] FaceNormals = {
        new float3(0, 0, -1), // Back
        new float3(0, 0, 1),  // Front
        new float3(0, 1, 0),  // Top
        new float3(0, -1, 0), // Bottom
        new float3(-1, 0, 0), // Left
        new float3(1, 0, 0)   // Right
    };

    public static readonly float4[] FaceTangents = {
        new float4(1, 0, 0, -1), // Back
        new float4(-1, 0, 0, -1),// Front
        new float4(1, 0, 0, 1),  // Top
        new float4(1, 0, 0, -1), // Bottom
        new float4(0, 0, -1, -1),// Left
        new float4(0, 0, 1, -1)  // Right
    };
    public static readonly Vector2[] FaceUVs = {
        new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0)
    };
}