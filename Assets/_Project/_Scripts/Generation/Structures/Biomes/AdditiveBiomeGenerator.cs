// ФАЙЛ: AdditiveBiomeGenerator.cs
using UnityEngine;

public struct AdditiveBiomeGenerator : IBiomeTerrainGenerator
{
    public int GetModifiedHeight(in BiomeInstanceBurst biome, int baseTerrainHeight, float dominantBiomeBaseHeight, in Vector2 worldPos, FastNoiseLite heightMapNoise)
    {
        // Для аддитивного биома нам нужен maxInfluence, который не передается.
        // Поэтому мы пересчитаем его здесь по упрощенной формуле.
        float distance = Vector2.Distance(worldPos, biome.position);
        float influence = 1f - (distance / biome.influenceRadius);
        influence = Mathf.Pow(influence, biome.contrast);
        
        float heightModification = biome.verticalDisplacementScale * biome.aggressiveness * influence;
        return baseTerrainHeight + (int)heightModification;
    }
}