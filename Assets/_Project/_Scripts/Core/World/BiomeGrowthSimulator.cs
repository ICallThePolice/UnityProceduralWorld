// --- НОВЫЙ ФАЙЛ: BiomeGrowthSimulator.cs ---
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

public class BiomeGrowthSimulator
{
    private List<BiomeAgent> agents;
    private readonly float growthStep;
    private readonly int maxIterations;

    public BiomeGrowthSimulator(List<BiomeAgent> initialAgents, float step = 2.0f, int iterations = 150)
    {
        this.agents = initialAgents;
        this.growthStep = step;
        this.maxIterations = iterations;
    }

    /// <summary>
    /// Запускает полную симуляцию роста и взаимодействия биомов.
    /// </summary>
    /// <returns>Финальный список активных и стабильных биомов.</returns>
    public List<BiomeAgent> Simulate(float minRadius)
    {
        for (int i = 0; i < maxIterations; i++)
        {
            // Если все агенты перестали расти, выходим из симуляции досрочно.
            if (!agents.Any(a => a.status == AgentStatus.Growing))
            {
                break;
            }
            SimulateStep();
        }

        CullSmallBiomes(minRadius);

        return agents.Where(a => a.status != AgentStatus.Culled && a.status != AgentStatus.Merged).ToList();
    }

    /// <summary>
    /// Выполняет один шаг симуляции.
    /// </summary>
    private void SimulateStep()
    {
        // 1. Сначала все "растущие" агенты пытаются увеличиться в размере
        foreach (var agent in agents)
        {
            if (agent.status == AgentStatus.Growing)
            {
                agent.Grow(growthStep);
            }
        }

        // 2. Проверяем столкновения между всеми парами агентов
        for (int i = 0; i < agents.Count; i++)
        {
            var agentA = agents[i];
            // Пропускаем уже обработанные или неактивные биомы
            if (agentA.status == AgentStatus.Merged || agentA.status == AgentStatus.Culled) continue;

            for (int j = i + 1; j < agents.Count; j++)
            {
                var agentB = agents[j];
                if (agentB.status == AgentStatus.Merged || agentB.status == AgentStatus.Culled) continue;

                // Проверяем расстояние между центрами и сумму их радиусов
                float distance = math.distance(agentA.position, agentB.position);
                float radiiSum = agentA.currentRadius + agentB.currentRadius;

                if (distance < radiiSum)
                {
                    // Столкновение! Применяем правила.
                    ApplyInteractionRules(agentA, agentB);
                }
            }
        }
    }

    /// <summary>
    /// Применяет правила взаимодействия к двум столкнувшимся агентам.
    /// </summary>
    private void ApplyInteractionRules(BiomeAgent a, BiomeAgent b)
    {
        bool isSameType = a.settings.biome.biomeID == b.settings.biome.biomeID;

        // ПРАВИЛО 1: Биомы одного типа сливаются в кластер
        if (isSameType)
        {
            // Если у них уже общий кластер, ничего не делаем
            if (a.clusterId == b.clusterId) return;

            int dominantClusterId = math.min(a.clusterId, b.clusterId);
            int clusterToConsume = math.max(a.clusterId, b.clusterId);

            // Распространяем новый ID на всех членов поглощаемого кластера
            PropagateClusterId(clusterToConsume, dominantClusterId);
            return;
        }

        // ПРАВИЛО 2: Биомы разных типов образуют пару
        // Если хотя бы один из них уже в паре, они просто блокируют рост друг друга.
        if (a.partnerId != -1 || b.partnerId != -1)
        {
            // Просто останавливаем их рост друг к другу.
            // В реальной системе здесь можно было бы добавить логику отталкивания.
            if (a.status == AgentStatus.Growing) a.status = AgentStatus.Stalled;
            if (b.status == AgentStatus.Growing) b.status = AgentStatus.Stalled;
            return;
        }

        // Если оба свободны, создаем пару
        if (a.partnerId == -1 && b.partnerId == -1)
        {
            a.status = AgentStatus.Paired;
            b.status = AgentStatus.Paired;
            a.partnerId = b.uniqueInstanceId;
            b.partnerId = a.uniqueInstanceId;
        }
    }

    private void PropagateClusterId(int oldClusterId, int newClusterId)
    {
        foreach (var agent in agents)
        {
            if (agent.clusterId == oldClusterId)
            {
                agent.clusterId = newClusterId;
                // Помечаем агента как слитого, чтобы он больше не участвовал в росте индивидуально
                agent.status = AgentStatus.Merged;
            }
        }
    }
    
    private void CullSmallBiomes(float minRadius)
    {
        // Группируем всех агентов по их финальному ID кластера
        var clusters = agents.GroupBy(a => a.clusterId);

        foreach (var cluster in clusters)
        {
            float maxClusterRadius = 0;
            // Находим максимальный радиус среди всех агентов в этом кластере
            foreach (var agentInCluster in cluster)
            {
                if (agentInCluster.currentRadius > maxClusterRadius)
                {
                    maxClusterRadius = agentInCluster.currentRadius;
                }
            }

            // Если даже самый большой агент в кластере меньше минимального радиуса,
            // помечаем весь кластер на удаление.
            if (maxClusterRadius < minRadius)
            {
                foreach (var agentToCull in cluster)
                {
                    agentToCull.status = AgentStatus.Culled;
                }
            }
        }
    }
}