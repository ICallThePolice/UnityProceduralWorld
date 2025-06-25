// --- ФАЙЛ: BiomeManager.cs (ФИНАЛЬНАЯ АРХИТЕКТУРА) ---

using UnityEngine;
using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// Управляет процедурным размещением "сердец" биомов и их артефактов по всему миру "на лету".
/// </summary>
public class BiomeManager : MonoBehaviour
{
    public static BiomeManager Instance { get; private set; }

    [HideInInspector] // Скрываем из инспектора, чтобы не мешалось
    public List<ArtifactInstance> lastGeneratedArtifacts = new List<ArtifactInstance>();

    [Header("Источники Биомов")]
    [Tooltip("Перетащите сюда все ассеты с настройками размещения для УНИКАЛЬНЫХ биомов (Эреб, Витал и т.д.). Нейтральный биом сюда не добавлять!")]
    public List<BiomePlacementSettingsSO> availableBiomes;

    [Header("Параметры Размещения")]
    [Tooltip("Размер одного региона, в котором может быть размещено не более одного 'сердца' биома.")]
    public int regionSize = 512;
    [Tooltip("Вероятность (0 до 1), что в данном регионе вообще появится какой-либо биом.")]
    [Range(0f, 1f)] public float biomePlacementChance = 0.3f;

    [Tooltip("Общий делитель плотности для артефактов. Чем МЕНЬШЕ это число, тем БОЛЬШЕ артефактов будет в мире.")]
    public float artifactDensityDivisor = 1000f;

    [Header("Параметры Прогрессии")]
    [Tooltip("Максимальное расстояние от центра, на котором прогрессия достигает 100%")]
    public float maxProgressionDistance = 3000f;
    [Tooltip("Сила 'хаоса', добавляемого к линейной прогрессии, чтобы сделать ее непредсказуемой.")]
    [Range(0f, 0.5f)] public float chaosFactor = 0.1f;

    // Кэш для уже рассчитанных биомов, чтобы не вычислять их каждый раз
    private Dictionary<Vector2Int, BiomeInstance> placedBiomeCache = new Dictionary<Vector2Int, BiomeInstance>();
    private int placementSeed;
    private FastNoiseLite chaosNoise;
    private FastNoiseLite heightmapNoise;
    public float GetHeightmapNoiseValue(Vector2 pos) => heightmapNoise.GetNoise(pos.x, pos.y);

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// Новый метод, который будет вызываться извне (от WorldController)
    /// для передачи всех нужных настроек.
    /// </summary>
    public void Initialize(WorldSettingsSO worldSettings)
    {
        if (worldSettings == null)
        {
            this.enabled = false;
            return;
        }

        // Вся логика, которая раньше была в Awake, теперь здесь.
        // Она зависит от настроек, которые нам передал WorldController.
        var heightSettings = worldSettings.heightmapNoiseSettings;
        placementSeed = heightSettings.seed;

        // Настройка шума хаоса
        chaosNoise = new FastNoiseLite(placementSeed + 1);
        chaosNoise.SetNoiseType(FastNoiseLite.NoiseType.Value);
        chaosNoise.SetFrequency(0.1f);
        
        // Настройка шума карты высот
        heightmapNoise = new FastNoiseLite(heightSettings.seed);
        heightmapNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        heightmapNoise.SetFrequency(heightSettings.scale);
        heightmapNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        heightmapNoise.SetFractalOctaves(heightSettings.octaves);
        heightmapNoise.SetFractalLacunarity(heightSettings.lacunarity);
        heightmapNoise.SetFractalGain(heightSettings.persistence);

    }

