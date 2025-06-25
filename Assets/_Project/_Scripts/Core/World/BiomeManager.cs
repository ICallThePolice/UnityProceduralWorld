using UnityEngine;
using System.Collections.Generic;

public class BiomeManager : MonoBehaviour
{
    public static BiomeManager Instance { get; private set; }

    [Header("Источники Биомов")]
    public List<BiomePlacementSettingsSO> availableBiomes;

    [Header("Параметры Размещения")]
    public int regionSize = 512;
    [Range(0f, 1f)] public float biomePlacementChance = 0.3f;
    
    [Header("Параметры Прогрессии")]
    public float maxProgressionDistance = 3000f;
    [Range(0f, 0.5f)] public float chaosFactor = 0.1f;
    
    private Dictionary<Vector2Int, BiomeInstance> placedBiomeCache = new Dictionary<Vector2Int, BiomeInstance>();
    private int placementSeed;
    private FastNoiseLite chaosNoise;

    private void Awake()
    {
        Instance = this;
    }

    public void Initialize(WorldSettingsSO worldSettings)
    {
        if (worldSettings == null)
        {
            this.enabled = false;
            return;
        }

        var heightSettings = worldSettings.heightmapNoiseSettings;
        placementSeed = heightSettings.seed;

        chaosNoise = new FastNoiseLite(placementSeed + 1);
        chaosNoise.SetNoiseType(FastNoiseLite.NoiseType.Value);
        chaosNoise.SetFrequency(0.1f);
    }

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

        BiomeInstance newInstance = new BiomeInstance
        {
            position = potentialPosition,
            settings = settings,
            calculatedRadius = Mathf.Lerp(settings.influenceRadius.x, settings.influenceRadius.y, finalProgression),
            calculatedContrast = Mathf.Lerp(settings.contrast.x, settings.contrast.y, finalProgression),
            // Остальные сложные параметры нам пока не нужны
        };

        placedBiomeCache.Add(regionCoords, newInstance);
        return newInstance;
    }
}