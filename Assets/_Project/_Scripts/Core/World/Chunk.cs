// --- Файл Chunk.cs ---
using UnityEngine;
using System;
using System.Linq;

public class Chunk
{
    public const int Width = 16;
    public const int Height = 48;

    public readonly Vector3Int chunkPosition;
    public GameObject gameObject;
    public Color32[] voxelColors;

    public Mesh meshData; // Ссылка на меш, которую нужно будет очищать

    private readonly ushort[,,] voxelIDs;

    public bool isDataGenerated = false;
    public bool isMeshGenerated = false;
    public bool isModifiedByPlayer = false;
    public float lastActiveTime; // Этот таймер мы будем использовать для оптимизации

    public Chunk(Vector3Int position)
    {
        this.chunkPosition = position;
        this.voxelIDs = new ushort[Width, Height, Width];
        this.voxelColors = new Color32[Width * Height * Width];
        this.lastActiveTime = Time.time;
    }
    
    // --- НОВЫЙ МЕТОД ---
    /// <summary>
    /// Освобождает все ресурсы, связанные с чанком.
    /// </summary>
    public void Dispose()
    {
        if (gameObject != null)
        {
            GameObject.Destroy(gameObject);
        }
        if (meshData != null)
        {
            // Эта самая важная строка для исправления утечки памяти!
            GameObject.Destroy(meshData);
        }
    }

    public ushort GetVoxelID(int x, int y, int z)
    {
        return voxelIDs[x, y, z];
    }

    public void SetVoxelID(int x, int y, int z, ushort id)
    {
        voxelIDs[x, y, z] = id;
        isModifiedByPlayer = true;
    }

    public ushort[] GetAllVoxelIDs()
    {
        ushort[] flatVoxels = new ushort[Width * Height * Width];
        Buffer.BlockCopy(voxelIDs, 0, flatVoxels, 0, flatVoxels.Length * sizeof(ushort));
        return flatVoxels;
    }

    public void SetAllVoxelIDs(ushort[] allVoxels)
    {
        if (allVoxels.Length != voxelIDs.Length)
        {
            return;
        }
        Buffer.BlockCopy(allVoxels, 0, voxelIDs, 0, allVoxels.Length * sizeof(ushort));
    }
}