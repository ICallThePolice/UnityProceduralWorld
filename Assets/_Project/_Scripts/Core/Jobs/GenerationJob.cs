using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using System.Collections.Generic;

[BurstCompile]
public struct GenerationJob : IJob
{
    // ВХОДНЫЕ ДАННЫЕ
    [ReadOnly] public Vector3Int chunkPosition;
    [ReadOnly] public FastNoiseLite heightMapNoise;
    [ReadOnly] public NativeArray<ClusterInfoBurst> clusters;
    [ReadOnly] public NativeArray<float2> allClusterNodes;
    [ReadOnly] public NativeArray<EdgeInfoBurst> allClusterEdges;
    [ReadOnly] public NativeArray<OverlayPlacementDataBurst> overlayPlacements;
    [ReadOnly] public NativeArray<VoxelOverlayDataBurst> voxelOverlayMap;

    [ReadOnly] public NativeArray<VoxelTypeDataBurst> voxelTypeMap;
    [ReadOnly] public ushort globalBiomeBlockID;
    [ReadOnly] public float2 atlasSizeInTiles;

    // ВЫХОДНЫЕ ДАННЫЕ
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
    
    // Вспомогательные структуры
    public struct InfluenceComparer : IComparer<BiomeInfluence> {
        public int Compare(BiomeInfluence x, BiomeInfluence y) => y.influence.CompareTo(x.influence);
    }
    
    private struct BlendedProperties {
        public VoxelTypeDataBurst primaryLandscape;
        public VoxelTypeDataBurst secondaryLandscape;
        public float landscapeBlendFactor;
        public VoxelOverlayDataBurst overlayData;
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
                    if (y >= surfaceHeight) { primaryBlockIDs[voxelIndex] = 0; continue; }

