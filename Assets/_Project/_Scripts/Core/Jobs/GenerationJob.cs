using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public struct GenerationJob : IJob
{
    // --- Входные данные (соответствуют VoxelGenerationPipeline) ---
    [ReadOnly] public Vector3Int chunkPosition;
    [ReadOnly] public FastNoiseLite heightMapNoise;
    [ReadOnly] public NativeArray<BiomeInstanceBurst> biomeInstances;
    [ReadOnly] public NativeArray<OverlayPlacementDataBurst> overlayPlacements;

    // Карты для поиска свойств по ID
    [ReadOnly] public NativeArray<VoxelTypeDataBurst> voxelTypeMap;
    [ReadOnly] public NativeArray<VoxelOverlayDataBurst> voxelOverlayMap;
    
    // Глобальные настройки
    [ReadOnly] public ushort globalBiomeBlockID;
    [ReadOnly] public float2 atlasSizeInTiles;

    // --- Выходные данные (финальные атрибуты для вершин) ---
    [WriteOnly] public NativeArray<ushort> primaryBlockIDs;     // ID основного блока (для мешинга)
    [WriteOnly] public NativeArray<Color32> finalColors;
    [WriteOnly] public NativeArray<float2> finalUv0s;
    [WriteOnly] public NativeArray<float2> finalUv1s;
    [WriteOnly] public NativeArray<float> finalTexBlends;
    [WriteOnly] public NativeArray<float4> finalEmissionData;
    [WriteOnly] public NativeArray<float4> finalGapColors;
    [WriteOnly] public NativeArray<float2> finalMaterialProps;
    [WriteOnly] public NativeArray<float> finalGapWidths;
    [WriteOnly] public NativeArray<float3> finalBevelData;

    // Внутренняя структура для хранения временных смешанных свойств
    private struct BlendedProperties
    {
        public VoxelTypeDataBurst landscapeData; // Свойства ландшафта после смешивания биомов
        public float landscapeBlendFactor;
        public VoxelOverlayDataBurst overlayData; // Свойства доминантного объекта
        public float overlayInfluence;
    }


    public void Execute()
    {
        for (int x = 0; x < Chunk.Width; x++)
        {
            for (int z = 0; z < Chunk.Width; z++)
            {
                var worldPos = new float2(chunkPosition.x * Chunk.Width + x, chunkPosition.z * Chunk.Width + z);
                BlendedProperties props = GetBlendedPropertiesForColumn(worldPos);

                float heightValue = heightMapNoise.GetNoise(worldPos.x, worldPos.y);
                int surfaceHeight = (int)math.remap(-1, 1, 10, Chunk.Height - 10, heightValue);
                
                for (int y = 0; y < Chunk.Height; y++)
                {
                    int voxelIndex = Chunk.GetVoxelIndex(x, y, z);
                    
                    if (y >= surfaceHeight)
                    {
                        primaryBlockIDs[voxelIndex] = 0;
                        continue;
                    }
                    
                    primaryBlockIDs[voxelIndex] = props.landscapeData.id;
                    finalColors[voxelIndex] = Color.Lerp(props.landscapeData.baseColor, props.overlayData.tintColor, props.overlayInfluence);
                    finalUv0s[voxelIndex] = props.landscapeData.baseUV; // UV теперь не делим здесь
                    finalUv1s[voxelIndex] = props.overlayData.id > 0 ? props.overlayData.overlayUV : float2.zero;
                    finalTexBlends[voxelIndex] = props.overlayInfluence;
                    finalEmissionData[voxelIndex] = math.lerp(float4.zero, props.overlayData.emissionData, props.overlayInfluence);
                    
                    float4 landscapeGapColor = new float4(props.landscapeData.gapColor.r / 255f, props.landscapeData.gapColor.g / 255f, props.landscapeData.gapColor.b / 255f, 1);
                    float4 overlayGapColor = new float4(props.overlayData.gapColor.r / 255f, props.overlayData.gapColor.g / 255f, props.overlayData.gapColor.b / 255f, 1);
                    finalGapColors[voxelIndex] = math.lerp(landscapeGapColor, overlayGapColor, props.overlayInfluence);
                    
                    finalGapWidths[voxelIndex] = math.lerp(props.landscapeData.gapWidth, props.overlayData.gapWidth, props.overlayInfluence);
                    finalBevelData[voxelIndex] = math.lerp(props.landscapeData.bevelData, props.overlayData.bevelData, props.overlayInfluence);
                    finalMaterialProps[voxelIndex] = math.lerp(new float2(0.1f, 0f), props.overlayData.materialProps, props.overlayInfluence);
                }
            }
        }
    }

    private BlendedProperties GetBlendedPropertiesForColumn(float2 worldPos)
    {
        int strongestBiomeIndex = -1, secondStrongestBiomeIndex = -1;
        float maxInfluence = 0f, secondMaxInfluence = 0f;

        for (int i = 0; i < biomeInstances.Length; i++)
        {
            float dist = math.distance(worldPos, biomeInstances[i].position);
            if (dist > biomeInstances[i].influenceRadius) continue;
            float influence = 1.0f - (dist / biomeInstances[i].influenceRadius);
            influence = math.pow(influence, biomeInstances[i].contrast);

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

        VoxelTypeDataBurst primaryLandscape, secondaryLandscape;
        float landscapeBlendFactor;

        if (strongestBiomeIndex != -1)
        {
            primaryLandscape = voxelTypeMap[biomeInstances[strongestBiomeIndex].blockID];
            if (secondStrongestBiomeIndex != -1)
            {
                secondaryLandscape = voxelTypeMap[biomeInstances[secondStrongestBiomeIndex].blockID];
                landscapeBlendFactor = secondMaxInfluence / (maxInfluence + secondMaxInfluence + 0.0001f);
            }
            else
            {
                secondaryLandscape = voxelTypeMap[globalBiomeBlockID];
                landscapeBlendFactor = 1.0f - maxInfluence;
            }
        }
        else
        {
            primaryLandscape = voxelTypeMap[globalBiomeBlockID];
            secondaryLandscape = primaryLandscape;
            landscapeBlendFactor = 0;
        }

        VoxelTypeDataBurst blendedLandscape = LerpVoxelType(primaryLandscape, secondaryLandscape, landscapeBlendFactor);

        int dominantOverlayIndex = -1;
        int maxPriority = -1;
        float overlayInfluence = 0f;

        for (int i = 0; i < overlayPlacements.Length; i++)
        {
            var placement = overlayPlacements[i];
            float dist = math.distance(worldPos, placement.position);
            if (dist > placement.radius) continue;

            var overlayProps = voxelOverlayMap[placement.overlayID];
            if (overlayProps.priority > maxPriority)
            {
                maxPriority = overlayProps.priority;
                dominantOverlayIndex = i;
            }
        }
        
        VoxelOverlayDataBurst dominantOverlay = default;
        if (dominantOverlayIndex != -1)
        {
            var placement = overlayPlacements[dominantOverlayIndex];
            dominantOverlay = voxelOverlayMap[placement.overlayID];
            float dist = math.distance(worldPos, placement.position);
            overlayInfluence = 1.0f - (dist / placement.radius);
            overlayInfluence = math.pow(overlayInfluence, placement.blendSharpness * 4.0f + 1.0f);
        }

        return new BlendedProperties
        {
            landscapeData = blendedLandscape,
            overlayData = dominantOverlay,
            overlayInfluence = overlayInfluence
        };
    }

    // Вспомогательная функция для линейной интерполяции свойств VoxelTypeDataBurst
    private VoxelTypeDataBurst LerpVoxelType(VoxelTypeDataBurst a, VoxelTypeDataBurst b, float t)
    {
        if (t <= 0.001f) return a;
        if (t >= 0.999f) return b;
        
        return new VoxelTypeDataBurst {
            id = a.id,
            isSolid = a.isSolid,
            baseColor = Color32.Lerp(a.baseColor, b.baseColor, t),
            baseUV = a.baseUV,
            gapWidth = math.lerp(a.gapWidth, b.gapWidth, t),
            gapColor = Color32.Lerp(a.gapColor, b.gapColor, t),
            bevelData = math.lerp(a.bevelData, b.bevelData, t)
        };
    }
}