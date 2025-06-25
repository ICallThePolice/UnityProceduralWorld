// --- ФАЙЛ: GenerationStructs.cs ---
// Этот файл - центральная библиотека для всех наших кастомных структур и перечислений.
// Он не вешается ни на какие объекты в сцене. Он просто существует в проекте.

using UnityEngine;
using System.Collections.Generic;

// --- Перечисления (Enums) ---
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

[System.Serializable]
public struct Vertex
{
    public Vector3 position;
    public Vector2 uv;
    public Vertex(Vector3 pos, Vector2 uv)
    {
        this.position = pos;
        this.uv = uv;
    }
}

public class ArtifactInstance
{
    public Vector2 position;
    public BiomeArtifactSO settings;
    public Vector2 calculatedSize;
    public float calculatedHeight;
    public float yOffset;
    public float groundHeight;
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
    public bool isInverted;
    public float biomeHighestPoint;
    public Vector3 calculatedTierRadii;
    public List<ArtifactInstance> childArtifacts = new List<ArtifactInstance>(); 
}

[System.Serializable]
public struct BiomeInstanceBurst
{
    public Vector2 position;
    public float influenceRadius;
    public float aggressiveness;
    public int tiers;
    public float contrast;
    public bool isInverted;
    public float biomeHighestPoint;
    public ushort surfaceVoxelID;
    public ushort subSurfaceVoxelID;
    public ushort mainVoxelID;
    public int subSurfaceDepth;
    public TerrainModifier terrainModificationType;
    public float verticalDisplacementScale;
    public Vector3 tierRadii;
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

[System.Serializable]
public class ChildArtifactPlacement
{
    [Tooltip("Какой дочерний артефакт создавать.")]
    public BiomeArtifactSO artifactSO;

    [Tooltip("Количество, которое нужно попытаться создать.")]
    public int spawnCount = 1;

    [Tooltip("Вертикальное смещение относительно центра родителя.")]
    public float yOffset = 0;

    [Tooltip("Горизонтальное смещение от центра родителя. 0 = точно в центре.")]
    public float horizontalOffset = 0;

    [Tooltip("Разрешить случайный угол для горизонтального смещения? Если false, смещение будет по одной оси.")]
    public bool useRandomAngle = true;
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