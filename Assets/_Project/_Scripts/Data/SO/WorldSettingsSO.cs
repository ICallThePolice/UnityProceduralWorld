// --- ФАЙЛ: WorldSettingsSO.cs ---
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "WorldSettings", menuName = "Procedural Voxel World/World Settings")]
public class WorldSettingsSO : ScriptableObject
{
    [Header("Настройки мира")]
    public Material worldMaterial;
    [Range(2, 16)] public int renderDistance = 8;
    public float chunkUpdateInterval = 0.25f;

    [Header("GPU Ассеты")]
    public ComputeShader biomeMapComputeShader;

    [Header("Настройки производительности")]
    [Range(1, 8)] public int maxDataJobsPerFrame = 4;
    [Range(1, 8)] public int maxMeshJobsPerFrame = 4;

    [Header("Настройки очистки (Keep-Alive)")]
    public float chunkLingerTime = 5f;

    [Header("Настройки Атласа Текстур")]
    public Texture2D worldTextureAtlas;
    public Vector2Int atlasSizeInTiles = new Vector2Int(2, 2);

    [Header("Ассеты данных")]
    public VoxelTypeSO globalBiomeBlock;
    public List<VoxelTypeSO> voxelTypes;
    public List<VoxelOverlaySO> voxelOverlays;

    [Header("Настройки генерации (шум)")]
    public NoiseSettingsSO heightmapNoiseSettings;
    public NoiseSettingsSO chaosNoiseSettings;
    public NoiseSettingsSO saturationNoiseSettings;
    public NoiseSettingsSO detailNoiseSettings;

    // --- НОВЫЕ ПОЛЯ ДЛЯ УПРАВЛЕНИЯ ГЕНЕРАЦИЕЙ ---
    [Header("Динамическая сложность мира")]
    [Tooltip("Максимальная дистанция от центра (0,0), на которой мир достигает максимальной сложности.")]
    public float maxComplexityDistance = 10000f;
    [Tooltip("Множитель частоты шума в центре мира (легкий мир).")]
    public float easyFrequencyMultiplier = 1.0f;
    [Tooltip("Множитель частоты шума на максимальной дистанции (сложный мир).")]
    public float hardFrequencyMultiplier = 2.5f;

    [Header("Настройки границ биомов")]
    [Tooltip("Ширина переходной/нейтральной зоны между биомами. 0.1 = узкая, 0.4 = очень широкая.")]
    [Range(0, 0.5f)]
    public float transitionWidth = 0.15f;
    [Header("Настройки границ биомов")]
    [Tooltip("Порог шума для создания нейтральных зон. Чем выше значение, тем ТОНЬШЕ будут нейтральные зоны. (Диапазон 0.0 - 1.0)")]
    [Range(0.0f, 1.0f)]
    public float erosionThreshold = 0.6f;

    [Tooltip("Масштаб шума, который используется для создания нейтральных зон.")]
    public float erosionNoiseScale = 0.01f;
    [Header("Фильтрация биомов")]
    [Tooltip("Радиус в ячейках для анализа окружения. Большее значение лучше убирает 'шум', но может сгладить границы.")]
    [Range(1, 5)]
    public int filteringRadius = 2;

    [Tooltip("Процент соседей, которые должны быть того же типа, чтобы ячейка 'выжила'. Меньше значение = биомы более устойчивы к удалению.")]
    [Range(0.0f, 1.0f)]
    public float requiredNeighborPercentage = 0.4f;
}