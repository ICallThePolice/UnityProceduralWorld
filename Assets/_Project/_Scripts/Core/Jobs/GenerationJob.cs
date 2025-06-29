// --- ФАЙЛ: GenerationJob.cs (ВОЗВРАЩАЕМ ОРИГИНАЛЬНУЮ ЛОГИКУ) ---
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct GenerationJob : IJob
{
    [ReadOnly] public Vector3Int chunkPosition;
    [ReadOnly] public FastNoiseLite heightMapNoise;
    
    // ВХОДНЫЕ КАРТЫ ДЛЯ ЧАНКА
    [ReadOnly] public NativeArray<ushort> chunkPrimaryIdMap; 
    [ReadOnly] public NativeArray<ushort> chunkSecondaryIdMap;
    [ReadOnly] public NativeArray<float> chunkBlendMap;

    [ReadOnly] public NativeArray<VoxelTypeDataBurst> voxelTypeMap;
    [ReadOnly] public ushort globalBiomeBlockID;
    
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
        {
            for (int z = 0; z < Chunk.Width; z++)
            {
                int mapIndex = x + z * Chunk.Width;
                ushort primaryID = chunkPrimaryIdMap[mapIndex];
                ushort secondaryID = chunkSecondaryIdMap[mapIndex];
                float blendFactor = chunkBlendMap[mapIndex];

                if (primaryID == 0) primaryID = globalBiomeBlockID;
                if (secondaryID == 0) secondaryID = primaryID; // Если второй биом - нейтральная зона, считаем его тем же, что и основной

                VoxelTypeDataBurst primaryProps = voxelTypeMap[primaryID];
                VoxelTypeDataBurst secondaryProps = voxelTypeMap[secondaryID];
                
                var worldPos = new float2(chunkPosition.x * Chunk.Width + x, chunkPosition.z * Chunk.Width + z);
                float heightValue = heightMapNoise.GetNoise(worldPos.x, worldPos.y);
                int surfaceHeight = (int)math.remap(-1, 1, 10, Chunk.Height - 10, heightValue);
                
                for (int y = 0; y < Chunk.Height; y++)
                {
                    int voxelIndex = Chunk.GetVoxelIndex(x, y, z);
                    if (y >= surfaceHeight) { 
                        primaryBlockIDs[voxelIndex] = 0;
                        continue; 
                    }

                    // --- ЗАПОЛНЯЕМ ДАННЫЕ С УЧЕТОМ СМЕШИВАНИЯ ДЛЯ ШЕЙДЕРА ---
                    primaryBlockIDs[voxelIndex] = primaryProps.id;
                    finalColors[voxelIndex] = Color.Lerp(primaryProps.baseColor, secondaryProps.baseColor, blendFactor);
                    finalUv0s[voxelIndex] = primaryProps.baseUV;
                    finalUv1s[voxelIndex] = secondaryProps.baseUV;
                    finalTexBlends[voxelIndex] = blendFactor;
                    finalGapColors[voxelIndex] = ToFloat4(Color32.Lerp(primaryProps.gapColor, secondaryProps.gapColor, blendFactor));
                    finalGapWidths[voxelIndex] = math.lerp(primaryProps.gapWidth, secondaryProps.gapWidth, blendFactor);
                    finalBevelData[voxelIndex] = math.lerp(primaryProps.bevelData, secondaryProps.bevelData, blendFactor);
                    
                    // Оверлеи и эмиттеры пока оставим без смешивания для простоты
                    finalEmissionData[voxelIndex] = float4.zero; 
                    finalMaterialProps[voxelIndex] = new float2(0.1f, 0f);
                }
            }
        }
    }
    
    private float4 ToFloat4(Color32 c)
    {
        return new float4(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
    }
}