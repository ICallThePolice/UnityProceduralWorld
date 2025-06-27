using UnityEngine;

/// <summary>
/// ScriptableObject, определяющий базовый тип вокселя (например, "Камень", "Земля").
/// Хранит только основные, несмешиваемые свойства.
/// </summary>
[CreateAssetMenu(fileName = "NewVoxelType", menuName = "Procedural Voxel World/Voxel Type")]
public class VoxelTypeSO : ScriptableObject
{
    [Header("Идентификация")]
    [Tooltip("Уникальный числовой ID вокселя. Используется для экономии памяти в данных чанка.")]
    public ushort ID;

    [Tooltip("Имя, отображаемое в редакторе и, возможно, в игре.")]
    public string VoxelName;
    
    // Категория все еще полезна для правил генерации
    [Tooltip("Категория вокселя. Используется для группировки и фильтрации типов в редакторе.")]
    public VoxelCategory category = VoxelCategory.Landscape;

    [Header("Физические свойства")]
    [Tooltip("Является ли воксель твердым? Влияет на физику и генерацию меша.")]
    public bool isSolid = true;

    [Tooltip("Является ли воксель прозрачным? Влияет на алгоритм отсечения граней (Culled Meshing).")]
    public bool isTransparent = false;

    [Header("Базовые визуальные данные")]
    [Tooltip("Координаты основной текстуры для этого блока в общем текстурном атласе.")]
    public Vector2Int textureAtlasCoord;

    [Tooltip("Базовый цвет вокселя. Может быть тонирован наложениями (Overlays).")]
    public Color baseColor = Color.white;
    
    [Tooltip("Звук, который проигрывается при разрушении этого типа вокселя.")]
    public AudioClip destructionSound;
}