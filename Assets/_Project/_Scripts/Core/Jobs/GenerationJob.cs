using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[BurstCompile]
public struct GenerationJob : IJob
{
    [ReadOnly] public Vector3Int chunkPosition;
    [ReadOnly] public FastNoiseLite heightMapNoise;
    [ReadOnly] public BiomeInstanceBurst neutralBiome;
    [ReadOnly] public NativeArray<BiomeInstanceBurst> biomeInstances;
    [ReadOnly] public NativeArray<Color32> voxelIdToColorMap;
    
    public NativeArray<ushort> resultingVoxelIDs;
    public NativeArray<Color32> resultingVoxelColors;

    private struct VoxelBiomeInfo
    {
        public ushort biomeID;
        public ushort blockID;
    }
    /*private Color32 GetPixelFromAtlas(ushort voxelID)
    {
        if (voxelID >= voxelUvCoordinates.Length) return new Color32(255, 0, 255, 255); // Безопасность

        Vector2Int tileCoord = voxelUvCoordinates[voxelID];
        int atlasTileSize = atlasWidth / 2; // Атлас 2x2
        int x = (int)((tileCoord.x + 0.5f) * atlasTileSize);
        int y = (int)((tileCoord.y + 0.5f) * atlasTileSize);
        return atlasData[y * atlasWidth + x];
    }*/


    public void Execute()
    {
        var heights = new NativeArray<int>(Chunk.Width * Chunk.Width, Allocator.Temp);
        var biomeInfos = new NativeArray<VoxelBiomeInfo>(Chunk.Width * Chunk.Width, Allocator.Temp);

        for (int x = 0; x < Chunk.Width; x++)
        {
            for (int z = 0; z < Chunk.Width; z++)
            {
                int index2D = x * Chunk.Width + z;
                float worldX = chunkPosition.x * Chunk.Width + x;
                float worldZ = chunkPosition.z * Chunk.Width + z;
                Vector2 worldPos = new Vector2(worldX, worldZ);

                BiomeInstanceBurst dominantBiome = neutralBiome;
                float maxInfluence = 0f;
                for (int i = 0; i < biomeInstances.Length; i++)
                {
                    var biome = biomeInstances[i];
                    float distance = Vector2.Distance(worldPos, biome.position);
                    if (distance < biome.influenceRadius)
                    {
                        float influence = 1f - (distance / biome.influenceRadius);
                        if (influence > maxInfluence)
                        {
                            maxInfluence = influence;
                            dominantBiome = biome;
                        }
                    }
                }

                int baseTerrainHeight = 10 + (int)(((heightMapNoise.GetNoise(worldX, worldZ) + 1f) / 2f) * 20f);
                heights[index2D] = baseTerrainHeight;
                biomeInfos[index2D] = new VoxelBiomeInfo
                {
                    biomeID = dominantBiome.biomeID,
                    blockID = dominantBiome.blockID
                };
            }
        }
        
        const int BLEND_RADIUS = 5;
        for (int x = 0; x < Chunk.Width; x++)
        {
            for (int z = 0; z < Chunk.Width; z++)
            {
                int index2D = x * Chunk.Width + z;
                int terrainHeight = heights[index2D];
                VoxelBiomeInfo mainBiomeInfo = biomeInfos[index2D];

                float minDistanceSq = BLEND_RADIUS * BLEND_RADIUS + 1;
                VoxelBiomeInfo closestOtherBiomeInfo = mainBiomeInfo; 

                for (int nx = -BLEND_RADIUS; nx <= BLEND_RADIUS; nx++)
                for (int nz = -BLEND_RADIUS; nz <= BLEND_RADIUS; nz++)
                {
                    int checkX = x + nx;
                    int checkZ = z + nz;
                    if (checkX >= 0 && checkX < Chunk.Width && checkZ >= 0 && checkZ < Chunk.Width)
                    {
                        var otherBiomeInfo = biomeInfos[checkX * Chunk.Width + checkZ];
                        if (otherBiomeInfo.biomeID != mainBiomeInfo.biomeID)
                        {
                            float distSq = nx * nx + nz * nz;
                            if (distSq < minDistanceSq)
                            {
                                minDistanceSq = distSq;
                                closestOtherBiomeInfo = otherBiomeInfo;
                            }
                        }
                    }
                }

                float blendFactor = Mathf.Sqrt(minDistanceSq) / BLEND_RADIUS;
                blendFactor = Mathf.Clamp01(blendFactor);

                for (int y = 0; y < Chunk.Height; y++)
                {
                    int index3D = y * (Chunk.Width * Chunk.Width) + x * Chunk.Width + z;
                    
                    ushort voxelIdToSet = (y > terrainHeight) ? (ushort)0 : mainBiomeInfo.blockID;
                    resultingVoxelIDs[index3D] = voxelIdToSet;

                    if (voxelIdToSet == 0)
                    {
                        resultingVoxelColors[index3D] = new Color32(0, 0, 0, 0);
                    }
                    else
                    {
                        Color32 colorA = voxelIdToColorMap[voxelIdToSet];
                        Color32 colorB = voxelIdToColorMap[closestOtherBiomeInfo.blockID];
                        resultingVoxelColors[index3D] = Color32.Lerp(colorB, colorA, blendFactor);
                    }
                }
            }
        }
        heights.Dispose();
        biomeInfos.Dispose();
    }
}