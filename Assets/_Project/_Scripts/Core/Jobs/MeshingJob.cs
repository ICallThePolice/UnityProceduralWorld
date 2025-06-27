// --- ФАЙЛ: MeshingJob.cs (ФИНАЛЬНАЯ ВЕРСИЯ С 8-ВОКСЕЛЬНЫМ СМЕШИВАНИЕМ) ---
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;

[BurstCompile]
public struct MeshingJob : IJob
{
    // --- Входные данные: Карты свойств и ID ---
    [ReadOnly] public float2 atlasTileSize;
    
    [ReadOnly] public NativeArray<ushort> primaryBlockIDs;
    [ReadOnly] public NativeArray<ushort> overlayIDs;
    
    [ReadOnly] public NativeArray<VoxelTypeDataBurst> voxelTypeMap;
    [ReadOnly] public NativeArray<VoxelOverlayDataBurst> voxelOverlayMap;
    
    // Данные о соседях (только ID)
    [ReadOnly] public NativeArray<ushort> neighborVoxelsPosX;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsNegX;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsPosY;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsNegY;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsPosZ;
    [ReadOnly] public NativeArray<ushort> neighborVoxelsNegZ;
    
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

            if (IsVoxelTransparent(x, y + 1, z)) CreateFace(new int3(x, y, z), Direction.Top);
            if (IsVoxelTransparent(x, y - 1, z)) CreateFace(new int3(x, y, z), Direction.Bottom);
            if (IsVoxelTransparent(x + 1, y, z)) CreateFace(new int3(x, y, z), Direction.Right);
            if (IsVoxelTransparent(x - 1, y, z)) CreateFace(new int3(x, y, z), Direction.Left);
            if (IsVoxelTransparent(x, y, z + 1)) CreateFace(new int3(x, y, z), Direction.Front);
            if (IsVoxelTransparent(x, y, z - 1)) CreateFace(new int3(x, y, z), Direction.Back);
        }
    }

    private void CreateFace(int3 localPos, Direction direction)
    {
        int vertexIndexOffset = vertices.Length;
        int dirIndex = (int)direction;
        
        float3 normal = VoxelData.FaceNormals[dirIndex];
        float4 tangent = VoxelData.FaceTangents[dirIndex];

        for (int i = 0; i < 4; i++)
        {
            float3 vertexPos = localPos + VoxelData.FaceVertices[dirIndex * 4 + i];
            // Передаем также UV смещение для конкретной вершины
            Vertex vertex = GetBlendedVertexData(vertexPos, normal, tangent, VoxelData.FaceUVs[i]);
            vertices.Add(vertex);
        }

        triangles.Add(vertexIndexOffset);
        triangles.Add(vertexIndexOffset + 1);
        triangles.Add(vertexIndexOffset + 2);
        triangles.Add(vertexIndexOffset);
        triangles.Add(vertexIndexOffset + 2);
        triangles.Add(vertexIndexOffset + 3);
    }

    private Vertex GetBlendedVertexData(float3 vertexPos, float3 normal, float4 tangent, float2 faceUV)
    {
        int3[] cornerPositions = new int3[8];
        GetCornerVoxelPositions(vertexPos, cornerPositions);

        VoxelTypeDataBurst[] cornerTypes = new VoxelTypeDataBurst[8];
        VoxelOverlayDataBurst[] cornerOverlays = new VoxelOverlayDataBurst[8];
        for(int i = 0; i < 8; i++)
        {
            GetVoxelPropertiesAt(cornerPositions[i], out cornerTypes[i], out cornerOverlays[i]);
        }

        float3 weights = math.frac(vertexPos);

        VoxelTypeDataBurst blendedLandscape = TrilinearInterpolate(cornerTypes, weights);
        VoxelOverlayDataBurst blendedOverlay = TrilinearInterpolate(cornerOverlays, weights);

        float overlayBlend = blendedOverlay.priority > 0 ? 1f : 0f;

        Color32 finalColor = Color32.Lerp(blendedLandscape.baseColor, blendedOverlay.tintColor, overlayBlend);
        float2 uv0 = (blendedLandscape.baseUV + faceUV) / atlasTileSize;
        float2 uv1 = blendedOverlay.id > 0 ? ((blendedOverlay.overlayUV + faceUV) / atlasTileSize) : float2.zero;
        float gapWidth = math.lerp(blendedLandscape.gapWidth, blendedOverlay.gapWidth, overlayBlend);
        Color32 gapColor = Color32.Lerp(blendedLandscape.gapColor, blendedOverlay.gapColor, overlayBlend);

        return new Vertex
        {
            position = vertexPos,
            normal = normal, tangent = tangent, color = finalColor,
            uv0 = uv0, uv1 = uv1, texBlend = overlayBlend,
            gapWidth = gapWidth, gapColor = new float4(gapColor.r / 255f, gapColor.g / 255f, gapColor.b / 255f, gapColor.a / 255f),
            emissionData = blendedOverlay.id > 0 ? blendedOverlay.emissionData : float4.zero,
            materialProps = blendedOverlay.id > 0 ? blendedOverlay.materialProps : new float2(0.1f, 0f),
            bevelData = blendedOverlay.id > 0 ? blendedOverlay.bevelData : new float3(0.1f, 0.8f, -1f)
        };
    }

    private void GetCornerVoxelPositions(float3 vertexPos, int3[] positions)
    {
        int3 basePos = (int3)math.floor(vertexPos);
        positions[0] = new int3(basePos.x - 1, basePos.y - 1, basePos.z - 1);
        positions[1] = new int3(basePos.x,     basePos.y - 1, basePos.z - 1);
        positions[2] = new int3(basePos.x - 1, basePos.y,     basePos.z - 1);
        positions[3] = new int3(basePos.x,     basePos.y,     basePos.z - 1);
        positions[4] = new int3(basePos.x - 1, basePos.y - 1, basePos.z);
        positions[5] = new int3(basePos.x,     basePos.y - 1, basePos.z);
        positions[6] = new int3(basePos.x - 1, basePos.y,     basePos.z);
        positions[7] = new int3(basePos.x,     basePos.y,     basePos.z);
    }
    
    private void GetVoxelPropertiesAt(int3 pos, out VoxelTypeDataBurst typeData, out VoxelOverlayDataBurst overlayData)
    {
        ushort typeId = GetVoxelID(pos);
        ushort overlayId = GetOverlayID(pos); // Для оверлеев пока не будем проверять соседей
        
        typeData = (typeId > 0 && typeId < voxelTypeMap.Length) ? voxelTypeMap[typeId] : default;
        overlayData = (overlayId > 0 && overlayId < voxelOverlayMap.Length) ? voxelOverlayMap[overlayId] : default;
    }

    // --- ВОССТАНОВЛЕННЫЕ МЕТОДЫ ---
    private VoxelTypeDataBurst TrilinearInterpolate(VoxelTypeDataBurst[] p, float3 t)
    {
        VoxelTypeDataBurst c00 = Lerp(p[0], p[1], t.x);
        VoxelTypeDataBurst c01 = Lerp(p[4], p[5], t.x);
        VoxelTypeDataBurst c10 = Lerp(p[2], p[3], t.x);
        VoxelTypeDataBurst c11 = Lerp(p[6], p[7], t.x);
        VoxelTypeDataBurst c0 = Lerp(c00, c01, t.z);
        VoxelTypeDataBurst c1 = Lerp(c10, c11, t.z);
        return Lerp(c0, c1, t.y);
    }
    
    private VoxelOverlayDataBurst TrilinearInterpolate(VoxelOverlayDataBurst[] p, float3 t)
    {
        float priority = 0;
        VoxelOverlayDataBurst dominant = default;
        for(int i = 0; i < 8; ++i)
        {
            if (p[i].priority > priority)
            {
                priority = p[i].priority;
                dominant = p[i];
            }
        }
        return dominant;
    }

    private VoxelTypeDataBurst Lerp(VoxelTypeDataBurst a, VoxelTypeDataBurst b, float t)
    {
        if (t <= 0.001f) return a;
        if (t >= 0.999f) return b;
        return new VoxelTypeDataBurst {
            id = a.id,
            baseColor = Color32.Lerp(a.baseColor, b.baseColor, t),
            baseUV = math.lerp(a.baseUV, b.baseUV, t),
            gapWidth = math.lerp(a.gapWidth, b.gapWidth, t),
            gapColor = Color32.Lerp(a.gapColor, b.gapColor, t)
        };
    }

    private ushort GetVoxelID(int3 pos)
    {
        if (pos.y < 0 || pos.y >= Chunk.Height) return 0;
        
        // Внутри текущего чанка
        if (pos.x >= 0 && pos.x < Chunk.Width && pos.z >= 0 && pos.z < Chunk.Width)
            return primaryBlockIDs[Chunk.GetVoxelIndex(pos.x, pos.y, pos.z)];
        
        // Соседние чанки
        if (pos.x < 0) return neighborVoxelsNegX.IsCreated ? neighborVoxelsNegX[Chunk.GetVoxelIndex(Chunk.Width + pos.x, pos.y, pos.z)] : (ushort)0;
        if (pos.x >= Chunk.Width) return neighborVoxelsPosX.IsCreated ? neighborVoxelsPosX[Chunk.GetVoxelIndex(pos.x - Chunk.Width, pos.y, pos.z)] : (ushort)0;
        if (pos.z < 0) return neighborVoxelsNegZ.IsCreated ? neighborVoxelsNegZ[Chunk.GetVoxelIndex(pos.x, pos.y, Chunk.Width + pos.z)] : (ushort)0;
        if (pos.z >= Chunk.Width) return neighborVoxelsPosZ.IsCreated ? neighborVoxelsPosZ[Chunk.GetVoxelIndex(pos.x, pos.y, pos.z - Chunk.Width)] : (ushort)0;
        
        return 0; // На случай если что-то пошло не так
    }
    
    private ushort GetOverlayID(int3 pos)
    {
        // Для оверлеев пока не будем усложнять и проверять соседей
        if (pos.x >= 0 && pos.x < Chunk.Width && pos.y >= 0 && pos.y < Chunk.Height && pos.z >= 0 && pos.z < Chunk.Width)
        {
            return overlayIDs[Chunk.GetVoxelIndex(pos.x, pos.y, pos.z)];
        }
        return 0;
    }

    private bool IsVoxelTransparent(int x, int y, int z)
    {
        if (y < 0 || y >= Chunk.Height) return true;
        
        ushort id = GetVoxelID(new int3(x, y, z));
        return (id == 0 || (id > 0 && id < voxelTypeMap.Length && !voxelTypeMap[id].isSolid));
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