// --- ФАЙЛ: VoxelGenerationPipeline.cs (ПОЛНАЯ ФИНАЛЬНАЯ ВЕРСИЯ) ---
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;

public class VoxelGenerationPipeline
{
    public event Action<Chunk, Mesh> OnChunkMeshReady;
    private readonly WorldSettingsSO settings;
    
    private readonly GPUBiomeMapGenerator gpuGenerator;
    private readonly Dictionary<Vector2Int, RegionData> generatedRegions = new Dictionary<Vector2Int, RegionData>();
    private readonly HashSet<Vector2Int> regionsBeingGenerated = new HashSet<Vector2Int>();
    private readonly int regionSize = 256;

    private readonly List<Chunk> chunksToGenerateData = new List<Chunk>();
    private readonly List<Chunk> chunksToGenerateMesh = new List<Chunk>();
    private readonly List<AsyncChunkDataRequest> runningDataJobs = new List<AsyncChunkDataRequest>();
    private readonly List<AsyncChunkMeshRequest> runningMeshJobs = new List<AsyncChunkMeshRequest>();

    private NativeArray<VoxelTypeDataBurst> voxelTypeMap;

    public VoxelGenerationPipeline(WorldSettingsSO settings, BiomeManager biomeManager)
    {
        this.settings = settings;
        InitializeVoxelTypeMap();
        
        gpuGenerator = new GPUBiomeMapGenerator(settings.biomeMapComputeShader, biomeManager.biomeMap);
    }
    public void RequestDataGeneration(Chunk chunk)
    {
        if (!chunksToGenerateData.Contains(chunk) && !chunk.isDataGenerated && !chunk.isDataJobRunning)
            chunksToGenerateData.Add(chunk);
    }

    public void RequestMeshGeneration(Chunk chunk)
    {
        if (!chunksToGenerateMesh.Contains(chunk) && !chunk.isMeshGenerated && !chunk.isMeshJobRunning)
            chunksToGenerateMesh.Add(chunk);
    }


    public void ProcessQueues(Func<Vector3Int, Chunk> getChunkCallback)
    {
        RequestNeededRegions(chunksToGenerateData.Select(GetRegionCoordsForChunk).ToList());
        ProcessDataGenerationQueue();
        ProcessMeshGenerationQueue(getChunkCallback);
    }

    public void RequestNeededRegions(List<Vector2Int> regionCoordsList)
    {
        var neededRegions = new HashSet<Vector2Int>(regionCoordsList);
        foreach (var regionCoords in neededRegions)
        {
            if (!generatedRegions.ContainsKey(regionCoords) && !regionsBeingGenerated.Contains(regionCoords))
            {
                regionsBeingGenerated.Add(regionCoords);
                gpuGenerator.GenerateMap(regionCoords, regionSize, this.settings, (data) =>
                {
                    if (data != null) { generatedRegions.Add(regionCoords, data); }
                    regionsBeingGenerated.Remove(regionCoords);
                });
            }
        }
    }
    
    private void ProcessDataGenerationQueue()
    {
        if (chunksToGenerateData.Count == 0 || runningDataJobs.Count >= settings.maxDataJobsPerFrame) return;

        var readyChunks = chunksToGenerateData
            .Where(chunk => generatedRegions.ContainsKey(GetRegionCoordsForChunk(chunk)))
            .ToList();
        
        foreach(var chunkToProcess in readyChunks)
        {
            if (runningDataJobs.Count >= settings.maxDataJobsPerFrame) break;

            chunksToGenerateData.Remove(chunkToProcess);
            RegionData regionData = generatedRegions[GetRegionCoordsForChunk(chunkToProcess)];
            StartGenerationJob(chunkToProcess, regionData);
        }
    }

