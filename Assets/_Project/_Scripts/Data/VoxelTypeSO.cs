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

    [Header("Настройки фаски (Bevel)")]
    [Tooltip("Ширина фаски для этого типа ландшафта.")]
    [Range(0.01f, 0.49f)] 
    public float BevelWidth = 0.1f;

    [Tooltip("Сила эффекта фаски. Управляет интенсивностью затемнения/высветления.")]
    [Range(0.0f, 1.0f)] 
    public float BevelStrength = 0.8f;

    [Tooltip("Направление фаски. -1 = затемнение (тень), 1 = высветление (блик).")]
    [Range(-1f, 1f)] 
    public float BevelDirection = -1.0f;
    
    [Header("Прочее")]
    public AudioClip destructionSound;
}