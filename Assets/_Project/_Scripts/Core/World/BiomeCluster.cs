// Файл: BiomeCluster.cs
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;



// Кластер биомов
public class BiomeCluster
{
    // Узел (ядро) биома
    public class BiomeNode
    {
        public int id;
        public float2 position;
        public BiomeNode(int id, float2 position) { this.id = id; this.position = position; }
    }

    // Ребро, соединяющее два узла
    public class BiomeEdge
    {
        public int nodeA_id;
        public int nodeB_id;
        public BiomeEdge(int a, int b) { nodeA_id = a; nodeB_id = b; }
    }

    public BiomePlacementSettingsSO settings;
    public Dictionary<int, BiomeNode> nodes = new Dictionary<int, BiomeNode>();
    public List<BiomeEdge> edges = new List<BiomeEdge>();

    public float influenceRadius;
    public float contrast;
    public float coreRadiusPercentage;

    public BiomeCluster(BiomeNode initialNode, BiomeInstance sourceInstance)
    {
        settings = sourceInstance.settings;
        influenceRadius = sourceInstance.calculatedRadius;
        contrast = sourceInstance.calculatedContrast;
        coreRadiusPercentage = sourceInstance.coreRadiusPercentage;
        AddNode(initialNode);
    }
    
    public void AddNode(BiomeNode node)
    {
        if (!nodes.ContainsKey(node.id))
        {
            nodes.Add(node.id, node);
        }
    }

    public void AddEdge(BiomeEdge edge)
    {
        edges.Add(edge);
    }
}