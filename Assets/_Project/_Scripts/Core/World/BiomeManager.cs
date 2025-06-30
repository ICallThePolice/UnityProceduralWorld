// --- ФАЙЛ: BiomeManager.cs (НОВАЯ, БЫСТРАЯ ВЕРСИЯ) ---
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;

public class BiomeManager : MonoBehaviour
{
    public static BiomeManager Instance { get; private set; }
    
    // Эти поля вы настраиваете в инспекторе
    public WorldSettingsSO worldSettings;
    public BiomeMapSO biomeMap; // Перетащите сюда ваш ассет "DefaultBiomeMap"
    
    private FastNoiseLite chaosNoise;
    private FastNoiseLite saturationNoise;
    private FastNoiseLite warpNoise; // Шум для искажения границ (имитация агрессивности)

    void Awake()
    {
        Instance = this;
    }

    public void Initialize(WorldSettingsSO settings)
    {
        this.worldSettings = settings;
        
        if (this.worldSettings == null) 
        {
            Debug.LogError("WorldSettings не назначены в WorldController! Генерация невозможна.");
            this.enabled = false; 
            return; 
        }
        if (this.biomeMap == null)
        {
            Debug.LogError("BiomeMap не назначен в WorldController! Генерация невозможна.");
            this.enabled = false;
            return;
        }
        if (this.biomeMap.biomeMappings == null || this.biomeMap.biomeMappings.Length == 0)
        {
            Debug.LogError("Ассет BiomeMap пуст (не содержит ни одного биома)! Генерация невозможна.");
            this.enabled = false;
            return;
        }

        if (this.worldSettings == null || biomeMap == null || biomeMap.biomeMappings.Length == 0)
        {
            Debug.LogError("WorldSettings или BiomeMap не назначены или пусты в BiomeManager! Генерация невозможна.");
            this.enabled = false;
            return;
        }

        // Инициализируем все генераторы шума на основе настроек
        chaosNoise = new FastNoiseLite(worldSettings.chaosNoiseSettings.seed);
        chaosNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        chaosNoise.SetFrequency(worldSettings.chaosNoiseSettings.scale);
        
        saturationNoise = new FastNoiseLite(worldSettings.saturationNoiseSettings.seed);
        saturationNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        saturationNoise.SetFrequency(worldSettings.saturationNoiseSettings.scale);

        // Этот шум можно добавить в WorldSettingsSO по аналогии с другими
        warpNoise = new FastNoiseLite(worldSettings.detailNoiseSettings.seed); 
        warpNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        warpNoise.SetFrequency(worldSettings.detailNoiseSettings.scale * 5f); // Искажения должны быть более мелкими
    }

    /// <summary>
    /// Мгновенно определяет основной и вторичный биом для точки мира, а также фактор их смешивания.
    /// </summary>
    public void GetBiomeDataAt(float2 worldPos, out ushort primaryID, out ushort secondaryID, out float blendFactor)
    {
        // 1. Вычисляем значения "климатических" шумов
        float chaosValue = chaosNoise.GetNoise(worldPos.x, worldPos.y);
        float saturationValue = saturationNoise.GetNoise(worldPos.x, worldPos.y);
        Vector2 targetPosition = new Vector2(chaosValue, saturationValue);

        // 2. Находим два ближайших биома на карте биомов (в BiomeMapSO)
        var sortedBiomes = biomeMap.biomeMappings
            .OrderBy(mapping => Vector2.SqrMagnitude(mapping.position - targetPosition))
            .Take(2)
            .ToList();

        // 3. Обрабатываем результат
        if (sortedBiomes.Count == 0)
        {
            primaryID = worldSettings.globalBiomeBlock.ID;
            secondaryID = worldSettings.globalBiomeBlock.ID;
            blendFactor = 0;
            return;
        }

        BiomeMapping closest = sortedBiomes[0];
        primaryID = closest.biome.BiomeBlock.ID;

        // 4. Если есть второй биом, рассчитываем смешивание
        if (sortedBiomes.Count > 1)
        {
            BiomeMapping secondClosest = sortedBiomes[1];
            secondaryID = secondClosest.biome.BiomeBlock.ID;
            
            float distToClosest = Mathf.Sqrt(Vector2.SqrMagnitude(closest.position - targetPosition));
            float distToSecondClosest = Mathf.Sqrt(Vector2.SqrMagnitude(secondClosest.position - targetPosition));
            float totalDist = distToClosest + distToSecondClosest;
            
            // Базовый фактор смешивания
            float baseBlend = (totalDist > 0.001f) ? distToClosest / totalDist : 0f;

            // 5. Применяем искажение границы (имитация агрессивности)
            float warpValue = warpNoise.GetNoise(worldPos.x, worldPos.y) * 0.1f; // небольшой фактор искажения
            blendFactor = Mathf.Clamp01(baseBlend + warpValue);
        }
        else
        {
            secondaryID = primaryID;
            blendFactor = 0;
        }
    }
}