// Файл: GenerationStructs.cs
// Описание: Содержит основные перечисления (enum) и классы для хранения
// данных о генерации в рантайме (не для Jobs).

using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;

// --- Перечисления (Enums) ---
public enum VoxelCategory { Landscape, Ore, Crystal, Flora, ManMade, Liquid, Special }
public enum TerrainModifier { Additive, Subtractive, Replace }
public enum BiomeInfluenceShape { Radial, OrganicNoise }
public enum Direction { Back, Front, Top, Bottom, Left, Right }
public enum BiomeArtifactType { MiniCrater, FloatingIslet, Pyramid, ConcentricWave }
public enum ChildPlacementMode { Circular, StrictlyAbove, StrictlyBelow, RandomOffset }
public enum ArtifactPlacementStrategyType { OnTieredSlopes, OuterRingWithHeightCheck, Central }

// --- Runtime-классы и структуры ---

// Вспомогательная структура для передачи данных о вокселе по ссылке
public struct VoxelStateData
{
    public int terrainHeight;
    public ushort voxelID;
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
    public float2 position;
    public BiomePlacementSettingsSO settings;
    // Динамически рассчитанные параметры
    public float calculatedRadius;
    public float calculatedContrast;
    public float coreRadiusPercentage;
    public float sharpness;
    public float calculatedAggressiveness;
    public int calculatedTiers;
    public bool isInverted;
    public float biomeHighestPoint;
    public Vector3 calculatedTierRadii;
    public List<ArtifactInstance> childArtifacts = new List<ArtifactInstance>();
}

[System.Serializable]
public class ChildArtifactPlacement
{
    public BiomeArtifactSO artifactSO;
    public VoxelTypeSO overrideVoxel;
    public int spawnCount = 1;
    public ChildPlacementMode placementMode = ChildPlacementMode.Circular;
    [Range(0f, 2f)] public float relativePlacementRadius = 0.8f;
    [Range(0f, 1f)] public float placementChaos = 0.1f;
    public float yOffset = 0;
}

[System.Serializable]
public class RootArtifactConfig
{
    public BiomeArtifactSO artifactSO;
    public ArtifactPlacementStrategyType placementStrategy;
}