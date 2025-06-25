using UnityEngine;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Rendering;
using System;

#region Вспомогательные классы запросов
public class AsyncChunkDataRequest
{
    public JobHandle JobHandle;
    public Chunk TargetChunk;
    public NativeArray<ushort> VoxelIDs;
    public NativeArray<Color32> VoxelColors;
    public NativeArray<BiomeInstanceBurst> BiomeInstances;
    
    public void Dispose()
    {
        if (VoxelIDs.IsCreated) VoxelIDs.Dispose();
        if (VoxelColors.IsCreated) VoxelColors.Dispose();
        if (BiomeInstances.IsCreated) BiomeInstances.Dispose();
    }
}
public class AsyncChunkMeshRequest
{
    public JobHandle JobHandle;
    public Chunk TargetChunk;
    public NativeArray<ushort> VoxelIDs;
    public NativeArray<Color32> VoxelColors;
    public NativeArray<ushort> NeighborPosX, NeighborNegX, NeighborPosZ, NeighborNegZ;
    public NativeList<Vertex> Vertices;
    public NativeList<int> Triangles;

    public void DisposeAll()
    {
        if (VoxelIDs.IsCreated) VoxelIDs.Dispose();
        if (VoxelColors.IsCreated) VoxelColors.Dispose();
        if (NeighborPosX.IsCreated) NeighborPosX.Dispose();
        if (NeighborNegX.IsCreated) NeighborNegX.Dispose();
        if (NeighborPosZ.IsCreated) NeighborPosZ.Dispose();
        if (NeighborNegZ.IsCreated) NeighborNegZ.Dispose();
        if (Vertices.IsCreated) Vertices.Dispose();
        if (Triangles.IsCreated) Triangles.Dispose();
    }
}
#endregion

public class VoxelGenerationPipeline
{
    public event Action<Chunk, Mesh> OnChunkMeshReady;
    private readonly WorldSettingsSO settings;
    private readonly BiomeManager biomeManager;
    private NativeArray<Color32> voxelIdToColorMap;

    private readonly List<Chunk> chunksToGenerateData = new List<Chunk>();
    private readonly List<Chunk> chunksToGenerateMesh = new List<Chunk>();
    private readonly List<AsyncChunkDataRequest> runningDataJobs = new List<AsyncChunkDataRequest>();
    private readonly List<AsyncChunkMeshRequest> runningMeshJobs = new List<AsyncChunkMeshRequest>();

    public VoxelGenerationPipeline(WorldSettingsSO settings, BiomeManager biomeManager)
    {
        this.settings = settings;
        this.biomeManager = biomeManager;

        Texture2D atlas = settings.worldMaterial.mainTexture as Texture2D;
        if (atlas != null && atlas.isReadable)
        {
            ushort maxId = 0;
            foreach (var voxelType in settings.voxelTypes) { if (voxelType != null && voxelType.ID > maxId) maxId = voxelType.ID; }
            
            voxelIdToColorMap = new NativeArray<Color32>(maxId + 1, Allocator.Persistent);
            var tempVoxelUvMap = new NativeArray<Vector2Int>(maxId + 1, Allocator.Temp);
            foreach (var voxelType in settings.voxelTypes) { if (voxelType != null) tempVoxelUvMap[voxelType.ID] = voxelType.textureAtlasCoord; }

            int atlasWidth = atlas.width;
            int atlasTileSize = atlasWidth / 2;
            Color32[] atlasPixels = atlas.GetPixels32();

            for (ushort id = 0; id < voxelIdToColorMap.Length; id++)
            {
                if (id < tempVoxelUvMap.Length)
                {
                    Vector2Int tileCoord = tempVoxelUvMap[id];
                    int x = (int)((tileCoord.x + 0.5f) * atlasTileSize);
                    int y = (int)((tileCoord.y + 0.5f) * atlasTileSize);
                    voxelIdToColorMap[id] = atlasPixels[y * atlasWidth + x];
                }
            }
            tempVoxelUvMap.Dispose();
        }
        else
        {
            Debug.LogError("Материалу мира не назначена текстура атласа, или она не помечена как Read/Write Enabled!");
        }
    }

    public void RequestDataGeneration(Chunk chunk) { if (!chunksToGenerateData.Contains(chunk)) chunksToGenerateData.Add(chunk); }
    public void RequestMeshGeneration(Chunk chunk) { if (!chunksToGenerateMesh.Contains(chunk)) chunksToGenerateMesh.Add(chunk); }

