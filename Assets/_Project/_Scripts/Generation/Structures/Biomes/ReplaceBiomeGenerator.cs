// ФАЙЛ: ReplaceBiomeGenerator.cs
using UnityEngine;

public struct ReplaceBiomeGenerator : IBiomeTerrainGenerator
{
    public int GetModifiedHeight(in BiomeInstanceBurst biome, int baseTerrainHeight, float dominantBiomeBaseHeight, in Vector2 worldPos, FastNoiseLite heightMapNoise)
    {
        float distanceToDominant = Vector2.Distance(worldPos, biome.position);
        float influence = GetFacetedPyramidInfluence(distanceToDominant, biome.influenceRadius, biome.tiers);
        float heightModification = biome.verticalDisplacementScale * biome.aggressiveness * influence;
        
        bool buildUp = dominantBiomeBaseHeight > baseTerrainHeight;
        if (biome.isInverted) buildUp = !buildUp;
        
        return buildUp ? (baseTerrainHeight + (int)heightModification) : (baseTerrainHeight - (int)heightModification);
    }

    // Вспомогательная функция для пирамиды теперь живет здесь
    private float GetFacetedPyramidInfluence(float distance, float radius, int tiers)
    {
        if (tiers <= 0) return 0f;
        float normDist = 1f - Mathf.Clamp01(distance / radius); 
        float stepSize = 1f / tiers;
        return Mathf.Ceil(normDist / stepSize) * stepSize;
    }
}