// Файл: BiomePlacementSettingsSO.cs
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewBiomePlacement", menuName = "Procedural Voxel World/Biome Placement Settings")]
public class BiomePlacementSettingsSO : ScriptableObject
{
    [Header("Идентификация")]
    public BiomeDefinitionSO biome;

    [Header("Параметры размещения")]
    public float minDistanceBetweenBiomes = 500f;
    public float neutralZoneRadius = 500f;
    
    [Header("Динамические параметры (Min/Max)")]
    [Tooltip("Как резко биом переходит в нейтральную зону. >1 - резче, <1 - плавнее.")]
    public Vector2 contrast = new Vector2(1.0f, 3.0f);
    [Tooltip("Диапазон радиуса влияния 'сердца' биома.")]
    public Vector2 influenceRadius = new Vector2(200f, 400f);
    [Tooltip("Диапазон 'агрессивности' биома.")]
    public Vector2 aggressiveness = new Vector2(0.2f, 0.8f);
    [Tooltip("Диапазон макс. количества ярусов.")]
    public Vector2Int maxTiers = new Vector2Int(1, 3);
    [Tooltip("Шанс (0 до 1), что Психик-биом будет инвертирован (карьер на горе или пирамида в низине).")]
    [Range(0f, 1f)] public float inversionChance = 0.25f;
    [Header("Настройки многоярусных биомов")]
    [Tooltip("Относительные радиусы для каждого яруса (x=ярус3, y=ярус2, z=ярус1). Значения от 0 до 1.")]
    public Vector3 tierPlacementRadii = new Vector3(0.4f, 0.7f, 1.0f);

    [Header("Корневые артефакты (размещаются относительно биома)")]
    public List<RootArtifactConfig> rootArtifacts;
}