    public void CancelChunkGeneration(Vector3Int chunkPos)
    {
        chunksToGenerateData.RemoveAll(chunk => chunk.chunkPosition == chunkPos);
        chunksToGenerateMesh.RemoveAll(chunk => chunk.chunkPosition == chunkPos);
    }

    public void ProcessQueues(Vector3Int playerChunkPos, Func<Vector3Int, Chunk> getChunkCallback)
    {
        ProcessDataGenerationQueue(playerChunkPos);
        ProcessMeshGenerationQueue(playerChunkPos, getChunkCallback);
    }
    
    private void ProcessDataGenerationQueue(Vector3Int playerChunkPos)
    {
        if (runningDataJobs.Count >= SystemInfo.processorCount || chunksToGenerateData.Count == 0) return;
        
        Chunk chunkToProcess = chunksToGenerateData[0];
        chunksToGenerateData.RemoveAt(0);

        List<BiomeInstance> relevantBiomes = biomeManager.GetBiomesInArea(chunkToProcess.chunkPosition, settings.renderDistance);
        var biomeInstancesForJob = new NativeArray<BiomeInstanceBurst>(relevantBiomes.Count, Allocator.Persistent);
        for (int i = 0; i < relevantBiomes.Count; i++)
        {
            var instance = relevantBiomes[i];
            biomeInstancesForJob[i] = new BiomeInstanceBurst
            {
                biomeID = instance.settings.biome.biomeID,
                position = instance.position,
                influenceRadius = instance.calculatedRadius,
                contrast = instance.calculatedContrast,
                blockID = instance.settings.biome.BiomeBlock.ID // <-- Передаем ID единственного блока
            };
        }

        var dataJobVoxelIDs = new NativeArray<ushort>(Chunk.Width * Chunk.Height * Chunk.Width, Allocator.Persistent);
        var dataJobVoxelColors = new NativeArray<Color32>(Chunk.Width * Chunk.Height * Chunk.Width, Allocator.Persistent);

        var heightNoise = new FastNoiseLite(settings.heightmapNoiseSettings.seed);
        heightNoise.SetFrequency(settings.heightmapNoiseSettings.scale);
        heightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        heightNoise.SetFractalOctaves(settings.heightmapNoiseSettings.octaves);
        heightNoise.SetFractalLacunarity(settings.heightmapNoiseSettings.lacunarity);
        heightNoise.SetFractalGain(settings.heightmapNoiseSettings.persistence);

        var neutralBiomeBurst = new BiomeInstanceBurst
        {
            biomeID = settings.neutralBiome.biomeID
        };

        var job = new GenerationJob
        {
            chunkPosition = chunkToProcess.chunkPosition,
            heightMapNoise = heightNoise,
            neutralBiome = neutralBiomeBurst,
            biomeInstances = biomeInstancesForJob,
            voxelIdToColorMap = this.voxelIdToColorMap,
            resultingVoxelIDs = dataJobVoxelIDs,
            resultingVoxelColors = dataJobVoxelColors
        };

        var handle = job.Schedule();
        runningDataJobs.Add(new AsyncChunkDataRequest
        {
            JobHandle = handle,
            TargetChunk = chunkToProcess,
            VoxelIDs = dataJobVoxelIDs,
            VoxelColors = dataJobVoxelColors,
            BiomeInstances = biomeInstancesForJob,
        });
    }
    
