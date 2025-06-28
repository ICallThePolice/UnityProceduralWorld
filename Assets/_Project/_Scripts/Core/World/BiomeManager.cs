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
    public int regionSize = 512;
    [Range(0f, 1f)] public float biomePlacementChance = 0.3f;
    [Tooltip("Радиус в регионах для поиска соседей при генерации")]
    public int searchRadiusInRegions = 3; 

    private Dictionary<Vector2Int, List<BiomeCluster>> regionCache = new Dictionary<Vector2Int, List<BiomeCluster>>();
    private int placementSeed;
    
    void Awake() { Instance = this; }

    public void Initialize(WorldSettingsSO worldSettings)
    {
        if (worldSettings == null) { this.enabled = false; return; }
        placementSeed = worldSettings.heightmapNoiseSettings.seed;
    }

    public List<BiomeCluster> GetClustersInArea(Vector3Int chunkPosition, int renderDistance)
    {
        var finalClusters = new HashSet<BiomeCluster>();
        int searchRadius = renderDistance + 2; 
        Vector2Int centerRegion = new Vector2Int(
            Mathf.FloorToInt((chunkPosition.x * Chunk.Width) / (float)regionSize),
            Mathf.FloorToInt((chunkPosition.z * Chunk.Width) / (float)regionSize));

        for (int x = -searchRadius; x <= searchRadius; x++) {
            for (int z = -searchRadius; z <= searchRadius; z++) {
                foreach (var cluster in GetOrCreateClustersForRegion(centerRegion + new Vector2Int(x, z))) {
                    finalClusters.Add(cluster);
                }
            }
        }
        return finalClusters.ToList();
    }

    private List<BiomeCluster> GetOrCreateClustersForRegion(Vector2Int regionCoords)
    {
        if (regionCache.TryGetValue(regionCoords, out var clusters)) return clusters;

        // --- ЭТАП 1: Генерация всех потенциальных ядер ---
        var candidateInstances = new List<BiomeInstance>();
        for (int x = -searchRadiusInRegions; x <= searchRadiusInRegions; x++) {
            for (int z = -searchRadiusInRegions; z <= searchRadiusInRegions; z++) {
                Vector2Int currentRegion = regionCoords + new Vector2Int(x, z);
                if (regionCache.ContainsKey(currentRegion)) continue;
                
                int regionSeed = placementSeed + currentRegion.x * 16777619 + currentRegion.y * 3145739;
                var random = new System.Random(regionSeed);

                if (random.NextDouble() < biomePlacementChance) {
                    var settings = availableBiomes[random.Next(0, availableBiomes.Count)];
                    var biomeInstance = new BiomeInstance {
                        position = new float2((currentRegion.x + (float)random.NextDouble()) * regionSize, (currentRegion.y + (float)random.NextDouble()) * regionSize),
                        settings = settings,
                        calculatedRadius = Mathf.Lerp(settings.influenceRadius.x, settings.influenceRadius.y, 0.5f),
                        calculatedContrast = Mathf.Lerp(settings.contrast.x, settings.contrast.y, 0.5f),
                        coreRadiusPercentage = settings.coreRadiusPercentage
                    };
                    candidateInstances.Add(biomeInstance);
                }
            }
        }
        
        // --- ЭТАП 2: Построение кластеров по принципу "видимости" ---
        var finalClusters = new List<BiomeCluster>();
        var assignedInstances = new HashSet<BiomeInstance>();

        foreach (var instance in candidateInstances) {
            if (assignedInstances.Contains(instance)) continue;
            
            var matchingCluster = finalClusters.FirstOrDefault(c => 
                c.settings.biome.biomeID == instance.settings.biome.biomeID &&
                c.nodes.Values.Any(n => math.distance(n.position, instance.position) < (c.influenceRadius + instance.calculatedRadius) * 0.8f));

            if (matchingCluster != null) {
                var newNode = new BiomeCluster.BiomeNode(matchingCluster.nodes.Count, instance.position);
                matchingCluster.AddNode(newNode);
                var closestNode = matchingCluster.nodes.Values.OrderBy(n => math.distance(n.position, newNode.position)).First();
                matchingCluster.AddEdge(new BiomeCluster.BiomeEdge(closestNode.id, newNode.id));
            } else {
                var newNode = new BiomeCluster.BiomeNode(0, instance.position);
                var newCluster = new BiomeCluster(newNode, instance);
                finalClusters.Add(newCluster);
            }
            assignedInstances.Add(instance);
        }

        // --- ЭТАП 3: Удаление "зажатых" кластеров (Culling) ---
        var clustersToCull = new HashSet<BiomeCluster>();

        for (int i = 0; i < finalClusters.Count; i++)
        {
            for (int j = i + 1; j < finalClusters.Count; j++)
            {
                var clusterA = finalClusters[i];
                var clusterB = finalClusters[j];

                // Пропускаем, если кластеры уже помечены на удаление или принадлежат одному биому
                if (clustersToCull.Contains(clusterA) || clustersToCull.Contains(clusterB) || clusterA.settings.biome.biomeID == clusterB.settings.biome.biomeID)
                {
                    continue;
                }

                float distance = GetMinDistanceBetweenClusters(clusterA, clusterB);
                
                // Проверяем, пересекаются ли их радиусы влияния
                if (distance < clusterA.influenceRadius + clusterB.influenceRadius)
                {
                    // Если пересекаются, помечаем на удаление более "слабый" кластер (с меньшим радиусом)
                    if (clusterA.influenceRadius < clusterB.influenceRadius)
                    {
                        clustersToCull.Add(clusterA);
                    }
                    else
                    {
                        clustersToCull.Add(clusterB);
                    }
                }
            }
        }

        // Удаляем все помеченные кластеры из финального списка
        if (clustersToCull.Count > 0)
        {
            finalClusters.RemoveAll(c => clustersToCull.Contains(c));
        }


        // Кэшируем результат для всех затронутых регионов (без изменений)
        for (int x = -searchRadiusInRegions; x <= searchRadiusInRegions; x++) {
            for (int z = -searchRadiusInRegions; z <= searchRadiusInRegions; z++) {
                Vector2Int r = regionCoords + new Vector2Int(x, z);
                if (!regionCache.ContainsKey(r)) {
                    regionCache.Add(r, finalClusters);
                }
            }
        }
        return finalClusters;
    }
    
    private float GetMinDistanceBetweenClusters(BiomeCluster a, BiomeCluster b) {
        float minDst = float.MaxValue;
        foreach (var nodeA in a.nodes.Values) {
            foreach (var nodeB in b.nodes.Values) {
                minDst = math.min(minDst, math.distance(nodeA.position, nodeB.position));
            }
        }
        return minDst;
    }
    
    // Метод GetOverlaysInArea остается без изменений
    public List<OverlayPlacementDataBurst> GetOverlaysInArea(Vector3Int chunkPosition, int renderDistance) {
        return new List<OverlayPlacementDataBurst>();
    }
}