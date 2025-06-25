// Файл: BiomeDefinitionSO.cs

using UnityEngine;

[CreateAssetMenu(fileName = "BiomeDefinition", menuName = "Procedural Voxel World/Biome Definition")]
public class BiomeDefinitionSO : ScriptableObject
{
    [Header("Метаданные Биома")]
    [Tooltip("Уникальный ID для этого биома. Должен быть больше 0.")]
    public ushort biomeID;

    [Header("Состав Вокселей Биома")]
    [Tooltip("Воксель для самого верхнего слоя земли (трава, песок и т.д.).")]
    public VoxelTypeSO SurfaceVoxel;
    [Tooltip("Воксель, находящийся сразу под поверхностным слоем (земля, глина).")]
    public VoxelTypeSO SubSurfaceVoxel;
    [Tooltip("Основной воксель биома, составляющий его массу (камень).")]
    public VoxelTypeSO MainVoxel;
    [Tooltip("Глубина подповерхностного слоя в блоках.")]
    [Range(1, 10)]
    public int SubSurfaceDepth = 3;

    // --- НОВЫЕ ПОЛЯ ---
    [Header("Модификация Ландшафта")]
    [Tooltip("Как этот биом влияет на высоту базового ландшафта?\nAdditive: добавляет высоту (горы).\nSubtractive: вычитает высоту (кратеры).\nReplace: не меняет базовую высоту.")]
    public TerrainModifier terrainModificationType = TerrainModifier.Replace;
    
    [Tooltip("Максимальная сила вертикального смещения в блоках (для холмов или кратеров).")]
    public float verticalDisplacementScale = 20f;
}