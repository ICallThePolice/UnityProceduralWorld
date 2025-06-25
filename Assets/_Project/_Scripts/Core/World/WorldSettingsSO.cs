// Файл: WorldSettingsSO.cs
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "WorldSettings", menuName = "Procedural Voxel World/World Settings")]
public class WorldSettingsSO : ScriptableObject
{
    [Header("Настройки мира")]
    public Material worldMaterial;
    [Range(2, 16)] public int renderDistance = 4;
    [Tooltip("Как часто (в секундах) обновлять активность чанков и загружать новые.")]
    public float chunkUpdateInterval = 0.5f;

    [Header("Настройки производительности конвейера")]
    [Tooltip("Макс. количество задач по генерации ДАННЫХ, запускаемых за один кадр.")]
    [Range(1, 8)]
    public int maxDataJobsPerFrame = 2;

    [Tooltip("Макс. количество задач по генерации МЕША, запускаемых за один кадр.")]
    [Range(1, 8)]
    public int maxMeshJobsPerFrame = 2;

    [Header("Настройки очистки (Keep-Alive)")]
    public float chunkLingerTime = 5f;
    public float cleanupCheckInterval = 5f;
    public int chunksPerCleanupFrame = 30;

    [Header("Ассеты данных")]
    public BiomeDefinitionSO neutralBiome;
    public List<VoxelTypeSO> voxelTypes;

    [Header("Настройки генерации")]
    public NoiseSettingsSO heightmapNoiseSettings;
    public NoiseSettingsSO chaosNoiseSettings;
    public NoiseSettingsSO saturationNoiseSettings;
}