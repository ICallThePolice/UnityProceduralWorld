// --- ФАЙЛ: BiomeSimulator.cs (ФИНАЛЬНАЯ ВЕРСИЯ 3.0) ---
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

public class BiomeSimulator
{
    private class Site
    {
        public int OwnerId = -1; // -1 == Нейтральный
    }

    private readonly Dictionary<int, BiomeAgent> agents;
    private readonly Dictionary<int2, Site> siteGrid = new Dictionary<int2, Site>();
    private readonly int simulationSteps;

    // Теперь это список "активных" агентов, которые еще могут расти
    private List<BiomeAgent> activeAgents;

    // Вспомогательный массив для проверки соседей в случайном порядке
    private readonly List<int2> neighborOffsets = new List<int2>
    {
        new int2(0, 1), new int2(1, 0), new int2(0, -1), new int2(-1, 0)
    };

    public BiomeSimulator(List<BiomeAgent> initialAgents, int steps = 100)
    {
        this.agents = initialAgents.ToDictionary(agent => agent.uniqueInstanceId, agent => agent);
        this.simulationSteps = steps;
        this.activeAgents = new List<BiomeAgent>(initialAgents);

        // Захватываем стартовые ячейки
        foreach (var agent in initialAgents)
        {
            // Координату мира преобразуем в координату сайта
            int2 siteCoord = new int2(Mathf.FloorToInt(agent.startingPosition.x), Mathf.FloorToInt(agent.startingPosition.y));
            if (!siteGrid.ContainsKey(siteCoord))
            {
                siteGrid[siteCoord] = new Site { OwnerId = agent.uniqueInstanceId };
                agent.ownedSites.Add(siteCoord);
            }
        }
    }

    public Dictionary<int2, int> Simulate()
    {
        for (int i = 0; i < simulationSteps && activeAgents.Count > 0; i++)
        {
            SimulateStep();
        }

        return siteGrid.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.OwnerId);
    }

    private void SimulateStep()
    {
        // Список сайтов, захваченных на этом шаге, для каждого агента
        var newlyCapturedSites = new Dictionary<int, List<int2>>();

        // 1. Фаза экспансии: каждый активный агент пытается захватить соседей
        foreach (var agent in activeAgents)
        {
            // Биомы в паре больше не расширяются в нейтральные зоны
            if (agent.isPaired) continue;

            List<int2> sitesToExpandFrom = new List<int2>(agent.ownedSites);
            foreach (var siteCoord in sitesToExpandFrom)
            {
                // Перемешиваем порядок проверки соседей, чтобы убрать параллельность
                Shuffle(neighborOffsets);

                foreach (var offset in neighborOffsets)
                {
                    int2 neighborCoord = siteCoord + offset;
                    
                    // Захватываем, только если ячейка свободна
                    if (!siteGrid.ContainsKey(neighborCoord))
                    {
                        siteGrid[neighborCoord] = new Site { OwnerId = agent.uniqueInstanceId };

                        if (!newlyCapturedSites.ContainsKey(agent.uniqueInstanceId))
                        {
                            newlyCapturedSites[agent.uniqueInstanceId] = new List<int2>();
                        }
                        newlyCapturedSites[agent.uniqueInstanceId].Add(neighborCoord);
                    }
                }
            }
        }

        // 2. Добавляем захваченные сайты к их владельцам
        foreach (var capture in newlyCapturedSites)
        {
            agents[capture.Key].ownedSites.UnionWith(capture.Value);
        }

        // 3. Фаза проверки контактов и обновления статусов
        List<BiomeAgent> agentsToRemove = new List<BiomeAgent>();
        foreach (var agent in activeAgents)
        {
            if (CheckForPairing(agent) || !CanExpand(agent))
            {
                agentsToRemove.Add(agent);
            }
        }
        
        // Удаляем из активных тех, кто нашел пару или кому больше некуда расти
        activeAgents.RemoveAll(a => agentsToRemove.Contains(a));
    }

    // Проверяет, может ли агент еще расширяться
    private bool CanExpand(BiomeAgent agent)
    {
        foreach (var siteCoord in agent.ownedSites)
        {
            foreach (var offset in neighborOffsets)
            {
                if (!siteGrid.ContainsKey(siteCoord + offset)) return true; // Нашли свободного соседа
            }
        }
        return false;
    }

    // Проверяет границы агента и формирует пару, если находит подходящего соседа
    private bool CheckForPairing(BiomeAgent agent)
    {
        if (agent.isPaired) return true;

        foreach (var siteCoord in agent.ownedSites)
        {
            foreach (var offset in neighborOffsets)
            {
                int2 neighborCoord = siteCoord + offset;
                if (siteGrid.TryGetValue(neighborCoord, out Site neighborSite) && neighborSite.OwnerId != agent.uniqueInstanceId)
                {
                    var otherAgent = agents[neighborSite.OwnerId];
                    if (!otherAgent.isPaired && agent.settings.biome.biomeID != otherAgent.settings.biome.biomeID)
                    {
                        agent.isPaired = true;
                        agent.partnerId = otherAgent.uniqueInstanceId;
                        otherAgent.isPaired = true;
                        otherAgent.partnerId = agent.uniqueInstanceId;
                        return true; // Агент нашел пару
                    }
                }
            }
        }
        return false;
    }

    // Простой алгоритм для перемешивания списка (Фишер-Йейтс)
    private void Shuffle(List<int2> list)
    {
        System.Random rng = new System.Random();
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            var value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }
}