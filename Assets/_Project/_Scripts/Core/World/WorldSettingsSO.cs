using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "WorldSettings", menuName = "Procedural Voxel World/World Settings")]
public class WorldSettingsSO : ScriptableObject
{
    [Header("Настройки мира")]
    public Material worldMaterial;
    [Range(2, 16)] public int renderDistance = 8;
    public float chunkUpdateInterval = 0.25f;

    [Header("Настройки производительности")]
    [Range(1, 8)] public int maxDataJobsPerFrame = 4;
    [Range(1, 8)] public int maxMeshJobsPerFrame = 4;
    
    [Header("Настройки очистки (Keep-Alive)")]
    public float chunkLingerTime = 5f;

    [Header("Настройки Атласа Текстур")]
    [Tooltip("Текстурный атлас для ландшафта и оверлеев.")]
    public Texture2D worldTextureAtlas;
    [Tooltip("Размер атласа в тайлах (например, 2x2, 4x4, 8x8).")]
    public Vector2Int atlasSizeInTiles = new Vector2Int(2, 2);

    [Header("Ассеты данных")]
    public VoxelTypeSO globalBiomeBlock; // Нейтральный блок (земля/камень)
    public List<VoxelTypeSO> voxelTypes;
    
    [Tooltip("Список всех возможных наложений (руд, мха и т.д.) в мире")]
    public List<VoxelOverlaySO> voxelOverlays; // <--- ДОБАВЛЕНО

    [Header("Настройки генерации")]
    public NoiseSettingsSO heightmapNoiseSettings;
    public NoiseSettingsSO chaosNoiseSettings;
    public NoiseSettingsSO saturationNoiseSettings;
    public NoiseSettingsSO detailNoiseSettings;
}