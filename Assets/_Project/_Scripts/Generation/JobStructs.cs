// Файл: JobStructs.cs
// Описание: Содержит все Burst-совместимые структуры данных,
// используемые в Job System для генерации и мешинга.

using Unity.Mathematics;
using UnityEngine;

// Структура вершины для меша
public struct Vertex
{
    public float3 position;
    public float3 normal;
    public Color32 color;
    public float2 uv0;
    public float2 uv1;
    public float  texBlend;
    public float4 emissionData;
    public float4 gapColor;
    public float2 materialProps;
    public float  gapWidth;
    public float3 bevelData;
}

// Статические данные для построения граней вокселя
public static class VoxelData
{
    public static readonly float3[] FaceVertices = {
        new float3(0, 0, 0), new float3(0, 1, 0), new float3(1, 1, 0), new float3(1, 0, 0),
        new float3(1, 0, 1), new float3(1, 1, 1), new float3(0, 1, 1), new float3(0, 0, 1),
        new float3(0, 1, 0), new float3(0, 1, 1), new float3(1, 1, 1), new float3(1, 1, 0),
        new float3(0, 0, 1), new float3(0, 0, 0), new float3(1, 0, 0), new float3(1, 0, 1),
        new float3(0, 0, 1), new float3(0, 1, 1), new float3(0, 1, 0), new float3(0, 0, 0),
        new float3(1, 0, 0), new float3(1, 1, 0), new float3(1, 1, 1), new float3(1, 0, 1)
    };
    
    public static readonly float3[] FaceNormals = {
        new float3(0, 0, -1), new float3(0, 0, 1), new float3(0, 1, 0),
        new float3(0, -1, 0), new float3(-1, 0, 0), new float3(1, 0, 0)
    };
    
    public static readonly float2[] FaceUVs = {
        new float2(0, 0), new float2(0, 1), new float2(1, 1), new float2(1, 0)
    };
}

// --- Структуры для Biome Generation Job ---

public struct ClusterInfoBurst
{
    public ushort blockID;
    public float influenceRadius;
    public float contrast;
    public float coreRadiusPercentage;
    public int nodeStartIndex;
    public int nodeCount;
    public int edgeStartIndex;
    public int edgeCount;
}

public struct EdgeInfoBurst
{
    public int nodeA_idx;
    public int nodeB_idx;
}

public struct BiomeInfluence
{
    public ushort blockID;
    public float influence;
}

// --- Структуры для Voxel Properties ---

[System.Serializable]
public struct VoxelTypeDataBurst
{
    public ushort id;
    public bool isSolid;
    public Color32 baseColor;
    public float2 baseUV;
    public float gapWidth;
    public Color32 gapColor;
    public float3 bevelData;
}

[System.Serializable]
public struct VoxelOverlayDataBurst
{
    public ushort id;
    public int priority;
    public Color32 tintColor;
    public float2 overlayUV;
    public float gapWidth;
    public Color32 gapColor;
    public float2 materialProps;
    public float4 emissionData;
    public float3 bevelData;
}

// --- Структуры для передачи данных о сущностях в Job'ы ---

[System.Serializable]
public struct OverlayPlacementDataBurst
{
    public ushort overlayID;
    public float2 position;
    public float radius;
    public float blendSharpness;
}

[System.Serializable]
public struct BiomeInstanceBurst
{
    public ushort biomeID;
    public Vector2 position;
    public float coreRadiusPercentage;
    public float sharpness;  
    public float influenceRadius;
    public float contrast;
    public ushort blockID;
}

[System.Serializable]
public struct ArtifactInstanceBurst
{
    public Vector2 position;
    public BiomeArtifactType artifactType;
    public Vector2 size;
    public float height;
    public float yOffset;
    public float groundHeight;
    public ushort mainVoxelID;
    public int tiers;
}

[System.Serializable]
public struct BiomeMappingBurst
{
    public ushort biomeID;
    public Vector2 position;
    public ushort surfaceVoxelID;
    public ushort subSurfaceVoxelID;
    public ushort mainVoxelID;
    public int subSurfaceDepth;
}