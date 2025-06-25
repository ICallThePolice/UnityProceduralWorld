// --- Файл Chunk.cs ---
using UnityEngine;
using System; // Необходимо для Buffer
using System.Linq; // Необходимо для .Cast<>

public class Chunk
{
    public const int Width = 16;
    public const int Height = 48;

    public readonly Vector3Int chunkPosition;
    public GameObject gameObject;

    public Mesh meshData;

    private readonly ushort[,,] voxelIDs;

    public bool isDataGenerated = false;
    public bool isMeshGenerated = false;
    public bool isModifiedByPlayer = false;
    public float lastActiveTime;

    public Chunk(Vector3Int position)
    {
        this.chunkPosition = position;
        this.voxelIDs = new ushort[Width, Height, Width];
        this.lastActiveTime = Time.time;
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