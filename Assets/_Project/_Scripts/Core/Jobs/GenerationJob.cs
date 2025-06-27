using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct GenerationJob : IJob
{
    // --- ВХОДНЫЕ ДАННЫЕ (ReadOnly) ---
    [ReadOnly] public Vector3Int chunkPosition;
    [ReadOnly] public FastNoiseLite heightMapNoise;
    [ReadOnly] public NativeArray<BiomeInstanceBurst> biomeInstances;
    [ReadOnly] public NativeArray<OverlayPlacementDataBurst> overlayPlacements;
    [ReadOnly] public NativeArray<VoxelTypeDataBurst> voxelTypeMap;
    [ReadOnly] public NativeArray<VoxelOverlayDataBurst> voxelOverlayMap;
    [ReadOnly] public float2 atlasTileSize;

    // --- ВЫХОДНЫЕ ДАННЫЕ (WriteOnly) ---
    [WriteOnly] public NativeArray<ushort> primaryBlockIDs;
    [WriteOnly] public NativeArray<Color32> finalColors;
    [WriteOnly] public NativeArray<float2> finalUv0s;
    [WriteOnly] public NativeArray<float2> finalUv1s;
    [WriteOnly] public NativeArray<float> finalTexBlends;
    [WriteOnly] public NativeArray<float4> finalEmissionData;
    [WriteOnly] public NativeArray<float4> finalGapColors;
    [WriteOnly] public NativeArray<float2> finalMaterialProps;
    [WriteOnly] public NativeArray<float> finalGapWidths;
    [WriteOnly] public NativeArray<float3> finalBevelData;

    public void Execute()
    {
        for (int x = 0; x < Chunk.Width; x++)
            for (int z = 0; z < Chunk.Width; z++)
            {
                int worldX = chunkPosition.x * Chunk.Width + x;
                int worldZ = chunkPosition.z * Chunk.Width + z;
                float2 worldPos = new float2(worldX, worldZ);

                // 1. Рассчитываем базовую высоту
                float heightValue = heightMapNoise.GetNoise(worldX, worldZ);
                int surfaceHeight = (int)math.remap(-1, 1, 0, Chunk.Height, heightValue);

                // 2. Находим базовый VoxelID (например, из самого влиятельного биома)
                ushort baseVoxelID = GetBaseVoxelID(worldPos);
                VoxelTypeDataBurst baseVoxelData = voxelTypeMap[baseVoxelID];

                // 3. Находим самый влиятельный оверлей в этой точке
                ushort overlayID = GetDominantOverlayID(worldPos);
                VoxelOverlayDataBurst overlayData = overlayID > 0 ? voxelOverlayMap[overlayID] : default;

                // 4. Вычисляем итоговые смешанные свойства
                //    ВАША ЗАДАЧА: реализовать логику внутри этих функций
                Color32 finalColor = CalculateFinalColor(baseVoxelData, overlayData);
                float2 uv0 = baseVoxelData.baseUV / atlasTileSize;
                float2 uv1 = overlayData.overlayUV / atlasTileSize;
                float blend = CalculateTextureBlend(baseVoxelData, overlayData);
                float4 emission = overlayData.emissionData; // Упрощенно, можно смешивать
                                                            // ... и так далее для всех `final...` свойств

                for (int y = 0; y < Chunk.Height; y++)
                {
                    int voxelIndex = Chunk.GetVoxelIndex(x, y, z);
                    if (y < surfaceHeight)
                    {
                        primaryBlockIDs[voxelIndex] = baseVoxelID; // или ID оверлея, если он заменяет блок
                        finalColors[voxelIndex] = finalColor;
                        finalUv0s[voxelIndex] = uv0;
                        finalUv1s[voxelIndex] = uv1;
                        finalTexBlends[voxelIndex] = blend;
                        finalEmissionData[voxelIndex] = emission;
                        finalGapColors[voxelIndex] = new float4(overlayData.gapColor.r / 255f, overlayData.gapColor.g / 255f, overlayData.gapColor.b / 255f, overlayData.gapColor.a / 255f);
                        finalMaterialProps[voxelIndex] = overlayData.materialProps;
                        finalGapWidths[voxelIndex] = overlayData.gapWidth;
                        finalBevelData[voxelIndex] = overlayData.bevelData;
                    }
                    else
                    {
                        // Заполняем воздухом
                        primaryBlockIDs[voxelIndex] = 0;
                    }
                }
            }
    }
    
    // ВАША ЗАДАЧА: Реализовать эти методы с вашей логикой
    private ushort GetBaseVoxelID(float2 worldPos) { /*...*/ return 1; } // Возвращает ID камня/земли
    private ushort GetDominantOverlayID(float2 worldPos) { /*...*/ return 0; } // Возвращает ID руды/мха
    private Color32 CalculateFinalColor(VoxelTypeDataBurst baseData, VoxelOverlayDataBurst overlayData) { return baseData.baseColor; }
    private float CalculateTextureBlend(VoxelTypeDataBurst baseData, VoxelOverlayDataBurst overlayData) { return 0f; }
    // ... и так далее для других сложных свойств ...
}