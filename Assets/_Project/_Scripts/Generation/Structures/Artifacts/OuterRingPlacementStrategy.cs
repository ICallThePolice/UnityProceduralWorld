#if false// Файл: OuterRingPlacementStrategy.cs
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Стратегия, размещающая артефакты в кольце СНАРУЖИ от основного радиуса биома.
/// </summary>
public class OuterRingPlacementStrategy : IArtifactPlacementStrategy
{
    public void Place(BiomeInstance parentBiome, RootArtifactConfig artifactConfig, System.Random random, List<ArtifactInstance> artifactListToFill, List<BiomeInstance> allBiomesInArea, BiomeManager manager)
    {
        var rootArtifactSO = artifactConfig.artifactSO;
        if (rootArtifactSO == null) return;

        // --- 1. Определяем кольцо для размещения ЗА ПРЕДЕЛАМИ биома ---
        // Эти значения можно вынести в настройки, если потребуется.
        // Сейчас они означают "от 110% до 150% радиуса родителя".
        float startRadius = parentBiome.calculatedRadius * 1.1f;
        float endRadius = parentBiome.calculatedRadius * 1.5f;

        // --- 2. Рассчитываем количество попыток спавна по новой, единой формуле ---
        float ringArea = Mathf.PI * (endRadius * endRadius - startRadius * startRadius);
        int spawnAttempts = Mathf.RoundToInt(ringArea / manager.artifactDensityDivisor * (rootArtifactSO.spawnDensity));

        if (spawnAttempts == 0) return;

        // --- 3. Цикл попыток размещения ---
        for (int i = 0; i < spawnAttempts; i++)
        {
            if (random.NextDouble() < rootArtifactSO.spawnChance)
            {
                // Находим случайную точку в нашем внешнем кольце
                float angle = (float)random.NextDouble() * 360f;
                float distance = Mathf.Lerp(startRadius, endRadius, (float)random.NextDouble());
                Vector2 offset = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * distance;
                Vector2 artifactPos = parentBiome.position + offset;

                // --- 4. Расчет высоты по базовому ландшафту (важно для нейтральной зоны) ---
                // Так как мы находимся в нейтральной зоне, мы не используем сложный CalculateFinalHeightAtPoint,
                // а берем высоту напрямую из базового шума высот.
                float baseHeightNoise = manager.GetHeightmapNoiseValue(artifactPos);
                int groundHeight = 5 + Mathf.RoundToInt(((baseHeightNoise + 1f) / 2f) * 20f);
                
                // Рассчитываем финальный размер
                float sizeMultiplier = Mathf.Lerp(rootArtifactSO.relativeSize.x, rootArtifactSO.relativeSize.y, (float)random.NextDouble());
                float finalSize = parentBiome.calculatedRadius * sizeMultiplier;
                
                // --- 5. Вызываем главный метод BiomeManager для создания артефакта и его детей ---
                // Y-смещение для корневых артефактов всегда 0, так как их высота уже определена.
                manager.CreateArtifactAndSpawnChildren(rootArtifactSO, artifactPos, finalSize, groundHeight, 0f, parentBiome, allBiomesInArea, random, artifactListToFill);
            }
        }
    }
}
#endif