    private void StartGenerationJob(Chunk chunk, RegionData regionData)
    {
        chunk.isDataJobRunning = true;

        // Создаем все три NativeArray, которые ожидает GenerationJob
        var primaryMap = new NativeArray<ushort>(Chunk.Width * Chunk.Width, Allocator.TempJob);
        var secondaryMap = new NativeArray<ushort>(Chunk.Width * Chunk.Width, Allocator.TempJob);
        var blendMap = new NativeArray<float>(Chunk.Width * Chunk.Width, Allocator.TempJob);

        int startX = chunk.chunkPosition.x * Chunk.Width;
        int startZ = chunk.chunkPosition.z * Chunk.Width;

        for (int x = 0; x < Chunk.Width; x++)
        {
            for (int z = 0; z < Chunk.Width; z++)
            {
                int worldX = startX + x;
                int worldZ = startZ + z;
                
                int regionLocalX = worldX % regionSize;
                int regionLocalZ = worldZ % regionSize;
                if (regionLocalX < 0) regionLocalX += regionSize;
                if (regionLocalZ < 0) regionLocalZ += regionSize;

                // Получаем цвет, в котором закодированы все наши данные
                Color biomeDataColor = regionData.GetValue(regionLocalX, regionLocalZ);

                int mapIndex = x + z * Chunk.Width;
                
                // --- Распаковываем данные ---
                primaryMap[mapIndex] = (ushort)biomeDataColor.r;      // ID основного биома
                secondaryMap[mapIndex] = (ushort)biomeDataColor.g;    // ID вторичного биома
                blendMap[mapIndex] = biomeDataColor.b;        // Фактор смешивания
            }
        }

        var heightNoise = new FastNoiseLite(settings.heightmapNoiseSettings.seed);
        heightNoise.SetFrequency(settings.heightmapNoiseSettings.scale);
        heightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        heightNoise.SetFractalOctaves(settings.heightmapNoiseSettings.octaves);
        heightNoise.SetFractalLacunarity(settings.heightmapNoiseSettings.lacunarity);
        heightNoise.SetFractalGain(settings.heightmapNoiseSettings.persistence);

        var request = new AsyncChunkDataRequest
        {
            TargetChunk = chunk,
            primaryBlockIDs = new NativeArray<ushort>(Chunk.Size, Allocator.Persistent),
            finalColors = new NativeArray<Color32>(Chunk.Size, Allocator.Persistent),
            finalUv0s = new NativeArray<float2>(Chunk.Size, Allocator.Persistent),
            finalUv1s = new NativeArray<float2>(Chunk.Size, Allocator.Persistent),
            finalTexBlends = new NativeArray<float>(Chunk.Size, Allocator.Persistent),
            finalEmissionData = new NativeArray<float4>(Chunk.Size, Allocator.Persistent),
            finalGapColors = new NativeArray<float4>(Chunk.Size, Allocator.Persistent),
            finalMaterialProps = new NativeArray<float2>(Chunk.Size, Allocator.Persistent),
            finalGapWidths = new NativeArray<float>(Chunk.Size, Allocator.Persistent),
            finalBevelData = new NativeArray<float3>(Chunk.Size, Allocator.Persistent),
        };

        var job = new GenerationJob
        {
            chunkPosition = chunk.chunkPosition,
            heightMapNoise = heightNoise,
            chunkPrimaryIdMap = primaryMap,
            chunkSecondaryIdMap = secondaryMap,
            chunkBlendMap = blendMap,
            voxelTypeMap = this.voxelTypeMap,
            globalBiomeBlockID = settings.globalBiomeBlock.ID,
            primaryBlockIDs = request.primaryBlockIDs,
            finalColors = request.finalColors,
            finalUv0s = request.finalUv0s,
            finalUv1s = request.finalUv1s,
            finalTexBlends = request.finalTexBlends,
            finalEmissionData = request.finalEmissionData,
            finalGapColors = request.finalGapColors,
            finalMaterialProps = request.finalMaterialProps,
            finalGapWidths = request.finalGapWidths,
            finalBevelData = request.finalBevelData
        };

        JobHandle handle = job.Schedule();
        
        var handle1 = primaryMap.Dispose(handle);
        var handle2 = secondaryMap.Dispose(handle);
        var handle3 = blendMap.Dispose(handle);
        request.JobHandle = JobHandle.CombineDependencies(handle1, handle2, handle3);

        runningDataJobs.Add(request);
    }
    
