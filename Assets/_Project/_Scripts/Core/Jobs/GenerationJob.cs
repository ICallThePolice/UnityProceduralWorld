using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[BurstCompile]
public struct GenerationJob : IJobParallelFor
{
    [ReadOnly] public Vector3Int chunkPosition;
    [ReadOnly] public FastNoiseLite heightMapNoise;
    [ReadOnly] public BiomeInstanceBurst neutralBiome;
    [ReadOnly] public NativeArray<BiomeInstanceBurst> biomeInstances;
    [ReadOnly] public NativeArray<ArtifactInstanceBurst> artifactInstances;

    [ReadOnly] public AdditiveBiomeGenerator additiveGenerator;
    [ReadOnly] public SubtractiveBiomeGenerator subtractiveGenerator;
    [ReadOnly] public ReplaceBiomeGenerator replaceGenerator;
    [ReadOnly] public MiniCraterGenerator craterGenerator;
    [ReadOnly] public FloatingIsletGenerator isletGenerator;
    
    public NativeArray<ushort> resultingVoxelIDs;

    public void Execute(int index)
    {
        // 1. Расчет координат и базовой высоты
        int y = index / (Chunk.Width * Chunk.Width);
        int temp = index % (Chunk.Width * Chunk.Width);
        int x = temp / Chunk.Width;
        int z = temp % Chunk.Width;
        float worldX = chunkPosition.x * Chunk.Width + x;
        float worldZ = chunkPosition.z * Chunk.Width + z;
        Vector2 worldPos = new Vector2(worldX, worldZ);
        int baseTerrainHeight = 5 + Mathf.RoundToInt(((heightMapNoise.GetNoise(worldX, worldZ) + 1f) / 2f) * 20f);
        
        // 2. Поиск доминантного биома
        BiomeInstanceBurst dominantBiome = neutralBiome;
        float maxInfluence = 0f;
        for (int i = 0; i < biomeInstances.Length; i++)
        {
            var biome = biomeInstances[i];
            float distance = Vector2.Distance(worldPos, biome.position);
            if (distance < biome.influenceRadius)
            {
                float influence = 1f - (distance / biome.influenceRadius);
                influence = Mathf.Pow(influence, biome.contrast);
                if (influence > maxInfluence)
                {
                    maxInfluence = influence;
                    dominantBiome = biome;
                }
            }
        }
        
        // 3. Расчет высоты с учетом биома
        int modifiedBiomeHeight = baseTerrainHeight;
        if (maxInfluence > 0)
        {
            float biomeCenterNoise = heightMapNoise.GetNoise(dominantBiome.position.x, dominantBiome.position.y);
            float dominantBiomeBaseHeight = 10 + Mathf.RoundToInt(((biomeCenterNoise + 1f) / 2f) * 20f);

            switch (dominantBiome.terrainModificationType)
            {
                case TerrainModifier.Additive:
                    modifiedBiomeHeight = additiveGenerator.GetModifiedHeight(dominantBiome, baseTerrainHeight, dominantBiomeBaseHeight, worldPos, heightMapNoise);
                    break;
                case TerrainModifier.Subtractive:
                    modifiedBiomeHeight = subtractiveGenerator.GetModifiedHeight(dominantBiome, baseTerrainHeight, dominantBiomeBaseHeight, worldPos, heightMapNoise);
                    break;
                case TerrainModifier.Replace:
                    modifiedBiomeHeight = replaceGenerator.GetModifiedHeight(dominantBiome, baseTerrainHeight, dominantBiomeBaseHeight, worldPos, heightMapNoise);
                    break;
            }
        }
        
        // 4. Финальное смешивание высоты
        int finalHeight = (int)Mathf.Lerp(baseTerrainHeight, modifiedBiomeHeight, maxInfluence);

        // 5. Создание VoxelState и применение артефактов, меняющих ландшафт
        VoxelStateData voxelData = new VoxelStateData { terrainHeight = finalHeight, voxelID = 0 };
        for (int i = 0; i < artifactInstances.Length; i++)
        {
            var artifact = artifactInstances[i];
            if (artifact.artifactType == BiomeArtifactType.MiniCrater)
            {
                craterGenerator.Apply(ref voxelData, artifact, worldPos, y, baseTerrainHeight);
            }
        }
        
        // 6. Установка вокселей ландшафта
        int finalTerrainHeight = Mathf.Clamp(voxelData.terrainHeight, 1, Chunk.Height - 1);
        if (y <= finalTerrainHeight)
        {
            if (y == finalTerrainHeight) voxelData.voxelID = dominantBiome.surfaceVoxelID;
            else if (y > finalTerrainHeight - dominantBiome.subSurfaceDepth) voxelData.voxelID = dominantBiome.subSurfaceVoxelID;
            else voxelData.voxelID = dominantBiome.mainVoxelID;
        }

        // 7. Применение объемных артефактов
        for (int i = 0; i < artifactInstances.Length; i++)
        {
            var artifact = artifactInstances[i];
            if (artifact.artifactType == BiomeArtifactType.FloatingIslet)
            {
                isletGenerator.Apply(ref voxelData, artifact, worldPos, y, baseTerrainHeight);
            }
        }

        // 8. Запись результата
        resultingVoxelIDs[index] = voxelData.voxelID;
    }
}