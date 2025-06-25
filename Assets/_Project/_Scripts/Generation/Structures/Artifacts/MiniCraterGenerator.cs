// ФАЙЛ: MiniCraterGenerator.cs
using UnityEngine;

public struct MiniCraterGenerator : IArtifactGenerator
{
    public void Apply(ref VoxelStateData voxelData, in ArtifactInstanceBurst artifact, in Vector2 worldPos, int y, int baseTerrainHeight)
    {
        float distToArtifact = Vector2.Distance(worldPos, artifact.position);
        float artifactRadius = artifact.size.x / 2f;

        if (distToArtifact < artifactRadius)
        {
            float craterFactor = 1f - (distToArtifact / artifactRadius);
            voxelData.terrainHeight -= (int)(artifact.height * craterFactor);
        }
    }
}