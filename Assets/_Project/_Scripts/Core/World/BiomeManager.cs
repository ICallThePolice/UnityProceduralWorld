// --- ФАЙЛ: BiomeManager.cs (ПЕРЕРАБОТАННАЯ ВЕРСИЯ) ---
using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks; // Для асинхронных задач
using Unity.Mathematics;

public class BiomeManager : MonoBehaviour
{
    public static BiomeManager Instance { get; private set; }

    [Header("Источники Биомов")]
    public List<BiomePlacementSettingsSO> availableBiomes;

    [Header("Параметры Размещения")]
    public int regionSize = 1024;
    public float minDistanceBetweenSeeds = 128f;
    public int simulationSteps = 150; // Увеличим для более полных биомов

    [Header("Динамическая агрессивность")]
    public float maxAggressivenessDistance = 5000f;
    public int baseSeedsPerRegion = 5;
    public int maxSeedsPerRegion = 10;
    
    // --- АСИНХРОННЫЙ КЭШ ---
    // Кэшируем не результат, а саму ЗАДАЧУ по симуляции.
    private readonly Dictionary<Vector2Int, Task<Dictionary<int2, int>>> runningSimulations = new Dictionary<Vector2Int, Task<Dictionary<int2, int>>>();
    private readonly Dictionary<int, BiomeAgent> allAgents = new Dictionary<int, BiomeAgent>();

    private int placementSeed;
    private int nextInstanceId = 1;
    private readonly object lockObject = new object(); // Для потокобезопасности

    void Awake()
    {
        Instance = this;
    }

    public void Initialize(WorldSettingsSO worldSettings)
    {
        if (worldSettings == null) { this.enabled = false; return; }
        placementSeed = worldSettings.heightmapNoiseSettings.seed;
    }

    /// <summary>
    /// Асинхронно получает или запускает симуляцию для региона.
    /// </summary>
    public Task<Dictionary<int2, int>> GetBiomeSiteGridFor(Vector2Int regionCoords)
    {
        lock (lockObject) // Блокируем доступ к словарю из разных потоков
        {
            if (runningSimulations.TryGetValue(regionCoords, out var simulationTask))
            {
                return simulationTask; // Если задача уже запущена, просто возвращаем ее
            }

            // Если задачи нет, создаем и запускаем новую в фоновом потоке
            var newSimulationTask = Task.Run(() =>
            {
                var initialAgents = new List<BiomeAgent>();
                for (int x = -1; x <= 1; x++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        GenerateSeedsForRegion(regionCoords + new Vector2Int(x, z), initialAgents);
                    }
                }

                var simulator = new BiomeSimulator(initialAgents, simulationSteps);
                return simulator.Simulate();
            });

            runningSimulations[regionCoords] = newSimulationTask;
            return newSimulationTask;
        }
    }

    /// <summary>
    /// Создает "семена" биомов для ОДНОГО региона с учетом дистанции от центра и минимального расстояния между собой.
    /// </summary>
    private void GenerateSeedsForRegion(Vector2Int regionCoords, List<BiomeAgent> agentList)
    {
        int regionSeed = placementSeed + regionCoords.x * 16777619 + regionCoords.y * 3145739;
        var random = new System.Random(regionSeed);

        float regionCenterDist = Vector2.Distance(regionCoords, Vector2.zero) * regionSize;
        float seedsLerpFactor = Mathf.Clamp01(regionCenterDist / maxAggressivenessDistance);
        int seedsToSpawn = (int)Mathf.Lerp(baseSeedsPerRegion, maxSeedsPerRegion, seedsLerpFactor);
        
        List<float2> spawnedPositions = new List<float2>();

        for (int i = 0; i < seedsToSpawn; i++)
        {
            for(int attempt = 0; attempt < 10; attempt++)
            {
                var settings = availableBiomes[random.Next(0, availableBiomes.Count)];
                var position = new float2(
                    (regionCoords.x + (float)random.NextDouble()) * regionSize,
                    (regionCoords.y + (float)random.NextDouble()) * regionSize
                );

                bool isTooClose = false;
                foreach (var existingPos in spawnedPositions)
                {
                    if (math.distance(position, existingPos) < minDistanceBetweenSeeds)
                    {
                        isTooClose = true;
                        break;
                    }
                }

                if (isTooClose) continue;

                float distanceToOrigin = math.distance(position, float2.zero);
                float aggressivenessFactor = Mathf.Clamp01(distanceToOrigin / maxAggressivenessDistance);

                var newAgent = new BiomeAgent(nextInstanceId++, position, settings, aggressivenessFactor);
                
                lock(lockObject) // Добавляем агента в общий список потокобезопасно
                {
                    if (!allAgents.ContainsKey(newAgent.uniqueInstanceId))
                    {
                        allAgents.Add(newAgent.uniqueInstanceId, newAgent);
                    }
                }
                
                agentList.Add(newAgent);
                spawnedPositions.Add(position);
                break;
            }
        }
    }
    /// <summary>
    /// Возвращает агента по его уникальному ID.
    /// </summary>
    public BiomeAgent GetAgent(int agentId)
    {
        lock(lockObject)
        {
            allAgents.TryGetValue(agentId, out var agent);
            return agent;
        }
    }
}