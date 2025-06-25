// --- ФАЙЛ: WorldController.cs (ФИНАЛЬНАЯ РЕФАКТОРИНГ-ВЕРСИЯ) ---
using UnityEngine;

public class WorldController : MonoBehaviour
{
    public static WorldController Instance { get; private set; }

    [Tooltip("Перетащите сюда ассет со всеми настройками мира")]
    public WorldSettingsSO worldSettings; // Ассет с настройками мира, который содержит все необходимые параметры
    public Transform player; // Ссылка на игрока, чтобы знать, где он находится в мире

    private ChunkManager chunkManager; // Менеджер чанков, который управляет их созданием и удалением
    private VoxelGenerationPipeline generationPipeline; // Конвейер генерации вокселей
    private float chunkUpdateTimer; // Таймер для обновления чанков

    private void Awake()
    {
        // 1. Инициализируем сам WorldController
        Instance = this;

        // 2. Проверяем его собственные зависимости
        if (worldSettings == null || player == null)
        {
            this.enabled = false;
            return;
        }

        // 3. Проверяем, что BiomeManager уже существует (его Awake выполнился благодаря Script Execution Order)
        if (BiomeManager.Instance == null)
        {
            this.enabled = false;
            return;
        }

        // 4. Явно инициализируем BiomeManager, передавая ему нужные настройки.
        // Теперь BiomeManager не ищет WorldController, а наоборот.
        BiomeManager.Instance.Initialize(worldSettings);

        // 5. Создаем остальные компоненты, которые зависят от настроенных менеджеров
        generationPipeline = new VoxelGenerationPipeline(worldSettings, BiomeManager.Instance);
        chunkManager = new ChunkManager(generationPipeline, worldSettings, this.transform);

    }

    private void Update()
    {
        // Обновляем таймер
        chunkUpdateTimer -= Time.deltaTime;

        // Логика управления чанками (загрузка/выгрузка) теперь выполняется по таймеру
        if (chunkUpdateTimer <= 0f)
        {
            chunkUpdateTimer = worldSettings.chunkUpdateInterval; // Сбрасываем таймер
            var playerChunkPosition = GetChunkPositionFromWorldPos(player.position);
            chunkManager.Update(playerChunkPosition);
        }
        
        // А вот обработка очередей и завершенных задач должна выполняться каждый кадр,
        // чтобы мир генерировался плавно и без рывков.
        var currentChunkPos = GetChunkPositionFromWorldPos(player.position); // Позиция нужна для сортировки
        generationPipeline.ProcessQueues(currentChunkPos, chunkManager.GetChunk);
        generationPipeline.CheckCompletedJobs(chunkManager.OnChunkDataReady);
    }

    private void OnDestroy()
{
    // Проверяем, были ли менеджеры вообще созданы, перед тем как их очищать
    if (generationPipeline != null)
    {
        generationPipeline.Dispose();
    }
    if (chunkManager != null)
    {
        chunkManager.Dispose();
    }
}

    private Vector3Int GetChunkPositionFromWorldPos(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt(worldPosition.x / Chunk.Width);
        int z = Mathf.FloorToInt(worldPosition.z / Chunk.Width);
        return new Vector3Int(x, 0, z);
    }
}