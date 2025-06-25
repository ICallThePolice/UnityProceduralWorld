using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[BurstCompile]
public struct GenerationJob : IJobParallelFor
{
    [ReadOnly] public Vector3Int chunkPosition;
    [ReadOnly] public FastNoiseLite heightMapNoise;
    [ReadOnly] public FastNoiseLite detailNoise;
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
        // 1. Расчет координат
        int y = index / (Chunk.Width * Chunk.Width);
        int temp = index % (Chunk.Width * Chunk.Width);
        int x = temp / Chunk.Width;
        int z = temp % Chunk.Width;
        float worldX = chunkPosition.x * Chunk.Width + x;
        float worldZ = chunkPosition.z * Chunk.Width + z;
        Vector2 worldPos = new Vector2(worldX, worldZ);

        // 2. Поиск ОДНОГО доминантного биома
        BiomeInstanceBurst dominantBiome = neutralBiome;
        float maxInfluence = 0f;
        for (int i = 0; i < biomeInstances.Length; i++)
        {
            var biome = biomeInstances[i];
            float distance = Vector2.Distance(worldPos, biome.position);
            if (distance < biome.influenceRadius)
            {
                float influence = Mathf.Pow(1f - (distance / biome.influenceRadius), biome.contrast);
                if (influence > maxInfluence)
                {
                    maxInfluence = influence;
                    dominantBiome = biome;
                }
            }
        }

        // 3. Расчет высоты ландшафта
        int baseTerrainHeight = 5 + Mathf.RoundToInt(((heightMapNoise.GetNoise(worldX, worldZ) + 1f) / 2f) * 20f);
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

        int finalHeight = (int)Mathf.Lerp(baseTerrainHeight, modifiedBiomeHeight, maxInfluence);

        VoxelStateData voxelData = new VoxelStateData { terrainHeight = finalHeight, voxelID = 0 };

        // 4. Применение артефактов, меняющих ландшафт (MiniCrater)
        for (int i = 0; i < artifactInstances.Length; i++)
        {
            var artifact = artifactInstances[i];
            if (artifact.artifactType == BiomeArtifactType.MiniCrater)
            {
                craterGenerator.Apply(ref voxelData, artifact, worldPos, y, baseTerrainHeight);
            }
        }

        // 5. Установка вокселей
        int finalTerrainHeight = Mathf.Clamp(voxelData.terrainHeight, 1, Chunk.Height - 1);
        if (y <= finalTerrainHeight)
        {
            float detailValue = (detailNoise.GetNoise(worldX, worldZ) + 1f) / 2f;
            if (y == finalTerrainHeight && detailValue < 0.3f)
            {
                voxelData.voxelID = 0;
            }
            else
            {
                if (y == finalTerrainHeight) voxelData.voxelID = dominantBiome.surfaceVoxelID;
                else if (y > finalTerrainHeight - dominantBiome.subSurfaceDepth) voxelData.voxelID = dominantBiome.subSurfaceVoxelID;
                else voxelData.voxelID = dominantBiome.mainVoxelID;
            }
        }

        // 6. Применение объемных артефактов (FloatingIslet)
        for (int i = 0; i < artifactInstances.Length; i++)
        {
            var artifact = artifactInstances[i];
            if (artifact.artifactType == BiomeArtifactType.FloatingIslet)
            {
                // Здесь мы используем старый метод isletGenerator.Apply
                // Убедитесь, что он использует artifact.mainVoxelID
            }
        }

        // 7. Запись результата
        resultingVoxelIDs[index] = voxelData.voxelID;
    }
}