    /// <summary>
    /// Главный публичный метод. Находит или вычисляет "на лету" все биомы, которые могут влиять на заданную область.
    /// </summary>
    public List<BiomeInstance> GetBiomesInArea(Vector3Int chunkPosition, int renderDistance)
    {
        var biomeCores = new List<BiomeInstance>();
        int chunkRenderRadius = renderDistance + 2;
        Vector2Int centerRegion = new Vector2Int(
            Mathf.RoundToInt(chunkPosition.x * Chunk.Width / (float)regionSize),
            Mathf.RoundToInt(chunkPosition.z * Chunk.Width / (float)regionSize)
        );
        int searchRadius = Mathf.CeilToInt((chunkRenderRadius * Chunk.Width + 2048) / (float)regionSize);


        for (int x = -searchRadius; x <= searchRadius; x++)
        {
            for (int z = -searchRadius; z <= searchRadius; z++)
            {
                Vector2Int regionCoords = new Vector2Int(centerRegion.x + x, centerRegion.y + z);
                BiomeInstance instance = GetOrCreateBiomeInstanceForRegion(regionCoords);
                if (instance != null)
                {
                    biomeCores.Add(instance);
                }
            }
        }
        return biomeCores;
    }

    /// <summary>
    /// Детерминированно вычисляет, есть ли биом в регионе, и какой именно. Использует кэш.
    /// </summary>
    private BiomeInstance GetOrCreateBiomeInstanceForRegion(Vector2Int regionCoords)
    {
        if (placedBiomeCache.TryGetValue(regionCoords, out BiomeInstance instance))
        {
            return instance;
        }

        int regionSeed = placementSeed + regionCoords.x * 16777619 + regionCoords.y * 3145739;
        var random = new System.Random(regionSeed);

        if (random.NextDouble() > biomePlacementChance)
        {
            placedBiomeCache.Add(regionCoords, null);
            return null;
        }

        int biomeTypeIndex = random.Next(0, availableBiomes.Count);
        BiomePlacementSettingsSO settings = availableBiomes[biomeTypeIndex];

        float posX = (regionCoords.x + (float)random.NextDouble()) * regionSize;
        float posZ = (regionCoords.y + (float)random.NextDouble()) * regionSize;
        Vector2 potentialPosition = new Vector2(posX, posZ);

        float distFromOrigin = potentialPosition.magnitude;
        float progression = Mathf.Clamp01((distFromOrigin - settings.neutralZoneRadius) / (maxProgressionDistance - settings.neutralZoneRadius));
        float chaos = chaosNoise.GetNoise(posX, posZ) * chaosFactor;
        float finalProgression = Mathf.Clamp01(progression + chaos);

        float finalRadius = Mathf.Lerp(settings.influenceRadius.x, settings.influenceRadius.y, finalProgression);
        float finalAggressiveness = Mathf.Lerp(settings.aggressiveness.x, settings.aggressiveness.y, finalProgression);
        int finalTiers = Mathf.RoundToInt(Mathf.Lerp(settings.maxTiers.x, settings.maxTiers.y, finalProgression));
        float finalContrast = Mathf.Lerp(settings.contrast.x, settings.contrast.y, finalProgression);

        bool isInverted = false;
        if (settings.biome.terrainModificationType == TerrainModifier.Replace)
        {
            if (random.NextDouble() < settings.inversionChance) isInverted = true;
        }

        BiomeInstance newInstance = new BiomeInstance
        {
            position = potentialPosition,
            settings = settings,
            calculatedRadius = finalRadius,
            calculatedAggressiveness = finalAggressiveness,
            calculatedTiers = finalTiers,
            calculatedContrast = finalContrast,
            isInverted = isInverted,
            calculatedTierRadii = settings.tierPlacementRadii
        };

        int highestPointInBiome = 0;
        // Сэмплируем несколько точек по окружности биома, чтобы найти самую высокую
        const int samplePoints = 16;
        for (int i = 0; i < samplePoints; i++)
        {
            float angle = (360f / samplePoints) * i;
            Vector2 offset = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * newInstance.calculatedRadius;
            Vector2 samplePos = newInstance.position + offset;

            float heightNoiseValue = this.chaosNoise.GetNoise(samplePos.x, samplePos.y); // Используем любой шум для разнообразия
            int sampleHeight = 10 + Mathf.RoundToInt(((heightNoiseValue + 1f) / 2f) * 20f);

            if (sampleHeight > highestPointInBiome)
            {
                highestPointInBiome = sampleHeight;
            }
        }
        newInstance.biomeHighestPoint = highestPointInBiome;

        placedBiomeCache.Add(regionCoords, newInstance);
        return newInstance;
    }