    // Главный цикл обработки очереди на создание меша
    private void ProcessMeshGenerationQueue(Vector3Int playerChunkPos, Func<Vector3Int, Chunk> getChunkCallback)
    {
        if (runningMeshJobs.Count >= SystemInfo.processorCount || chunksToGenerateMesh.Count == 0) return;
        
        Chunk chunkToProcess = chunksToGenerateMesh[0];

        Chunk nPosX = getChunkCallback(chunkToProcess.chunkPosition + new Vector3Int(1, 0, 0));
        Chunk nNegX = getChunkCallback(chunkToProcess.chunkPosition + new Vector3Int(-1, 0, 0));
        Chunk nPosZ = getChunkCallback(chunkToProcess.chunkPosition + new Vector3Int(0, 0, 1));
        Chunk nNegZ = getChunkCallback(chunkToProcess.chunkPosition + new Vector3Int(0, 0, -1));

        bool allNeighborsReady = (nPosX == null || nPosX.isDataGenerated) && (nNegX == null || nNegX.isDataGenerated) && (nPosZ == null || nPosZ.isDataGenerated) && (nNegZ == null || nNegZ.isDataGenerated);

        if (allNeighborsReady)
        {
            chunksToGenerateMesh.RemoveAt(0);

            var jobVoxelIDs = new NativeArray<ushort>(chunkToProcess.GetAllVoxelIDs(), Allocator.Persistent);
            var jobVoxelColors = new NativeArray<Color32>(chunkToProcess.voxelColors, Allocator.Persistent);
            var neighborDataPosX = GetNeighborData(nPosX);
            var neighborDataNegX = GetNeighborData(nNegX);
            var neighborDataPosZ = GetNeighborData(nPosZ);
            var neighborDataNegZ = GetNeighborData(nNegZ);
            var vertices = new NativeList<Vertex>(Allocator.Persistent);
            var triangles = new NativeList<int>(Allocator.Persistent);

            var job = new MeshingJob
            {
                voxelIDs = jobVoxelIDs,
                voxelColors = jobVoxelColors,
                hasNeighborPosX = nPosX != null,
                hasNeighborNegX = nNegX != null,
                hasNeighborPosZ = nPosZ != null,
                hasNeighborNegZ = nNegZ != null,
                neighborVoxelsPosX = neighborDataPosX,
                neighborVoxelsNegX = neighborDataNegX,
                neighborVoxelsPosZ = neighborDataPosZ,
                neighborVoxelsNegZ = neighborDataNegZ,
                vertices = vertices,
                triangles = triangles
            };

            var handle = job.Schedule();
            
            runningMeshJobs.Add(new AsyncChunkMeshRequest
            {
                JobHandle = handle,
                TargetChunk = chunkToProcess,
                VoxelIDs = jobVoxelIDs,
                VoxelColors = jobVoxelColors,
                NeighborPosX = neighborDataPosX,
                NeighborNegX = neighborDataNegX,
                NeighborPosZ = neighborDataPosZ,
                NeighborNegZ = neighborDataNegZ,
                Vertices = vertices,
                Triangles = triangles
            });
        }
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
                request.TargetChunk.SetAllVoxelIDs(request.VoxelIDs.ToArray());
                request.TargetChunk.voxelColors = request.VoxelColors.ToArray();
                request.TargetChunk.isDataGenerated = true;
                onDataReadyCallback(request.TargetChunk);
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
                Mesh mesh = new Mesh();
                if (request.Vertices.Length > 0)
                {
                    // --- ФИНАЛЬНОЕ, ПРАВИЛЬНОЕ ОПИСАНИЕ МЕША ---
                    // "Декларация" должна ТОЧНО соответствовать структуре "коробки" Vertex
                    var vertexAttributes = new[]
                    {
                        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                        new VertexAttributeDescriptor(VertexAttribute.Color,    VertexAttributeFormat.UNorm8, 4)
                    };

                    mesh.SetVertexBufferParams(request.Vertices.Length, vertexAttributes);
                    mesh.SetIndexBufferParams(request.Triangles.Length, IndexFormat.UInt32);

                    mesh.SetVertexBufferData(request.Vertices.AsArray(), 0, 0, request.Vertices.Length);
                    mesh.SetIndexBufferData(request.Triangles.AsArray(), 0, 0, request.Triangles.Length);

                    mesh.subMeshCount = 1;
                    mesh.SetSubMesh(0, new SubMeshDescriptor(0, request.Triangles.Length));
                    
                    mesh.RecalculateBounds();
                    
                    // Unity сама добавит нормали отдельным потоком данных,
                    // когда мы вызовем этот метод. Шейдер их получит.
                    mesh.RecalculateNormals();
                }
                
                OnChunkMeshReady?.Invoke(request.TargetChunk, mesh);
                request.DisposeAll();
                runningMeshJobs.RemoveAt(i);
            }
        }
    }

    public void Dispose()
    {
        foreach (var request in runningDataJobs)
        {
            request.JobHandle.Complete();
            request.Dispose();
        }
        foreach (var request in runningMeshJobs)
        {
            request.JobHandle.Complete();
            request.DisposeAll();
        }
    }

    private NativeArray<ushort> GetNeighborData(Chunk neighbor)
    {
        if (neighbor != null && neighbor.isDataGenerated)
            return new NativeArray<ushort>(neighbor.GetAllVoxelIDs(), Allocator.Persistent);
        return new NativeArray<ushort>(0, Allocator.Persistent);
    }
}