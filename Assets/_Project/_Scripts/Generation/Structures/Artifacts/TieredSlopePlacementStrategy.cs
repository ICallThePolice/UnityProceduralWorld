// Файл: TieredSlopePlacementStrategy.cs
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Стратегия, размещающая артефакты на склонах каждого яруса биома.
/// </summary>
public class TieredSlopePlacementStrategy : IArtifactPlacementStrategy
{
    public void Place(BiomeInstance parentBiome, RootArtifactConfig artifactConfig, System.Random random, List<ArtifactInstance> artifactListToFill, List<BiomeInstance> allBiomesInArea, BiomeManager manager)
    {
        var rootArtifactSO = artifactConfig.artifactSO;
        if (rootArtifactSO == null) return;

        // Цикл по ярусам для размещения корневых артефактов
        for (int tierIndex = 1; tierIndex <= parentBiome.calculatedTiers; tierIndex++)
        {
            var placedArtifactsOnThisTier = new List<Vector2>();
            // Ширина склона, на котором могут появляться артефакты (в процентах от радиуса яруса).
            float slopeWidth = 0.25f;
            float startRadiusNorm, endRadiusNorm;

            switch (tierIndex)
            {
                case 1: endRadiusNorm = parentBiome.calculatedTierRadii.z; break;
                case 2: endRadiusNorm = parentBiome.calculatedTierRadii.y; break;
                case 3: endRadiusNorm = parentBiome.calculatedTierRadii.x; break;
                default: continue;
            }
            startRadiusNorm = endRadiusNorm * (1.0f - slopeWidth);

            float startRadius = parentBiome.calculatedRadius * startRadiusNorm;
            float endRadius = parentBiome.calculatedRadius * endRadiusNorm;
            float ringArea = Mathf.PI * (endRadius * endRadius - startRadius * startRadius);
            int spawnAttempts = Mathf.RoundToInt(ringArea / manager.artifactDensityDivisor * (rootArtifactSO.spawnDensity));

            for (int i = 0; i < spawnAttempts; i++)
            {
                if (random.NextDouble() < rootArtifactSO.spawnChance)
                {
                    float angle = (float)random.NextDouble() * 360f;
                    float distance = Mathf.Lerp(startRadius, endRadius, (float)random.NextDouble());
                    Vector2 offset = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * distance;
                    Vector2 artifactPos = parentBiome.position + offset;

                    // --- ИСПРАВЛЕННАЯ ПРОВЕРКА НА ДОМИНИРОВАНИЕ ---
                    BiomeInstance dominantBiomeAtPos = manager.GetDominantBiomeAt(artifactPos, allBiomesInArea, out float influence);

                    // Пропускаем, только если здесь доминирует биом ДРУГОГО ТИПА.
                    // Пересечения с однотипными биомами теперь разрешены.
                    if (dominantBiomeAtPos != null && dominantBiomeAtPos.settings.biome != parentBiome.settings.biome)
                    {
                        continue;
                    }

                    float sizeMultiplier = Mathf.Lerp(rootArtifactSO.relativeSize.x, rootArtifactSO.relativeSize.y, (float)random.NextDouble());
                    float finalSize = parentBiome.calculatedRadius * sizeMultiplier;

                    bool isTooClose = false;
                    foreach (var placedPos in placedArtifactsOnThisTier)
                    {
                        if (Vector2.Distance(artifactPos, placedPos) < finalSize) { isTooClose = true; break; }
                    }
                    if (isTooClose) continue;

                    int groundHeight = manager.CalculateFinalHeightAtPoint(artifactPos, allBiomesInArea);
                    manager.CreateArtifactAndSpawnChildren(rootArtifactSO, artifactPos, finalSize, groundHeight, 0f, parentBiome, allBiomesInArea, random, artifactListToFill);

                    placedArtifactsOnThisTier.Add(artifactPos);
                }
            }
        }
    }
}