    public BiomeInstance GetDominantBiomeAt(Vector2 worldPos, List<BiomeInstance> allBiomesInArea, out float maxInfluence)
    {
        BiomeInstance dominantBiome = null;
        maxInfluence = 0f;

        foreach (var biomeInstance in allBiomesInArea)
        {
            float distance = Vector2.Distance(worldPos, biomeInstance.position);
            if (distance < biomeInstance.calculatedRadius)
            {
                float influence = 1f - (distance / biomeInstance.calculatedRadius);
                influence = Mathf.Pow(influence, biomeInstance.calculatedContrast);
                
                if (influence > maxInfluence)
                {
                    maxInfluence = influence;
                    dominantBiome = biomeInstance;
                }
            }
        }
        return dominantBiome;
    }

    // Рассчитывает финальную высоту ландшафта в конкретной мировой точке, учитывая влияние доминантного биома.
    // <param name="worldPos">Мировые координаты XZ, для которых рассчитывается высота.</param>
    // <returns>Финальная высота ландшафта в блоках.</returns>
    public int CalculateFinalHeightAtPoint(Vector2 worldPos, List<BiomeInstance> relevantBiomes)
    {
        // 1. Расчет базовой высоты ландшафта по шуму.
        float baseHeightNoise = this.heightmapNoise.GetNoise(worldPos.x, worldPos.y);
        int baseTerrainHeight = 5 + Mathf.RoundToInt(((baseHeightNoise + 1f) / 2f) * 20f);

        // 2. Используем новый метод-помощник для поиска доминантного биома и силы его влияния.
        BiomeInstance dominantBiome = GetDominantBiomeAt(worldPos, relevantBiomes, out float maxInfluence);
        
        // Изначально, модифицированная высота равна базовой.
        int modifiedBiomeHeight = baseTerrainHeight;

        // Если в этой точке вообще есть влияние какого-либо биома...
        if (dominantBiome != null)
        {
            // ... то мы будем рассчитывать его воздействие.

            // Поскольку генераторы ожидают структуру `BiomeInstanceBurst`, мы должны
            // преобразовать наш класс `BiomeInstance` в эту структуру.
            var dominantBiomeBurst = new BiomeInstanceBurst
            {
                position = dominantBiome.position,
                influenceRadius = dominantBiome.calculatedRadius,
                aggressiveness = dominantBiome.calculatedAggressiveness,
                tiers = dominantBiome.calculatedTiers,
                contrast = dominantBiome.calculatedContrast,
                isInverted = dominantBiome.isInverted,
                biomeHighestPoint = dominantBiome.biomeHighestPoint,
                surfaceVoxelID = dominantBiome.settings.biome.SurfaceVoxel.ID,
                subSurfaceVoxelID = dominantBiome.settings.biome.SubSurfaceVoxel.ID,
                mainVoxelID = dominantBiome.settings.biome.MainVoxel.ID,
                subSurfaceDepth = dominantBiome.settings.biome.SubSurfaceDepth,
                terrainModificationType = dominantBiome.settings.biome.terrainModificationType,
                verticalDisplacementScale = dominantBiome.settings.biome.verticalDisplacementScale,
                tierRadii = dominantBiome.calculatedTierRadii
            };

            // Создаем экземпляры генераторов для вызова их логики.
            var additiveGenerator = new AdditiveBiomeGenerator();
            var subtractiveGenerator = new SubtractiveBiomeGenerator();
            var replaceGenerator = new ReplaceBiomeGenerator();

            // Рассчитываем высоту в центре доминантного биома, она нужна некоторым генераторам.
            float biomeCenterNoise = heightmapNoise.GetNoise(dominantBiome.position.x, dominantBiome.position.y);
            float dominantBiomeBaseHeight = 10 + Mathf.RoundToInt(((biomeCenterNoise + 1f) / 2f) * 20f);

            // В зависимости от типа биома, вызываем соответствующий генератор.
            switch (dominantBiomeBurst.terrainModificationType)
            {
                case TerrainModifier.Additive:
                    modifiedBiomeHeight = additiveGenerator.GetModifiedHeight(dominantBiomeBurst, baseTerrainHeight, dominantBiomeBaseHeight, worldPos, heightmapNoise);
                    break;
                case TerrainModifier.Subtractive:
                    modifiedBiomeHeight = subtractiveGenerator.GetModifiedHeight(dominantBiomeBurst, baseTerrainHeight, dominantBiomeBaseHeight, worldPos, heightmapNoise);
                    break;
                case TerrainModifier.Replace:
                    modifiedBiomeHeight = replaceGenerator.GetModifiedHeight(dominantBiomeBurst, baseTerrainHeight, dominantBiomeBaseHeight, worldPos, heightmapNoise);
                    break;
            }
        }

        // 5. Финальное смешивание высот.
        // Плавно смешиваем базовую высоту ландшафта и высоту, измененную биомом.
        // Сила смешивания зависит от силы влияния (maxInfluence) доминантного биома.
        return (int)Mathf.Lerp(baseTerrainHeight, modifiedBiomeHeight, maxInfluence);
    }

