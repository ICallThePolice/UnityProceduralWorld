// --- ФАЙЛ: MeshingJob.cs (ИСПРАВЛЕННАЯ И ОПТИМИЗИРОВАННАЯ ВЕРСИЯ) ---
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;

[BurstCompile]
public struct MeshingJob : IJob
{
    // --- Входные данные о геометрии и соседях ---
    [ReadOnly] public NativeArray<ushort> primaryBlockIDs;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsPosX;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsNegX;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsPosZ;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsNegZ;

    // --- Входные данные: ЗАРАНЕЕ ВЫЧИСЛЕННЫЕ свойства для каждого вокселя ---
    [ReadOnly] public NativeArray<Color32> finalColors;
    [ReadOnly] public NativeArray<float2> finalUv0s;
    [ReadOnly] public NativeArray<float2> finalUv1s;
    [ReadOnly] public NativeArray<float> finalTexBlends;
    [ReadOnly] public NativeArray<float4> finalEmissionData;
    [ReadOnly] public NativeArray<float4> finalGapColors;
    [ReadOnly] public NativeArray<float2> finalMaterialProps;
    [ReadOnly] public NativeArray<float> finalGapWidths;
    [ReadOnly] public NativeArray<float3> finalBevelData;

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
            if (primaryBlockIDs[voxelIndex] == 0) continue;

            var position = new Vector3(x, y, z);
            if (IsVoxelTransparent(x, y + 1, z)) CreateFace(Direction.Top,    position, voxelIndex);
            if (IsVoxelTransparent(x, y - 1, z)) CreateFace(Direction.Bottom, position, voxelIndex);
            if (IsVoxelTransparent(x + 1, y, z)) CreateFace(Direction.Right,  position, voxelIndex);
            if (IsVoxelTransparent(x - 1, y, z)) CreateFace(Direction.Left,   position, voxelIndex);
            if (IsVoxelTransparent(x, y, z + 1)) CreateFace(Direction.Front,  position, voxelIndex);
            if (IsVoxelTransparent(x, y, z - 1)) CreateFace(Direction.Back,   position, voxelIndex);
        }
    }

    private void CreateFace(Direction direction, Vector3 voxelLocalPosition, int voxelIndex)
    {
        int vertexIndex = vertices.Length;
        int dirIndex = (int)direction;

        // --- ИЗМЕНЕНИЕ ---
        // Просто создаем 4 вертекса в цикле, присваивая им нужные значения.
        // Это эффективнее, чем создавать "шаблон" и копировать его.
        for (int i = 0; i < 4; i++)
        {
            // --- ИСПРАВЛЕНИЕ ОШИБКИ ТИПОВ ---
            // Явно приводим UnityEngine.Vector2 к Unity.Mathematics.float2 перед сложением.
            float2 uv0 = finalUv0s[voxelIndex] + (float2)VoxelData.FaceUVs[i];
            float2 uv1 = finalUv1s[voxelIndex] + (float2)VoxelData.FaceUVs[i];
            
            vertices.Add(new Vertex
            {
                position = voxelLocalPosition + VoxelData.FaceVertices[dirIndex * 4 + i],
                normal = VoxelData.FaceNormals[dirIndex],
                tangent = VoxelData.FaceTangents[dirIndex],
                
                // Присваиваем заранее вычисленные данные
                color = finalColors[voxelIndex],
                texBlend = finalTexBlends[voxelIndex],
                emissionData = finalEmissionData[voxelIndex],
                gapColor = finalGapColors[voxelIndex],
                materialProps = finalMaterialProps[voxelIndex],
                gapWidth = finalGapWidths[voxelIndex],
                bevelData = finalBevelData[voxelIndex],
                
                // Присваиваем UV с учетом смещения для атласа
                uv0 = uv0,
                uv1 = uv1
            });
        }

        triangles.Add(vertexIndex + 0);
        triangles.Add(vertexIndex + 1);
        triangles.Add(vertexIndex + 2);
        triangles.Add(vertexIndex + 0);
        triangles.Add(vertexIndex + 2);
        triangles.Add(vertexIndex + 3);
    }

    private bool IsVoxelTransparent(int x, int y, int z)
    {
        if (y < 0 || y >= Chunk.Height) return true;
        
        if (x >= 0 && x < Chunk.Width && z >= 0 && z < Chunk.Width)
        {
            return primaryBlockIDs[Chunk.GetVoxelIndex(x, y, z)] == 0;
        }

        if (x < 0) {
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
        
        return true; 
    }
}


/// <summary>
/// Статический класс-помощник, хранящий данные о геометрии граней вокселя.
/// Эти данные постоянны и могут быть использованы в Burst-компилируемом коде.
/// </summary>
public static class VoxelData
{
    // Позиции вершин для 6 граней вокселя (4 вертекса на грань)
    public static readonly Vector3[] FaceVertices = {
        // Back, Front, Top, Bottom, Left, Right
        // Индексы 0-3: Back (-Z)
        new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0),
        // Индексы 4-7: Front (+Z)
        new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1),
        // Индексы 8-11: Top (+Y)
        new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0),
        // Индексы 12-15: Bottom (-Y)
        new Vector3(0, 0, 1), new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1),
        // Индексы 16-19: Left (-X)
        new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0),
        // Индексы 20-23: Right (+X)
        new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1)
    };
    
    // Нормали для 6 граней
    public static readonly float3[] FaceNormals = {
        new float3(0, 0, -1), // Back
        new float3(0, 0, 1),  // Front
        new float3(0, 1, 0),  // Top
        new float3(0, -1, 0), // Bottom
        new float3(-1, 0, 0), // Left
        new float3(1, 0, 0)   // Right
    };

    // Тангенты для 6 граней
    public static readonly float4[] FaceTangents = {
        new float4(1, 0, 0, -1), // Back
        new float4(-1, 0, 0, -1),// Front
        new float4(1, 0, 0, 1),  // Top
        new float4(1, 0, 0, -1), // Bottom
        new float4(0, 0, -1, -1),// Left
        new float4(0, 0, 1, -1)  // Right
    };
    
    // Базовые UV координаты для одной грани (квадрата)
    public static readonly Vector2[] FaceUVs = {
        new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0)
    };
}