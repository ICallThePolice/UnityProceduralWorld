using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public struct GenerationJob : IJob
{
    [ReadOnly] public Vector3Int chunkPosition;
    [ReadOnly] public FastNoiseLite heightMapNoise;
    [ReadOnly] public NativeArray<BiomeInstanceBurst> biomeInstances;
    [ReadOnly] public NativeArray<OverlayPlacementDataBurst> overlayPlacements;
    [ReadOnly] public ushort globalBiomeBlockID;

    // --- ВЫХОДНЫЕ ДАННЫЕ УПРОЩЕНЫ ---
    [WriteOnly] public NativeArray<ushort> primaryBlockIDs;
    [WriteOnly] public NativeArray<ushort> overlayIDs;

    public void Execute()
    {
        for (int x = 0; x < Chunk.Width; x++)
        for (int z = 0; z < Chunk.Width; z++)
        {
            int worldX = chunkPosition.x * Chunk.Width + x;
            int worldZ = chunkPosition.z * Chunk.Width + z;
            float2 worldPos = new float2(worldX, worldZ);
            
            float heightValue = heightMapNoise.GetNoise(worldX, worldZ);
            int surfaceHeight = (int)math.remap(-1, 1, 10, Chunk.Height - 5, heightValue);

            // Просто определяем доминирующий ID блока и ID оверлея
            ushort baseVoxelID = GetStrongestBiomeBlockID(worldPos);
            ushort overlayID = GetDominantOverlayID(worldPos, out _);

            for (int y = 0; y < Chunk.Height; y++)
            {
                int voxelIndex = Chunk.GetVoxelIndex(x, y, z);
                if (y < surfaceHeight)
                {
                    primaryBlockIDs[voxelIndex] = baseVoxelID;
                    overlayIDs[voxelIndex] = overlayID;
                }
                else
                {
                    primaryBlockIDs[voxelIndex] = 0; // Air
                    overlayIDs[voxelIndex] = 0;
                }
            }
        }
    }
    
    private ushort GetStrongestBiomeBlockID(float2 worldPos)
    {
        float maxInfluence = -1f;
        int strongestBiomeIndex = -1;

        for (int i = 0; i < biomeInstances.Length; i++)
        {
            var biome = biomeInstances[i];
            float dist = math.distance(worldPos, biome.position);
            if (dist > biome.influenceRadius) continue;
            
            float influence = 1.0f - (dist / biome.influenceRadius);
            
            if (influence > maxInfluence)
            {
                maxInfluence = influence;
                strongestBiomeIndex = i;
            }
        }

        return (strongestBiomeIndex != -1) ? biomeInstances[strongestBiomeIndex].blockID : globalBiomeBlockID;
    }

    private ushort GetDominantOverlayID(float2 worldPos, out float maxInfluence)
    {
        maxInfluence = 0f;
        ushort dominantID = 0;
        
        for (int i = 0; i < overlayPlacements.Length; i++)
        {
            var overlay = overlayPlacements[i];
            float dist = math.distance(worldPos, overlay.position);
            if (dist > overlay.radius) continue;

            float influence = 1.0f - (dist / overlay.radius);
            influence = math.pow(influence, overlay.blendSharpness * 4.0f + 1.0f);

            if (influence > maxInfluence)
            {
                maxInfluence = influence;
                dominantID = overlay.overlayID;
            }
        }
        return dominantID;
    }
}