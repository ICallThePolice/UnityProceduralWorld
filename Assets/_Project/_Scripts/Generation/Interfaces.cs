using UnityEngine;
using System.Collections.Generic;

public interface IBiomeTerrainGenerator
{
    int GetModifiedHeight(in BiomeInstanceBurst biome, int baseTerrainHeight, float dominantBiomeBaseHeight, in Vector2 worldPos, FastNoiseLite heightMapNoise);
}
public interface IArtifactGenerator
{
    void Apply(ref VoxelStateData voxelData, in ArtifactInstanceBurst artifact, in Vector2 worldPos, int y, int baseTerrainHeight);
}

public interface IArtifactPlacementStrategy
{
    void Place(BiomeInstance parentBiome, RootArtifactConfig artifactConfig, System.Random random, List<ArtifactInstance> artifactListToFill, List<BiomeInstance> allBiomesInArea, BiomeManager manager);
}