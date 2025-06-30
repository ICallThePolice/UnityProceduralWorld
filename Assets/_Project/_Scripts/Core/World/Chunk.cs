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
    public bool isDataJobRunning = false;
    public bool isMeshGenerated = false;
    public bool isMeshJobRunning = false;
    public float lastActiveTime;

    // --- Храним все вычисленные данные ---
    public ushort[] primaryBlockIDs;
    public Color32[] finalColors;
    public float2[] finalUv0s;
    public float2[] finalUv1s;
    public float[] finalTexBlends;
    public float4[] finalEmissionData;
    public float4[] finalGapColors;
    public float2[] finalMaterialProps;
    public float[] finalGapWidths;
    public float3[] finalBevelData;

    public Chunk(Vector3Int position)
    {
        this.chunkPosition = position;
        this.lastActiveTime = Time.time;

        primaryBlockIDs = new ushort[Size];
        finalColors = new Color32[Size];
        finalUv0s = new float2[Size];
        finalUv1s = new float2[Size];
        finalTexBlends = new float[Size];
        finalEmissionData = new float4[Size];
        finalGapColors = new float4[Size];
        finalMaterialProps = new float2[Size];
        finalGapWidths = new float[Size];
        finalBevelData = new float3[Size];
    }
    
    public static int GetVoxelIndex(int x, int y, int z) { return y + Height * (z + Width * x); }
    
    public void Dispose()
    {
        if (gameObject != null) { GameObject.Destroy(gameObject); }
        if (meshData != null) { GameObject.Destroy(meshData); }
    }
}