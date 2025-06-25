#if false// --- ФАЙЛ: SubtractiveBiomeGenerator.cs (АРХИТЕКТУРА "ВЛОЖЕННЫЕ ЧАШИ") ---
using UnityEngine;

public struct SubtractiveBiomeGenerator : IBiomeTerrainGenerator
{
    public int GetModifiedHeight(in BiomeInstanceBurst biome, int baseTerrainHeight, float dominantBiomeBaseHeight, in Vector2 worldPos, FastNoiseLite heightMapNoise)
    {
        // --- 1. Базовые расчеты ---

        // Максимально возможная глубина для этого биома.
        float totalAvailableDepth = biome.verticalDisplacementScale * biome.aggressiveness;
        // Расстояние от текущей точки до центра биома, нормализованное от 0 (в центре) до 1 (на краю).
        float normDist = Vector2.Distance(worldPos, biome.position) / biome.influenceRadius;
        normDist = Mathf.Clamp01(normDist);

        float finalDepth = 0;

        // --- 2. Логика в зависимости от количества ярусов (Тиров) ---

        if (biome.tiers <= 1)
        {
            // --- ОДНОЯРУСНЫЙ КРАТЕР (глубина 1/3) ---
            float targetDepth = totalAvailableDepth / 3.0f;
            // Создаем одну плавную чашу от края до центра.
            // Инвертируем normDist, чтобы получить 1 в центре и 0 на краю.
            float influence = 1.0f - normDist;
            finalDepth = targetDepth * SmoothStep(influence);
        }
        else if (biome.tiers == 2)
        {
            // --- ДВУХЪЯРУСНЫЙ КРАТЕР (глубина 2/3) ---
            float depthPerTier = totalAvailableDepth / 3.0f;
            float tier1_outerRadius = biome.tierRadii.z;
            float tier2_outerRadius = biome.tierRadii.y;

            if (normDist > tier2_outerRadius) // Мы находимся на первом, внешнем ярусе
            {
                // Пересчитываем позицию внутри этого кольца (от 0 до 1).
                float t = (normDist - tier2_outerRadius) / (tier1_outerRadius - tier2_outerRadius);
                // Плавно опускаемся от 0 до глубины первого яруса.
                finalDepth = depthPerTier * SmoothStep(1.0f - t);
            }
            else // Мы находимся на втором, внутреннем ярусе
            {
                // Начинаем с полной глубины первого яруса...
                float baseDepth = depthPerTier;
                // ...и добавляем второе углубление.
                float t = normDist / tier2_outerRadius;
                finalDepth = baseDepth + (depthPerTier * SmoothStep(1.0f - t));
            }
        }
        else // biome.tiers >= 3
        {
            // --- ТРЕХЪЯРУСНЫЙ КРАТЕР (полная глубина) ---
            float depthPerTier = totalAvailableDepth / 3.0f;
            // Определяем радиусы для трех ярусов.
            float tier1_outerRadius = biome.tierRadii.z;
            float tier2_outerRadius = biome.tierRadii.y;
            float tier3_outerRadius = biome.tierRadii.x;

            if (normDist > tier2_outerRadius) // Внешний ярус
            {
                float t = (normDist - tier2_outerRadius) / (tier1_outerRadius - tier2_outerRadius);
                finalDepth = depthPerTier * SmoothStep(1.0f - t);
            }
            else if (normDist > tier3_outerRadius) // Средний ярус
            {
                float baseDepth = depthPerTier; // Начинаем с глубины первого яруса
                float t = (normDist - tier3_outerRadius) / (tier2_outerRadius - tier3_outerRadius);
                finalDepth = baseDepth + (depthPerTier * SmoothStep(1.0f - t));
            }
            else // Внутренний ярус (самое дно)
            {
                float baseDepth = depthPerTier * 2; // Начинаем с глубины второго яруса
                float t = normDist / tier3_outerRadius;
                finalDepth = baseDepth + (depthPerTier * SmoothStep(1.0f - t));
            }
        }

        // Возвращаем финальную абсолютную высоту.
        return (int)(dominantBiomeBaseHeight - finalDepth);
    }

    /// <summary>
    /// Вспомогательная функция для создания плавного перехода (кривая "Smoothstep").
    /// </summary>
    private float SmoothStep(float t)
    {
        // t должен быть в диапазоне от 0 до 1.
        return t * t * (3f - 2f * t);
    }
}
#endif