                    primaryBlockIDs[voxelIndex] = props.primaryLandscape.id;
                    Color landscapeColor = Color.Lerp(props.primaryLandscape.baseColor, props.secondaryLandscape.baseColor, props.landscapeBlendFactor);
                    finalColors[voxelIndex] = Color.Lerp(landscapeColor, props.overlayData.tintColor, props.overlayInfluence);
                    finalUv0s[voxelIndex] = props.primaryLandscape.baseUV;
                    finalUv1s[voxelIndex] = props.secondaryLandscape.baseUV;
                    finalTexBlends[voxelIndex] = props.landscapeBlendFactor;
                    float4 landscapeGapColor = math.lerp(ToFloat4(props.primaryLandscape.gapColor), ToFloat4(props.secondaryLandscape.gapColor), props.landscapeBlendFactor);
                    finalGapColors[voxelIndex] = math.lerp(landscapeGapColor, ToFloat4(props.overlayData.gapColor), props.overlayInfluence);
                    float landscapeGapWidth = math.lerp(props.primaryLandscape.gapWidth, props.secondaryLandscape.gapWidth, props.landscapeBlendFactor);
                    finalGapWidths[voxelIndex] = math.lerp(landscapeGapWidth, props.overlayData.gapWidth, props.overlayInfluence);
                    float3 landscapeBevelData = math.lerp(props.primaryLandscape.bevelData, props.secondaryLandscape.bevelData, props.landscapeBlendFactor);
                    finalBevelData[voxelIndex] = math.lerp(landscapeBevelData, props.overlayData.bevelData, props.overlayInfluence);
                    finalEmissionData[voxelIndex] = math.lerp(float4.zero, props.overlayData.emissionData, props.overlayInfluence);
                    finalMaterialProps[voxelIndex] = math.lerp(new float2(0.1f, 0f), props.overlayData.materialProps, props.overlayInfluence);
                }
            }
        }
    }

     private BlendedProperties GetBlendedPropertiesForColumn(float2 worldPos)
    {
        var influences = new NativeList<BiomeInfluence>(Allocator.Temp);
        float totalInfluence = 0;

        for (int i = 0; i < clusters.Length; i++)
        {
            var cluster = clusters[i];
            
            float influenceValue = 0;
            float minDistance = float.MaxValue;
            float localRadiusAtClosestPoint = cluster.influenceRadius;

            if (cluster.edgeCount > 0)
            {
                for (int j = 0; j < cluster.edgeCount; j++)
                {
                    var edge = allClusterEdges[cluster.edgeStartIndex + j];
                    var nodeA_pos = allClusterNodes[edge.nodeA_idx];
                    var nodeB_pos = allClusterNodes[edge.nodeB_idx];
                    
                    // Находим ближайшую точку Q на отрезке AB для нашей точки P (worldPos)
                    float2 ab = nodeB_pos - nodeA_pos;
                    float2 ap = worldPos - nodeA_pos;
                    float dot_ab_ab = math.dot(ab, ab);
                    float t = (dot_ab_ab == 0) ? 0 : math.saturate(math.dot(ap, ab) / dot_ab_ab);
                    
                    float2 closestPointOnLine = nodeA_pos + t * ab;
                    float dist = math.distance(worldPos, closestPointOnLine);

                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        // --- НОВАЯ ЛОГИКА: Интерполируем радиус вдоль линии ---
                        // Здесь мы предполагаем, что радиус всех ядер в кластере одинаков,
                        // и равен общему радиусу кластера. Это упрощение.
                        localRadiusAtClosestPoint = cluster.influenceRadius; 
                    }
                }
            }
            else if (cluster.nodeCount > 0)
            {
                minDistance = math.distance(worldPos, allClusterNodes[cluster.nodeStartIndex]);
                localRadiusAtClosestPoint = cluster.influenceRadius;
            }

            if (minDistance > localRadiusAtClosestPoint) continue;
            
            // Расчет влияния теперь использует локальный радиус
            float coreRadius = localRadiusAtClosestPoint * cluster.coreRadiusPercentage;

            if (minDistance < coreRadius) {
                influenceValue = 1.0f;
            } else {
                float t = (minDistance - coreRadius) / (localRadiusAtClosestPoint - coreRadius + 0.0001f);
                influenceValue = 1f - t*t;
            }
            influenceValue = math.pow(influenceValue, cluster.contrast);

            if (influenceValue > 0.001f) {
                influences.Add(new BiomeInfluence { influence = influenceValue, blockID = cluster.blockID });
                totalInfluence += influenceValue;
            }
        }

        float neutralInfluence = math.max(0, 1.0f - totalInfluence);
        influences.Add(new BiomeInfluence { influence = neutralInfluence, blockID = globalBiomeBlockID });
        influences.Sort(new InfluenceComparer());

        VoxelTypeDataBurst primaryLandscape, secondaryLandscape;
        float landscapeBlendFactor;

        BiomeInfluence strongest = influences[0];
        if (influences.Length > 1) {
            BiomeInfluence secondStrongest = influences[1];
            float blendSum = strongest.influence + secondStrongest.influence;
            landscapeBlendFactor = (blendSum > 0) ? (secondStrongest.influence / blendSum) : 0;
            primaryLandscape = voxelTypeMap[strongest.blockID];
            secondaryLandscape = voxelTypeMap[secondStrongest.blockID];
        } else {
            primaryLandscape = voxelTypeMap[strongest.blockID];
            secondaryLandscape = primaryLandscape;
            landscapeBlendFactor = 0;
        }
        
        influences.Dispose();
        
        VoxelOverlayDataBurst dominantOverlay = default;
        float overlayInfluence = 0;
        int maxPriority = -1;

        for (int i = 0; i < overlayPlacements.Length; i++)
        {
            var placement = overlayPlacements[i]; // Берем данные о РАЗМЕЩЕНИИ
            float dist = math.distance(worldPos, placement.position);
            if (dist > placement.radius) continue;

            // По ID из данных о размещении, находим СВОЙСТВА оверлея в карте
            var overlayProps = voxelOverlayMap[placement.overlayID];
            if (overlayProps.priority > maxPriority)
            {
                maxPriority = overlayProps.priority;
                dominantOverlay = overlayProps; // Сохраняем СВОЙСТВА доминанта
                // Рассчитываем его влияние
                overlayInfluence = 1.0f - math.saturate(dist / placement.radius);
                overlayInfluence = math.pow(overlayInfluence, placement.blendSharpness * 4.0f + 1.0f);
            }
        }
        
        return new BlendedProperties {
            primaryLandscape = primaryLandscape,
            secondaryLandscape = secondaryLandscape,
            landscapeBlendFactor = landscapeBlendFactor,
            overlayData = dominantOverlay,
            overlayInfluence = overlayInfluence
        };
    }

    private float GetDistanceToCluster(float2 worldPos, ClusterInfoBurst cluster)
    {
        float minSqDistance = float.MaxValue;
        if (cluster.edgeCount > 0)
        {
            for (int j = 0; j < cluster.edgeCount; j++)
            {
                var edge = allClusterEdges[cluster.edgeStartIndex + j];
                var nodeA = allClusterNodes[edge.nodeA_idx];
                var nodeB = allClusterNodes[edge.nodeB_idx];
                minSqDistance = math.min(minSqDistance, GetSquaredDistanceToLineSegment(nodeA, nodeB, worldPos));
            }
        }
        else if (cluster.nodeCount > 0)
        {
            minSqDistance = math.distancesq(worldPos, allClusterNodes[cluster.nodeStartIndex]);
        }
        return math.sqrt(minSqDistance);
    }
    
    private float GetSquaredDistanceToLineSegment(float2 a, float2 b, float2 p)
    {
        float2 ab = b - a;
        float2 ap = p - a;
        float dot_ap_ab = math.dot(ap, ab);
        if (dot_ap_ab <= 0.0f) return math.distancesq(p, a);
        float dot_ab_ab = math.dot(ab, ab);
        if (dot_ab_ab <= dot_ap_ab) return math.distancesq(p, b);
        return math.distancesq(p, a + (dot_ap_ab / dot_ab_ab) * ab);
    }

    private float4 ToFloat4(Color32 c)
    {
        return new float4(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
    }
}