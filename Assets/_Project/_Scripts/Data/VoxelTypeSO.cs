using UnityEngine;

[CreateAssetMenu(fileName = "NewVoxelType", menuName = "Procedural Voxel World/Voxel Type")]
public class VoxelTypeSO : ScriptableObject
{
    [Header("Идентификация")]
    public ushort ID;
    public string VoxelName;
    public VoxelCategory category = VoxelCategory.Landscape;

    [Header("Физические свойства")]
    public bool isSolid = true;
    public bool isTransparent = false;

    [Header("Базовые визуальные данные")]
    public Vector2Int textureAtlasCoord;
    public Color baseColor = Color.white;
    
    // --- ДОБАВЛЕНО: Уникальные настройки Gap для ландшафтных блоков ---
    [Header("Настройки швов (Gap) для ландшафта")]
    [Range(0f, 0.1f)] public float GapWidth = 0.0f;
    public Color GapColor = new Color(0.1f, 0.1f, 0.1f, 1.0f);
    
    [Header("Прочее")]
    public AudioClip destructionSound;
}