    private void ProcessMeshGenerationQueue(Func<Vector3Int, Chunk> getChunkCallback)
    {
        if (runningMeshJobs.Count >= settings.maxMeshJobsPerFrame) return;

        List<Chunk> chunksReadyForMeshing = new List<Chunk>();

        foreach (var potentialChunk in chunksToGenerateMesh)
        {
            if (potentialChunk.isMeshJobRunning || !potentialChunk.isDataGenerated) continue;

            Chunk nPosX = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.right);
            Chunk nNegX = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.left);
            Chunk nPosY = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.up);
            Chunk nNegY = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.down);
            Chunk nPosZ = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.forward);
            Chunk nNegZ = getChunkCallback(potentialChunk.chunkPosition + Vector3Int.back);
            
            bool allNeighborsReady = (nPosX == null || nPosX.isDataGenerated) &&
                                     (nNegX == null || nNegX.isDataGenerated) &&
                                     (nPosY == null || nPosY.isDataGenerated) &&
                                     (nNegY == null || nNegY.isDataGenerated) &&
                                     (nPosZ == null || nPosZ.isDataGenerated) &&
                                     (nNegZ == null || nNegZ.isDataGenerated);
            if (allNeighborsReady)
            {
                chunksReadyForMeshing.Add(potentialChunk);
            }
        }
        
        foreach (var chunkToProcess in chunksReadyForMeshing)
        {
            if (runningMeshJobs.Count >= settings.maxMeshJobsPerFrame) break;

            chunksToGenerateMesh.Remove(chunkToProcess);
            chunkToProcess.isMeshJobRunning = true;

            var request = new AsyncChunkMeshRequest {
                TargetChunk = chunkToProcess,
                primaryBlockIDs = new NativeArray<ushort>(chunkToProcess.primaryBlockIDs, Allocator.TempJob),
                finalColors = new NativeArray<Color32>(chunkToProcess.finalColors, Allocator.TempJob),
                finalUv0s = new NativeArray<float2>(chunkToProcess.finalUv0s, Allocator.TempJob),
                finalUv1s = new NativeArray<float2>(chunkToProcess.finalUv1s, Allocator.TempJob),
                finalTexBlends = new NativeArray<float>(chunkToProcess.finalTexBlends, Allocator.TempJob),
                finalEmissionData = new NativeArray<float4>(chunkToProcess.finalEmissionData, Allocator.TempJob),
                finalGapColors = new NativeArray<float4>(chunkToProcess.finalGapColors, Allocator.TempJob),
                finalMaterialProps = new NativeArray<float2>(chunkToProcess.finalMaterialProps, Allocator.TempJob),
                finalGapWidths = new NativeArray<float>(chunkToProcess.finalGapWidths, Allocator.TempJob),
                finalBevelData = new NativeArray<float3>(chunkToProcess.finalBevelData, Allocator.TempJob),
                NeighborPosX = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.right),
                NeighborNegX = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.left),
                NeighborPosY = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.up),
                NeighborNegY = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.down),
                NeighborPosZ = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.forward),
                NeighborNegZ = GetNeighborData(getChunkCallback, chunkToProcess.chunkPosition + Vector3Int.back),
                Vertices = new NativeList<Vertex>(Allocator.Persistent), Triangles = new NativeList<int>(Allocator.Persistent)
            };

            var job = new MeshingJob {
                primaryBlockIDs = request.primaryBlockIDs, finalColors = request.finalColors, finalUv0s = request.finalUv0s,
                finalUv1s = request.finalUv1s, finalTexBlends = request.finalTexBlends, finalEmissionData = request.finalEmissionData,
                finalGapColors = request.finalGapColors, finalMaterialProps = request.finalMaterialProps,
                finalGapWidths = request.finalGapWidths, finalBevelData = request.finalBevelData,
                neighborVoxelsPosX = request.NeighborPosX, neighborVoxelsNegX = request.NeighborNegX,
                neighborVoxelsPosY = request.NeighborPosY, neighborVoxelsNegY = request.NeighborNegY,
                neighborVoxelsPosZ = request.NeighborPosZ, neighborVoxelsNegZ = request.NeighborNegZ,
                voxelTypeMap = this.voxelTypeMap, atlasSizeInTiles = new float2(settings.atlasSizeInTiles.x, settings.atlasSizeInTiles.y),
                vertices = request.Vertices, triangles = request.Triangles
            };

            request.JobHandle = job.Schedule();
            runningMeshJobs.Add(request);
        }
    }
    
    private NativeArray<ushort> GetNeighborData(Func<Vector3Int, Chunk> getChunkCallback, Vector3Int neighborPos)
    {
        Chunk neighbor = getChunkCallback(neighborPos);
        if (neighbor != null && neighbor.isDataGenerated)
        {
            return new NativeArray<ushort>(neighbor.primaryBlockIDs, Allocator.TempJob);
        }
        return new NativeArray<ushort>(0, Allocator.TempJob);
    }

    public void CheckCompletedJobs(Action<Chunk> onDataReadyCallback)
    {
        CheckCompletedDataJobs(onDataReadyCallback);
        CheckCompletedMeshJobs();
    }

    private void CheckCompletedDataJobs(Action<Chunk> onDataReadyCallback)
    {
        for (int i = runningDataJobs.Count - 1; i >= 0; i--)
        {
            var request = runningDataJobs[i];
            if (request.JobHandle.IsCompleted)
            {
                request.JobHandle.Complete();
                var chunk = request.TargetChunk;
                if (chunk != null)
                {
                    Debug.Log($"<color=green>[Pipeline] DataJob для чанка {chunk.chunkPosition} УСПЕШНО ЗАВЕРШЕН. Запрашиваем меш.</color>");
                    request.primaryBlockIDs.CopyTo(chunk.primaryBlockIDs);
                    request.finalColors.CopyTo(chunk.finalColors);
                    request.finalUv0s.CopyTo(chunk.finalUv0s);
                    request.finalUv1s.CopyTo(chunk.finalUv1s);
                    request.finalTexBlends.CopyTo(chunk.finalTexBlends);
                    request.finalEmissionData.CopyTo(chunk.finalEmissionData);
                    request.finalGapColors.CopyTo(chunk.finalGapColors);
                    request.finalMaterialProps.CopyTo(chunk.finalMaterialProps);
                    request.finalGapWidths.CopyTo(chunk.finalGapWidths);
                    request.finalBevelData.CopyTo(chunk.finalBevelData);

                    chunk.isDataGenerated = true;
                    chunk.isDataJobRunning = false;
                    onDataReadyCallback(chunk);
                }
                request.Dispose();
                runningDataJobs.RemoveAt(i);
            }
        }
    }

    private void CheckCompletedMeshJobs()
    {
        for (int i = runningMeshJobs.Count - 1; i >= 0; i--)
        {
            var request = runningMeshJobs[i];
            if (request.JobHandle.IsCompleted)
            {
                request.JobHandle.Complete();

                Debug.Log($"<color=cyan>[Pipeline] MeshJob для чанка {request.TargetChunk.chunkPosition} ЗАВЕРШЕН. Вершин: {request.Vertices.Length}. Треугольников: {request.Triangles.Length}.</color>");
                
                Mesh mesh = new Mesh();

                if (request.Vertices.Length > 0 && request.Triangles.Length > 0)
                {
                    var vertexAttributes = new[] {
                        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                        new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 1),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord4, VertexAttributeFormat.Float32, 4),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord5, VertexAttributeFormat.Float32, 2),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord6, VertexAttributeFormat.Float32, 1),
                        new VertexAttributeDescriptor(VertexAttribute.TexCoord7, VertexAttributeFormat.Float32, 3)
                    };
                    mesh.SetVertexBufferParams(request.Vertices.Length, vertexAttributes);
                    mesh.SetIndexBufferParams(request.Triangles.Length, IndexFormat.UInt32);
                    mesh.SetVertexBufferData(request.Vertices.AsArray(), 0, 0, request.Vertices.Length);
                    mesh.SetIndexBufferData(request.Triangles.AsArray(), 0, 0, request.Triangles.Length);
                    mesh.subMeshCount = 1;
                    mesh.SetSubMesh(0, new SubMeshDescriptor(0, request.Triangles.Length));
                    mesh.RecalculateBounds();
                }

                OnChunkMeshReady?.Invoke(request.TargetChunk, mesh);

                var chunk = request.TargetChunk;
                if (chunk != null)
                {
                    chunk.isMeshGenerated = true;
                    chunk.isMeshJobRunning = false;
                }
                request.DisposeAll();
                runningMeshJobs.RemoveAt(i);
            }
        }
    }

    private void InitializeVoxelTypeMap()
    {
        if (settings.voxelTypes == null || settings.voxelTypes.Count == 0) return;
        ushort maxId = 0;
        foreach (var voxelType in settings.voxelTypes)
        {
            if (voxelType != null && voxelType.ID > maxId) maxId = voxelType.ID;
        }
        voxelTypeMap = new NativeArray<VoxelTypeDataBurst>(maxId + 1, Allocator.Persistent);
        foreach (var voxelType in settings.voxelTypes)
        {
            if (voxelType == null) continue;
            voxelTypeMap[voxelType.ID] = new VoxelTypeDataBurst
            {
                id = voxelType.ID,
                isSolid = voxelType.isSolid,
                baseColor = voxelType.baseColor,
                baseUV = new float2(voxelType.textureAtlasCoord.x, voxelType.textureAtlasCoord.y),
                gapWidth = voxelType.GapWidth,
                gapColor = voxelType.GapColor,
                bevelData = new float3(voxelType.BevelWidth, voxelType.BevelStrength, voxelType.BevelDirection)
            };
        }
    }
    
    private Vector2Int GetRegionCoordsForChunk(Chunk chunk)
    {
        return new Vector2Int(
            Mathf.FloorToInt(chunk.chunkPosition.x * Chunk.Width / (float)regionSize),
            Mathf.FloorToInt(chunk.chunkPosition.z * Chunk.Width / (float)regionSize)
        );
    }

    // Вспомогательный метод для публичного доступа к данным вокселя (если потребуется из других систем)
    public VoxelTypeDataBurst GetVoxelTypeData(ushort id)
    {
        if (voxelTypeMap.IsCreated && id < voxelTypeMap.Length)
        {
            return voxelTypeMap[id];
        }
        return default;
    }

    // Вспомогательный метод для публичного доступа к данным региона (например, для миникарты)
    public RegionData GetRegionData(Vector2Int regionCoords)
    {
        generatedRegions.TryGetValue(regionCoords, out RegionData data);
        return data;
    }

    public void CancelChunkGeneration(Vector3Int chunkPos)
    {
        chunksToGenerateData.RemoveAll(c => c.chunkPosition == chunkPos);
        chunksToGenerateMesh.RemoveAll(c => c.chunkPosition == chunkPos);
    }

    public void Dispose()
    {
        foreach (var request in runningDataJobs)
        {
            request.JobHandle.Complete();
            request.Dispose();
        }
        runningDataJobs.Clear();

        foreach (var request in runningMeshJobs)
        {
            request.JobHandle.Complete();
            request.DisposeAll();
        }
        runningMeshJobs.Clear();

        if (voxelTypeMap.IsCreated) voxelTypeMap.Dispose();

        foreach (var region in generatedRegions.Values)
        {
            region.Dispose();
        }
        generatedRegions.Clear();
    }
}