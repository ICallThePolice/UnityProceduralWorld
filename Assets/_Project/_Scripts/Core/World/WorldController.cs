// --- ФАЙЛ: WorldController.cs (ФИНАЛЬНАЯ ИСПРАВЛЕННАЯ ВЕРСИЯ) ---
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine.Rendering;

public class WorldController : MonoBehaviour
{
    public static WorldController Instance { get; private set; }

    public WorldSettingsSO worldSettings;
    public Transform player;

    [Header("UI")]
    public RawImage minimapDisplay;
    [Tooltip("Как часто (в секундах) миникарта будет пытаться обновиться.")]
    public float minimapUpdateInterval = 0.5f;
    [Tooltip("Масштаб миникарты. 1.0 = 1 пиксель на 1 юнит мира.")]
    public float minimapScale = 2.0f;

    private ChunkManager chunkManager;
    private VoxelGenerationPipeline generationPipeline;
    private float chunkUpdateTimer;

    private void Awake()
    {
        Instance = this;
        if (worldSettings == null || player == null || BiomeManager.Instance == null) 
        { 
            Debug.LogError("Ключевые компоненты не назначены в WorldController!");
            this.enabled = false; 
            return; 
        }

        SetupShaderGlobals();
        
        BiomeManager.Instance.Initialize(worldSettings); 
        generationPipeline = new VoxelGenerationPipeline(worldSettings, BiomeManager.Instance);
        chunkManager = new ChunkManager(generationPipeline, worldSettings, this.transform);
    }
    
    private void Start()
    {
        if (minimapDisplay != null && worldSettings.biomeMapComputeShader != null)
        {
            StartCoroutine(MinimapUpdateRoutine());
        }
        else
        {
            Debug.LogError("Миникарта или ее Compute Shader не назначены, корутина не будет запущена.");
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
        
        generationPipeline.ProcessQueues(chunkManager.GetChunk);
        generationPipeline.CheckCompletedJobs(chunkManager.OnChunkDataReady);
    }
    
    private void OnDestroy()
    {
        generationPipeline?.Dispose();
        chunkManager?.Dispose();
    }
    
    private IEnumerator MinimapUpdateRoutine()
    {
        var mapDescriptor = new RenderTextureDescriptor(512, 512, RenderTextureFormat.ARGB32) { enableRandomWrite = true };
        RenderTexture minimapTexture = new RenderTexture(mapDescriptor);
        minimapTexture.Create();
        minimapDisplay.texture = minimapTexture;

        var biomeMapCompute = worldSettings.biomeMapComputeShader;
        int kernel = biomeMapCompute.FindKernel("VisualizeGrid");

        while (true)
        {
            // ИСПРАВЛЕНО: Используем BiomeManager.Instance для доступа к regionSize
            int regionX = Mathf.FloorToInt(player.position.x / BiomeManager.Instance.regionSize);
            int regionZ = Mathf.FloorToInt(player.position.z / BiomeManager.Instance.regionSize);
            Vector2Int playerRegion = new Vector2Int(regionX, regionZ);
            
            Task<Dictionary<int2, int>> simulationTask = BiomeManager.Instance.GetBiomeSiteGridFor(playerRegion);
            
            while (!simulationTask.IsCompleted)
            {
                yield return null;
            }

            Dictionary<int2, int> siteGrid = simulationTask.Result;
            
            if (siteGrid != null && siteGrid.Count > 0)
            {
                var siteDataList = new List<float4>();
                foreach (var kvp in siteGrid)
                {
                    var agent = BiomeManager.Instance.GetAgent(kvp.Value);
                    if (agent != null)
                    {
                       siteDataList.Add(new float4(kvp.Key.x, kvp.Key.y, agent.settings.biome.BiomeBlock.ID, agent.isPaired ? 1 : 0));
                    }
                }
                
                if (siteDataList.Count > 0)
                {
                    var siteBuffer = new ComputeBuffer(siteDataList.Count, sizeof(float) * 4);
                    siteBuffer.SetData(siteDataList);

                    biomeMapCompute.SetBuffer(kernel, "_SiteData", siteBuffer);
                    biomeMapCompute.SetInt("_SiteCount", siteDataList.Count);
                    biomeMapCompute.SetTexture(kernel, "_Result", minimapTexture);
                    biomeMapCompute.SetVector("_PlayerPos", new float4(player.position.x, player.position.z, 0, 0));
                    biomeMapCompute.SetFloat("_MinimapScale", minimapScale);
                    biomeMapCompute.Dispatch(kernel, 512 / 8, 512 / 8, 1);
                    
                    siteBuffer.Release();
                }
            }
            
            yield return new WaitForSeconds(minimapUpdateInterval);
        }
    }

    private void SetupShaderGlobals()
    {
        if (worldSettings.worldMaterial != null && worldSettings.worldTextureAtlas != null)
        {
            worldSettings.worldMaterial.SetTexture("_MainTex", worldSettings.worldTextureAtlas);
        }
    }

    private Vector3Int GetChunkPositionFromWorldPos(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt(worldPosition.x / Chunk.Width);
        int z = Mathf.FloorToInt(worldPosition.z / Chunk.Width);
        return new Vector3Int(x, 0, z);
    }
}