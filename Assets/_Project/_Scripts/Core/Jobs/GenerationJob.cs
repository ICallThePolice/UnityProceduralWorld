using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;

// GenerationJob.cs

[BurstCompile]
public struct GenerationJob : IJob
{
    // --- Входные данные (без изменений) ---
    public Vector3Int chunkPosition;
    public FastNoiseLite heightMapNoise;
    [ReadOnly] public NativeArray<BiomeInstanceBurst> biomeInstances;
    [ReadOnly] public ushort globalBiomeBlockID;
    [ReadOnly] public NativeArray<VoxelCategory> voxelIdToCategoryMap;

    // --- Выходные данные (без изменений) ---
    public NativeArray<ushort> primaryBlockIDs;
    public NativeArray<ushort> secondaryBlockIDs;
    public NativeArray<float> blendFactors;

    public void Execute()
    {
        for (int x = 0; x < Chunk.Width; x++)
        {
            for (int z = 0; z < Chunk.Width; z++)
            {
                int worldX = chunkPosition.x * Chunk.Width + x;
                int worldZ = chunkPosition.z * Chunk.Width + z;

                // --- Этап 1: Рассчитываем базовую высоту ландшафта ---
                float heightValue = heightMapNoise.GetNoise(worldX, worldZ);
                int surfaceHeight = (int)math.remap(-1, 1, 0, Chunk.Height, heightValue);

                // --- Этап 2: Находим два самых влиятельных биома в этой точке ---
                int strongestBiomeIndex = -1;
                int secondStrongestBiomeIndex = -1;
                float maxInfluence = 0f;
                float secondMaxInfluence = 0f;

                for (int i = 0; i < biomeInstances.Length; i++)
                {
                    var currentBiome = biomeInstances[i]; // Для удобства
                    float dist = math.distance(new float2(worldX, worldZ), currentBiome.position);

                    if (dist > currentBiome.influenceRadius) continue;

                    float influence;
                    float normalizedDist = dist / currentBiome.influenceRadius;

                    // Проверяем, находимся ли мы внутри "ядра" биома
                    if (normalizedDist < currentBiome.coreRadiusPercentage)
                    {
                        // Если да, то влияние максимально (100%), смешивания нет.
                        influence = 1.0f;
                    }
                    else
                    {
                        // Если нет, мы находимся в "зоне смешивания".
                        // Нам нужно пересчитать наше положение от 0 до 1 внутри этой зоны.
                        float blendZoneWidth = 1.0f - currentBiome.coreRadiusPercentage;

                        // Защита от деления на ноль, если ядро занимает 100%
                        if (blendZoneWidth < 0.0001f)
                        {
                            influence = 0f; // Мы за пределами ядра, значит, и за пределами биома
                        }
                        else
                        {
                            // Насколько далеко мы зашли в зону смешивания
                            float distIntoBlendZone = normalizedDist - currentBiome.coreRadiusPercentage;

                            // Нормализуем это расстояние (от 0 до 1)
                            float t = distIntoBlendZone / blendZoneWidth;

                            // Теперь применяем нашу формулу резкости к этому значению 't'
                            float sharpnessExponent = 1.0f + currentBiome.sharpness * 15.0f;
                            influence = math.pow(1.0f - t, sharpnessExponent);
                        }
                    }

                    if (influence > maxInfluence)
                    {
                        secondMaxInfluence = maxInfluence;
                        secondStrongestBiomeIndex = strongestBiomeIndex;
                        maxInfluence = influence;
                        strongestBiomeIndex = i;
                    }
                    else if (influence > secondMaxInfluence)
                    {
                        secondMaxInfluence = influence;
                        secondStrongestBiomeIndex = i;
                    }
                }

                // --- Этап 3: ИНТЕЛЛЕКТУАЛЬНОЕ ОПРЕДЕЛЕНИЕ БЛОКОВ И СМЕШИВАНИЯ ---
                ushort primaryBlockId;
                ushort secondaryBlockId;
                float blendFactor;

                if (strongestBiomeIndex == -1)
                {
                    // Если никакие биомы не влияют, используем нейтральный ландшафт
                    primaryBlockId = globalBiomeBlockID;
                    secondaryBlockId = 0;
                    blendFactor = 0;
                }
                else
                {
                    // Основной блок - это блок самого сильного биома
                    primaryBlockId = biomeInstances[strongestBiomeIndex].blockID;

                    // Определяем, что будет "фоном" для смешивания:
                    // либо второй по силе биом, либо нейтральный ландшафт
                    float neutralLandscapeInfluence = 1.0f - maxInfluence;
                    ushort backgroundBlockId = globalBiomeBlockID;
                    if (secondStrongestBiomeIndex != -1 && secondMaxInfluence > neutralLandscapeInfluence)
                    {
                        backgroundBlockId = biomeInstances[secondStrongestBiomeIndex].blockID;
                    }
                    
                    // Получаем категории основного блока и фона
                    VoxelCategory primaryCategory = voxelIdToCategoryMap[primaryBlockId];
                    VoxelCategory backgroundCategory = voxelIdToCategoryMap[backgroundBlockId];

                    // Вызываем нашу функцию с правилами, чтобы решить, смешивать ли их
                    if (CanBiomesBlend(primaryCategory, backgroundCategory))
                    {
                        // РАЗРЕШАЕМ СМЕШИВАНИЕ
                        secondaryBlockId = backgroundBlockId;
                        if (secondStrongestBiomeIndex != -1 && secondMaxInfluence > neutralLandscapeInfluence)
                        {
                            blendFactor = secondMaxInfluence / (maxInfluence + secondMaxInfluence);
                        }
                        else
                        {
                            blendFactor = neutralLandscapeInfluence;
                        }
                    }
                    else
                    {
                        // ЗАПРЕЩАЕМ СМЕШИВАНИЕ (например, Руда и Растение)
                        secondaryBlockId = 0;
                        blendFactor = 0;
                    }
                }

                // --- Этап 4: Заполняем колонку вокселей вычисленными данными (без изменений) ---
                for (int y = 0; y < Chunk.Height; y++)
                {
                    int voxelIndex = Chunk.GetVoxelIndex(x, y, z);
                    if (y < surfaceHeight)
                    {
                        primaryBlockIDs[voxelIndex] = primaryBlockId;
                        secondaryBlockIDs[voxelIndex] = secondaryBlockId;
                        blendFactors[voxelIndex] = blendFactor;
                    }
                    else
                    {
                        primaryBlockIDs[voxelIndex] = 0;
                        secondaryBlockIDs[voxelIndex] = 0;
                        blendFactors[voxelIndex] = 0;
                    }
                }
            }
        }
    }

    private bool CanBiomesBlend(VoxelCategory cat1, VoxelCategory cat2)
    {
        // ПРАВИЛО №1: Объекты одной категории всегда смешиваются
        if (cat1 == cat2)
        {
            return true;
        }

        // ПРАВИЛО №2: Разрешаем Руде (Ore) и Кристаллам (Crystal) смешиваться с Ландшафтом (Landscape)
        if ((cat1 == VoxelCategory.Landscape && (cat2 == VoxelCategory.Ore || cat2 == VoxelCategory.Crystal)) ||
            (cat2 == VoxelCategory.Landscape && (cat1 == VoxelCategory.Ore || cat1 == VoxelCategory.Crystal)))
        {
            return true;
        }

        // Во всех остальных случаях - запрещаем смешивание (например, Постройки и Руда)
        return false;
    }
}