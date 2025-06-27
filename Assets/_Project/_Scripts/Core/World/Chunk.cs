// --- ФАЙЛ: Chunk.cs (ИСПРАВЛЕННАЯ ВЕРСИЯ) ---
using UnityEngine;
using Unity.Mathematics;

public class Chunk
{
    public const int Width = 16;
    public const int Height = 48;
    public const int Size = Width * Height * Width;

    public readonly Vector3Int chunkPosition;
    public GameObject gameObject;
    public Mesh meshData;

    public bool isDataGenerated = false;
    public bool isMeshGenerated = false;
    public float lastActiveTime;

    // --- ИСПРАВЛЕНО: Теперь храним только ID. Вся визуальная информация вычисляется в MeshingJob ---
    public ushort[] primaryBlockIDs;
    public ushort[] overlayIDs;

    public Chunk(Vector3Int position)
    {
        this.chunkPosition = position;
        this.lastActiveTime = Time.time;
        this.primaryBlockIDs = new ushort[Size];
        this.overlayIDs = new ushort[Size];
    }
    
    public static int GetVoxelIndex(int x, int y, int z) { return y + z * Height + x * Height * Width; }
    
    public void Dispose()
    {
        if (gameObject != null) { GameObject.Destroy(gameObject); }
        if (meshData != null) { GameObject.Destroy(meshData); }
    }
}