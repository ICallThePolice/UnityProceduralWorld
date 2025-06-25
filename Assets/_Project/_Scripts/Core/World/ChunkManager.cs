using UnityEngine;
using System.Collections.Generic;
using System;

public class ChunkManager
{
    private readonly Dictionary<Vector3Int, Chunk> activeChunks = new Dictionary<Vector3Int, Chunk>();
    private readonly VoxelGenerationPipeline pipeline;
    private readonly WorldSettingsSO settings;
    private readonly Transform worldTransform;

    public ChunkManager(VoxelGenerationPipeline pipeline, WorldSettingsSO settings, Transform worldTransform)
    {
        this.pipeline = pipeline;
        this.settings = settings;
        this.worldTransform = worldTransform;
        this.pipeline.OnChunkMeshReady += HandleChunkMeshReady;

    }

    public void Update(Vector3Int playerChunkPosition)
    {
        // 1. Определяем, какие чанки должны быть в мире.
        HashSet<Vector3Int> desiredChunks = new HashSet<Vector3Int>();
        for (int x = -settings.renderDistance; x <= settings.renderDistance; x++)
        {
            for (int z = -settings.renderDistance; z <= settings.renderDistance; z++)
            {
                desiredChunks.Add(new Vector3Int(playerChunkPosition.x + x, 0, playerChunkPosition.z + z));
            }
        }

        List<Vector3Int> chunksToUnload = new List<Vector3Int>();

        // 2. Находим чанки на удаление и обновляем таймер у активных.
        foreach (var chunk in activeChunks.Values)
        {
            if (desiredChunks.Contains(chunk.chunkPosition))
            {
                chunk.lastActiveTime = Time.time; // Обновляем таймер, чанк все еще нужен
            }
            else
            {
                // Если чанк больше не в зоне видимости и его время жизни истекло
                if (Time.time - chunk.lastActiveTime > settings.chunkLingerTime)
                {
                    chunksToUnload.Add(chunk.chunkPosition);
                }
            }
        }
        
        // 3. Выгружаем "устаревшие" чанки.
        foreach (var chunkPos in chunksToUnload)
        {
            // Отправляем команду отмены в конвейер.
            // Это предотвратит запуск новых Job'ов для этого чанка.
            pipeline.CancelChunkGeneration(chunkPos);
            
            if (activeChunks.TryGetValue(chunkPos, out Chunk chunk))
            {
                chunk.Dispose(); // Используем наш новый метод для полной очистки
                activeChunks.Remove(chunkPos);
            }
        }

        // 4. Загружаем недостающие чанки.
        foreach (var chunkPos in desiredChunks)
        {
            if (!activeChunks.ContainsKey(chunkPos))
            {
                Chunk newChunk = new Chunk(chunkPos);
                activeChunks.Add(chunkPos, newChunk);
                pipeline.RequestDataGeneration(newChunk);
            }
        }
    }

    
    public void OnChunkDataReady(Chunk chunk)
    {
        pipeline.RequestMeshGeneration(chunk);
    }

    /// <summary>
    /// Метод-обработчик события OnChunkMeshReady от VoxelGenerationPipeline.
    /// Отвечает за создание GameObject'а в сцене, когда меш готов.
    /// </summary>
    private void HandleChunkMeshReady(Chunk chunk, Mesh meshData)
    {
        // Проверка на случай, если чанк был выгружен пока генерировался меш
        if (!activeChunks.ContainsKey(chunk.chunkPosition))
        {
            // Если чанка уже нет в активных, просто уничтожаем пришедший меш
            if (meshData != null) GameObject.Destroy(meshData);
            return;
        }

        chunk.meshData = meshData;

        if (chunk.gameObject == null)
        {
            chunk.gameObject = new GameObject($"Chunk ({chunk.chunkPosition.x}, {chunk.chunkPosition.z})");
            chunk.gameObject.transform.position = new Vector3(chunk.chunkPosition.x * Chunk.Width, 0, chunk.chunkPosition.z * Chunk.Width);
            chunk.gameObject.transform.SetParent(worldTransform, true);

            chunk.gameObject.AddComponent<MeshFilter>();
            chunk.gameObject.AddComponent<MeshRenderer>().sharedMaterial = settings.worldMaterial;
            chunk.gameObject.AddComponent<MeshCollider>();
        }
        
        chunk.gameObject.GetComponent<MeshFilter>().sharedMesh = meshData;
        chunk.gameObject.GetComponent<MeshCollider>().sharedMesh = meshData;

        chunk.isMeshGenerated = true;
    }

    /// <summary>
    /// Публичный метод для получения чанка по координатам. Нужен для VoxelGenerationPipeline для проверки соседей.
    /// </summary>
    public Chunk GetChunk(Vector3Int pos) 
    {
        activeChunks.TryGetValue(pos, out Chunk chunk);
        return chunk;
    }
    
    public void Dispose()
    {
        if (pipeline != null)
        {
            this.pipeline.OnChunkMeshReady -= HandleChunkMeshReady;
        }
    }
}