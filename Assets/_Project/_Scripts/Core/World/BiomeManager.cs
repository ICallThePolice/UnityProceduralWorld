// --- ФАЙЛ: BiomeManager.cs (ПЕРЕРАБОТАННАЯ ВЕРСИЯ) ---
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

public class BiomeManager : MonoBehaviour
{
    public static BiomeManager Instance { get; private set; }

    [Header("Источники Биомов")]
    public List<BiomePlacementSettingsSO> availableBiomes;

    [Header("Параметры Размещения")]
    [Tooltip("Размер региона, в котором происходит один 'посев' биомов")]
    public int regionSize = 1024;
    [Tooltip("Минимальный радиус, который должен иметь биом или кластер, чтобы не быть удаленным")]
    public float minBiomeRadius = 50f;
    
    [Tooltip("Толщина нейтральной зоны в юнитах мира/пикселях карты")]
    public int neutralZoneWidth = 2;
    [Tooltip("Масштаб разрешения карты. 1 = 1 пиксель на юнит. 0.25 = 1 пиксель на 4 юнита.")]
    [Range(0.1f, 1.0f)]
    public float resolutionScale = 0.25f;
    [Tooltip("Среднее количество 'семян' биомов на один регион")]
    public int seedsPerRegion = 5;
    [Tooltip("Радиус в регионах для предварительного поиска и симуляции")]
    public int simulationLookAhead = 1;

    [Header("Ассеты")]
    [Tooltip("Перетащите сюда ассет 'BiomeMapGenerator.compute'")]
    public ComputeShader biomeMapComputeShader;

    // --- Новый кэш будет хранить готовых, "выросших" агентов ---
    private Dictionary<Vector2Int, List<BiomeAgent>> regionCache = new Dictionary<Vector2Int, List<BiomeAgent>>();
    private int placementSeed;
    private int nextInstanceId = 1;
    private RenderTexture currentMinimapTexture;
    private BiomeMapGPUGenerator mapGenerator;

    void Awake()
    {
        Instance = this;
        mapGenerator = new BiomeMapGPUGenerator(biomeMapComputeShader);
    }

    void OnDestroy()
    {
        // Очищаем текстуру при выходе, чтобы избежать утечек
        if (currentMinimapTexture != null)
        {
            currentMinimapTexture.Release();
            currentMinimapTexture = null;
        }
    }

    public void Initialize(WorldSettingsSO worldSettings)
    {
        if (worldSettings == null) { this.enabled = false; return; }
        placementSeed = worldSettings.heightmapNoiseSettings.seed;
    }

    /// <summary>
    /// Главный публичный метод. Возвращает готовый список биомов для указанной области.
    /// </summary>
    public List<BiomeAgent> GetBiomesInArea(Vector3Int chunkPosition)
    {
        var finalAgents = new HashSet<BiomeAgent>();
        Vector2Int centerRegion = new Vector2Int(
            Mathf.FloorToInt((chunkPosition.x * Chunk.Width) / (float)regionSize),
            Mathf.FloorToInt((chunkPosition.z * Chunk.Width) / (float)regionSize));

        // Собираем агентов из центрального региона и всех соседних.
        for (int x = -simulationLookAhead; x <= simulationLookAhead; x++)
        {
            for (int z = -simulationLookAhead; z <= simulationLookAhead; z++)
            {
                // Метод GetOrCreateAgentsForRegion теперь содержит всю логику
                foreach (var agent in GetOrCreateAgentsForRegion(centerRegion + new Vector2Int(x, z)))
                {
                    finalAgents.Add(agent);
                }
            }
        }
        return finalAgents.ToList();
    }
    
    /// <summary>
    /// Получает или создает и симулирует список агентов для одного региона.
    /// </summary>
    private List<BiomeAgent> GetOrCreateAgentsForRegion(Vector2Int regionCoords)
    {
        // Если результат симуляции для этого региона уже есть в кэше, возвращаем его.
        if (regionCache.TryGetValue(regionCoords, out var cachedAgents))
        {
            return cachedAgents;
        }

        // --- ЭТАП 1: Посев семян ---
        // Собираем семена из текущего региона и всех соседних, чтобы симуляция на границах была корректной.
        var initialAgents = new List<BiomeAgent>();
        for (int x = -simulationLookAhead; x <= simulationLookAhead; x++)
        {
            for (int z = -simulationLookAhead; z <= simulationLookAhead; z++)
            {
                Vector2Int currentRegion = regionCoords + new Vector2Int(x, z);
                
                // Проверяем, не были ли семена для этого региона уже сгенерированы в рамках другой симуляции
                if (regionCache.ContainsKey(currentRegion)) continue;

                // Используем уникальный сид для каждого региона, чтобы генерация была детерминированной
                int regionSeed = placementSeed + currentRegion.x * 16777619 + currentRegion.y * 3145739;
                var random = new System.Random(regionSeed);

                for(int i = 0; i < seedsPerRegion; i++)
                {
                    // Выбираем случайный тип биома
                    var settings = availableBiomes[random.Next(0, availableBiomes.Count)];
                    
                    // Создаем случайную позицию внутри региона
                    var position = new float2(
                        (currentRegion.x + (float)random.NextDouble()) * regionSize, 
                        (currentRegion.y + (float)random.NextDouble()) * regionSize
                    );

                    // Чем дальше от центра мира (0,0), тем выше агрессивность
                    float distanceToOrigin = math.distance(position, float2.zero);
                    float aggressiveness = math.saturate(distanceToOrigin / 5000f); // 5000 - условная дистанция до "макс. сложности"

                    var newAgent = new BiomeAgent(nextInstanceId++, position, settings, aggressiveness);
                    initialAgents.Add(newAgent);
                }
            }
        }

        // --- ЭТАП 2: Запуск симуляции ---
        var simulator = new BiomeGrowthSimulator(initialAgents);
        List<BiomeAgent> finalAgents = simulator.Simulate(minBiomeRadius);

        // --- ЭТАП 3: Кэширование результата ---
        // Мы кэшируем ПОЛНЫЙ результат симуляции для КАЖДОГО затронутого региона.
        // Это гарантирует, что при запросе соседнего региона мы получим тот же самый согласованный результат.
        for (int x = -simulationLookAhead; x <= simulationLookAhead; x++)
        {
            for (int z = -simulationLookAhead; z <= simulationLookAhead; z++)
            {
                Vector2Int r = regionCoords + new Vector2Int(x, z);
                if (!regionCache.ContainsKey(r))
                {
                    regionCache.Add(r, finalAgents);
                }
            }
        }

        return finalAgents;
    }
    
    // Этот метод нам больше не нужен в BiomeManager, но может понадобиться где-то еще.
    // Пока оставим его пустым.
    public List<OverlayPlacementDataBurst> GetOverlaysInArea(Vector3Int chunkPosition, int renderDistance) {
        return new List<OverlayPlacementDataBurst>();
    }
}