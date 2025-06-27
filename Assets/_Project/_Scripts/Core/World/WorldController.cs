// --- ФАЙЛ: WorldController.cs (ИСПРАВЛЕННАЯ ВЕРСИЯ) ---
using UnityEngine;

public class WorldController : MonoBehaviour
{
    public static WorldController Instance { get; private set; }

    public WorldSettingsSO worldSettings;
    public Transform player;

    private ChunkManager chunkManager;
    private VoxelGenerationPipeline generationPipeline;
    private float chunkUpdateTimer;

    private void Awake()
    {
        Instance = this;
        if (worldSettings == null || player == null) { this.enabled = false; return; }
        if (BiomeManager.Instance == null) { this.enabled = false; return; }
        
        SetupShaderGlobals();
        
        BiomeManager.Instance.Initialize(worldSettings);
        generationPipeline = new VoxelGenerationPipeline(worldSettings, BiomeManager.Instance);
        chunkManager = new ChunkManager(generationPipeline, worldSettings, this.transform);
    }
    
    private void SetupShaderGlobals()
    {
        if (worldSettings.worldMaterial != null && worldSettings.worldTextureAtlas != null)
        {
            worldSettings.worldMaterial.SetTexture("_MainTex", worldSettings.worldTextureAtlas);
        }
    }
    
    private void Update()
    {
        chunkUpdateTimer -= Time.deltaTime;
        if (chunkUpdateTimer <= 0f)
        {
            chunkUpdateTimer = worldSettings.chunkUpdateInterval;
            var playerChunkPosition = GetChunkPositionFromWorldPos(player.position);
            chunkManager.Update(playerChunkPosition);
        }

        var currentChunkPos = GetChunkPositionFromWorldPos(player.position);
        generationPipeline.ProcessQueues(currentChunkPos, chunkManager.GetChunk);
        generationPipeline.CheckCompletedJobs(chunkManager.OnChunkDataReady);
    }

    private void OnDestroy()
    {
        generationPipeline?.Dispose();
        chunkManager?.Dispose();
    }

    private Vector3Int GetChunkPositionFromWorldPos(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt(worldPosition.x / Chunk.Width);
        int z = Mathf.FloorToInt(worldPosition.z / Chunk.Width);
        return new Vector3Int(x, 0, z);
    }
}