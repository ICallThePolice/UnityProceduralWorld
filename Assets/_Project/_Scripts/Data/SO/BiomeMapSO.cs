using UnityEngine;

[System.Serializable]
public struct BiomeMapping
{
    public BiomeDefinitionSO biome;
    [Tooltip("Позиция биома на карте. X = Хаос (-1 до 1), Y = Насыщенность (-1 до 1)")]
    public Vector2 position; 
}

[CreateAssetMenu(fileName = "BiomeMap", menuName = "Procedural Voxel World/Biome Map")]
public class BiomeMapSO : ScriptableObject
{
    [Tooltip("Список всех биомов и их центральные точки на карте Хаоса/Насыщенности")]
    public BiomeMapping[] biomeMappings;

    public BiomeDefinitionSO GetBiome(float chaos, float saturation)
    {
        if (biomeMappings == null || biomeMappings.Length == 0) return null;

        BiomeMapping closestBiome = biomeMappings[0];
        float minDistanceSq = float.MaxValue;
        Vector2 targetPosition = new Vector2(chaos, saturation);

        foreach (var mapping in biomeMappings)
        {
            if (mapping.biome == null) continue; // Проверка на пустой слот
            float distSq = Vector2.SqrMagnitude(mapping.position - targetPosition);
            if (distSq < minDistanceSq)
            {
                minDistanceSq = distSq;
                closestBiome = mapping;
            }
        }
        return closestBiome.biome;
    }
}