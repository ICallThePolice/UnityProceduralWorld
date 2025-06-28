// --- ФАЙЛ: GenerationStructs.cs ---
// Этот файл - центральная библиотека для всех наших кастомных структур и перечислений.
// Он не вешается ни на какие объекты в сцене. Он просто существует в проекте.

using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics; 

// --- Перечисления (Enums) ---
public enum VoxelCategory { Landscape, Ore, Crystal, Flora, ManMade, Liquid, Special }
public enum TerrainModifier { Additive, Subtractive, Replace }
public enum BiomeInfluenceShape { Radial, OrganicNoise }
public enum Direction { Back, Front, Top, Bottom, Left, Right }
public enum BiomeArtifactType { MiniCrater, FloatingIslet, Pyramid, ConcentricWave }


// --- Структуры для данных ---
// Вспомогательная структура для передачи данных о вокселе по ссылке
public struct VoxelStateData
{
    public int terrainHeight;
    public ushort voxelID;
}

// --- НОВЫЙ ИНТЕРФЕЙС ("КОНТРАКТ") ---
public interface IBiomeTerrainGenerator
{
    int GetModifiedHeight(in BiomeInstanceBurst biome, int baseTerrainHeight, float dominantBiomeBaseHeight, in Vector2 worldPos, FastNoiseLite heightMapNoise);
}
public interface IArtifactGenerator
{
    // Каждый генератор артефактов должен уметь применять себя к данным вокселя
    void Apply(ref VoxelStateData voxelData, in ArtifactInstanceBurst artifact, in Vector2 worldPos, int y, int baseTerrainHeight);
}

public struct Vertex
{
    public float3 position;
    public Color32 color;
    public float3 normal; 
    public float2 uv0;
    public float2 uv1;
    public float  texBlend;
    public float4 emissionData;
    public float4 gapColor;
    public float2 materialProps;
    public float  gapWidth;
    public float3 bevelData;
}


public class ArtifactInstance
{
    public Vector2 position;
    public BiomeArtifactSO settings;
    public Vector2 calculatedSize;
    public float calculatedHeight;
    public float yOffset;
    public float groundHeight;
    public ushort mainVoxelID;
    public int tiers;
}

public class BiomeInstance
{
    public Vector2 position;
    public BiomePlacementSettingsSO settings;
    // Динамически рассчитанные параметры
    public float calculatedRadius;
    public float calculatedAggressiveness;
    public int calculatedTiers;
    public float calculatedContrast;
    public float coreRadiusPercentage;
    public float sharpness;  
    public bool isInverted;
    public float biomeHighestPoint;
    public Vector3 calculatedTierRadii;
    public List<ArtifactInstance> childArtifacts = new List<ArtifactInstance>(); 
}

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

public static class VoxelData
{
    // Позиции вершин для 6 граней вокселя (4 вертекса на грань)
    public static readonly float3[] FaceVertices = {
        // Back, Front, Top, Bottom, Left, Right
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

    public static readonly float4[] FaceTangents = {
        new float4(1, 0, 0, -1), new float4(-1, 0, 0, -1), new float4(1, 0, 0, 1),
        new float4(1, 0, 0, -1), new float4(0, 0, -1, -1), new float4(0, 0, 1, -1)
    };
    
    public static readonly float2[] FaceUVs = {
        new float2(0, 0), new float2(0, 1), new float2(1, 1), new float2(1, 0)
    };
}

/// <summary>
/// Burst-совместимая структура для передачи информации о размещении оверлея в мире.
/// </summary>
[System.Serializable]
public struct OverlayPlacementDataBurst
{
    public ushort overlayID; // ID оверлея, который нужно применить
    public float2 position;  // Центр влияния
    public float radius;     // Радиус влияния
    public float blendSharpness; // Резкость краев влияния (0-1)
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

public enum ChildPlacementMode 
{
    Circular,           // Равномерно по кругу с фактором хаоса
    StrictlyAbove,      // Строго над центром родителя
    StrictlyBelow,      // Строго под центром родителя
    RandomOffset        // Случайное смещение в пределах радиуса (старая логика)
}

[System.Serializable]
public class ChildArtifactPlacement
{
    [Tooltip("Какой дочерний артефакт создавать.")]
    public BiomeArtifactSO artifactSO;
    
    [Tooltip("(Опционально) Если указано, дочерний артефакт будет построен из этого вокселя, а не из материала родительского биома.")]
    public VoxelTypeSO overrideVoxel;

    [Tooltip("Количество, которое нужно попытаться создать.")]
    public int spawnCount = 1;

    [Tooltip("Режим размещения дочерних элементов.")]
    public ChildPlacementMode placementMode = ChildPlacementMode.Circular;

    [Tooltip("Радиус размещения дочерних элементов, ОТНОСИТЕЛЬНО радиуса родителя. 1.0 = на самом краю.")]
    [Range(0f, 2f)] public float relativePlacementRadius = 0.8f;
    
    [Tooltip("Фактор хаоса (0 до 1). Влияет на угол, дистанцию и высоту размещения.")]
    [Range(0f, 1f)] public float placementChaos = 0.1f;

    [Tooltip("Вертикальное смещение относительно центра родителя.")]
    public float yOffset = 0;
}

/// <summary>
/// Перечисление всех возможных стратегий размещения артефактов.
/// Позволит нам выбирать их в инспекторе.
/// </summary>
public enum ArtifactPlacementStrategyType
{
    OnTieredSlopes,
    OuterRingWithHeightCheck,
    Central          // Размещение в центре (для будущих идей)
}

/// <summary>
/// Новый класс для настройки "корневых" артефактов, которые размещаются относительно биома.
/// </summary>
[System.Serializable]
public class RootArtifactConfig
{
    [Tooltip("Какой артефакт размещать.")]
    public BiomeArtifactSO artifactSO;
    
    [Tooltip("Какую стратегию использовать для его размещения.")]
    public ArtifactPlacementStrategyType placementStrategy;
}

/// <summary>
/// "Контракт", которому должна следовать каждая стратегия размещения.
/// </summary>
public interface IArtifactPlacementStrategy
{
    void Place(BiomeInstance parentBiome, RootArtifactConfig artifactConfig, System.Random random, List<ArtifactInstance> artifactListToFill, List<BiomeInstance> allBiomesInArea, BiomeManager manager);
}