    public void CreateArtifactAndSpawnChildren(BiomeArtifactSO artifactSO, Vector2 artifactPos, float finalSize, int groundHeight, float yOffset, BiomeInstance rootBiome, List<BiomeInstance> allBiomesInArea, System.Random random, List<ArtifactInstance> artifactListToFill, ushort? overrideVoxelID = null)
    {
        // --- 1. Определяем, какой воксель использовать ---
        ushort finalVoxelID;
        if (overrideVoxelID.HasValue)
        {
            // Если нам передали ID для переопределения, используем его.
            finalVoxelID = overrideVoxelID.Value;
        }
        else
        {
            // Иначе, берем основной материал из корневого биома.
            finalVoxelID = rootBiome.settings.biome.MainVoxel.ID;
        }

        // --- 2. Создаем текущий артефакт ---
        const float maxArtifactThickness = 15.0f;
        float calculatedHeight = rootBiome.settings.biome.verticalDisplacementScale * artifactSO.relativeHeight;
        float finalHeight = Mathf.Min(calculatedHeight, maxArtifactThickness);

        var parentArtifactInstance = new ArtifactInstance
        {
            position = artifactPos,
            settings = artifactSO,
            calculatedSize = new Vector2(finalSize, finalSize),
            calculatedHeight = finalHeight,
            groundHeight = groundHeight,
            yOffset = yOffset,
            mainVoxelID = finalVoxelID
        };
        artifactListToFill.Add(parentArtifactInstance);

        // --- 3. РЕКУРСИЯ: Ищем и создаем дочерние артефакты ---
        if (parentArtifactInstance.settings.childArtifacts == null || parentArtifactInstance.settings.childArtifacts.Count == 0)
        {
            return; // Выход из рекурсии
        }

        foreach (var childPlacementInfo in parentArtifactInstance.settings.childArtifacts)
        {
            ushort? childOverrideID = null;
            if (childPlacementInfo.overrideVoxel != null)
            {
                childOverrideID = childPlacementInfo.overrideVoxel.ID;
            }
            // Если есть переопределение ID, используем его, иначе берем основной материал родителя.
            for (int i = 0; i < childPlacementInfo.spawnCount; i++)
            {
                // Позиция: Позиция родителя + горизонтальное смещение ребенка
                Vector2 childOffset = Vector2.zero;
                if (childPlacementInfo.horizontalOffset > 0)
                {
                    float angle = childPlacementInfo.useRandomAngle ? (float)random.NextDouble() * 360f : 0f;
                    childOffset = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * childPlacementInfo.horizontalOffset;
                }
                Vector2 childArtifactPos = parentArtifactInstance.position + childOffset;

                // Размер: Рассчитывается относительно размера родителя
                float childSizeMultiplier = Mathf.Lerp(childPlacementInfo.artifactSO.relativeSize.x, childPlacementInfo.artifactSO.relativeSize.y, (float)random.NextDouble());
                float childFinalSize = finalSize * childSizeMultiplier;

                int childGroundHeight = (int)parentArtifactInstance.groundHeight;
                float childYOffset = childPlacementInfo.yOffset;

                CreateArtifactAndSpawnChildren(
                    childPlacementInfo.artifactSO,
                    childArtifactPos,
                    childFinalSize,
                    childGroundHeight,
                    childYOffset,
                    rootBiome,
                    allBiomesInArea,
                    random,
                    artifactListToFill,
                    childOverrideID
                );
            }
        }
    }

