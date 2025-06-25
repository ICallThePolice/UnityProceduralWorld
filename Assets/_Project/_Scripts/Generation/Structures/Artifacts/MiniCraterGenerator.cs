using UnityEngine;

public struct MiniCraterGenerator : IArtifactGenerator
{
    public void Apply(ref VoxelStateData voxelData, in ArtifactInstanceBurst artifact, in Vector2 worldPos, int y, int baseTerrainHeight)
    {
        float distToArtifact = Vector2.Distance(worldPos, artifact.position);
        float artifactRadius = artifact.size.x / 2f;

        if (distToArtifact < artifactRadius)
        {
            // --- ИЗМЕНЕНИЕ: Используем плавную кривую вместо линейной ---
            float influence = 1f - (distToArtifact / artifactRadius);
            float smoothInfluence = influence * influence * (3f - 2f * influence); // SmoothStep

            // Высота теперь берется напрямую из артефакта (мы изменим ее вычисление в BiomeManager)
            voxelData.terrainHeight -= (int)(artifact.height * smoothInfluence);
        }
    }
}