    /// <summary>
    /// Главный метод размещения артефактов. Теперь он - "диспетчер", который выбирает и запускает нужную стратегию.
    /// </summary>
    public void PlaceArtifacts(BiomeInstance parent, System.Random random, List<BiomeInstance> allBiomesInArea, List<ArtifactInstance> artifactListToFill)
    {
        foreach (var artifactConfig in parent.settings.rootArtifacts)
        {
            IArtifactPlacementStrategy strategy;
            switch (artifactConfig.placementStrategy)
            {
                case ArtifactPlacementStrategyType.OnTieredSlopes:
                    strategy = new TieredSlopePlacementStrategy();
                    break;
                case ArtifactPlacementStrategyType.OuterRingWithHeightCheck: // <-- ДОБАВЬТЕ ЭТОТ БЛОК
                    strategy = new OuterRingPlacementStrategy();
                    break;
                default:
                    strategy = new TieredSlopePlacementStrategy();
                    break;
            }

            // Этот вызов теперь должен быть без ошибок, так как все параметры совпадают
            strategy.Place(parent, artifactConfig, random, artifactListToFill, allBiomesInArea, this);
        }
    }

    #if UNITY_EDITOR
    void OnDrawGizmos()
    {
        // --- Отрисовка основных биомов (из кэша, как и раньше) ---
        if (placedBiomeCache != null)
        {
            foreach (var instance in placedBiomeCache.Values)
            {
                if (instance == null || instance.settings == null || instance.settings.biome == null) continue;
                Vector3 parentCenter3D = new Vector3(instance.position.x, 0, instance.position.y);
                switch (instance.settings.biome.terrainModificationType)
                {
                    case TerrainModifier.Additive: Gizmos.color = Color.green; break;
                    case TerrainModifier.Subtractive: Gizmos.color = Color.magenta; break;
                    case TerrainModifier.Replace: Gizmos.color = Color.cyan; break;
                }
                Gizmos.DrawWireSphere(parentCenter3D, instance.calculatedRadius);
            }
        }

        // --- НОВАЯ ЛОГИКА: Отрисовка артефактов из специального отладочного списка ---
        if (lastGeneratedArtifacts != null && lastGeneratedArtifacts.Count > 0)
        {
            foreach (var artifact in lastGeneratedArtifacts)
            {
                if (artifact == null || artifact.settings == null) continue;
                
                // Рассчитываем 3D позицию центра артефакта, используя правильную высоту
                Vector3 artifactCenter3D = new Vector3(artifact.position.x, artifact.groundHeight + artifact.yOffset, artifact.position.y);
                
                switch (artifact.settings.artifactType)
                {
                    case BiomeArtifactType.FloatingIslet:
                        Gizmos.color = Color.cyan;
                        Vector3 islandBoxSize = new Vector3(artifact.calculatedSize.x, artifact.calculatedHeight, artifact.calculatedSize.y);
                        Gizmos.DrawWireCube(artifactCenter3D + new Vector3(0, artifact.calculatedHeight / 2f, 0), islandBoxSize);
                        break;
                    case BiomeArtifactType.MiniCrater:
                        Gizmos.color = Color.red;
                        float craterRadius = artifact.calculatedSize.x / 2f;
                        Gizmos.DrawWireSphere(artifactCenter3D, craterRadius);
                        break;
                    default:
                        Gizmos.color = Color.yellow;
                        float defaultRadius = artifact.calculatedSize.x / 2f;
                        Gizmos.DrawWireSphere(artifactCenter3D, defaultRadius);
                        break;
                }

                Handles.Label(artifactCenter3D + Vector3.up * 2, artifact.settings.name);
            }
        }
    }
    #endif
}