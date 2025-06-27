using UnityEngine;

/// <summary>
/// ScriptableObject, определяющий один тип вокселя (например, "Камень", "Земля").
/// Этот ассет хранит все данные, касающиеся конкретного типа блока,
/// отделяя данные от логики согласно архитектурному плану. 
/// </summary>
[CreateAssetMenu(fileName = "NewVoxelType", menuName = "Procedural Voxel World/Voxel Type")]
public class VoxelTypeSO : ScriptableObject
{
    [Header("Идентификация")]
    [Tooltip("Уникальный числовой ID вокселя. Используется для экономии памяти в данных чанка.")]
    public ushort ID; // 

    [Tooltip("Имя, отображаемое в редакторе и, возможно, в игре.")]
    public string VoxelName;
    [Tooltip("Категория вокселя. Используется для группировки и фильтрации типов в редакторе.")]
    public VoxelCategory category = VoxelCategory.Landscape;

    [Header("Физические свойства")]
    [Tooltip("Является ли воксель твердым? Влияет на физику и генерацию меша.")]
    public bool isSolid = true; // 

    [Tooltip("Является ли воксель прозрачным? Влияет на алгоритм отсечения граней (Culled Meshing).")]
    public bool isTransparent = false; // 

    [Header("Визуальные и звуковые данные")]
    [Tooltip("Координаты основной текстуры для этого блока в общем текстурном атласе.")]
    public Vector2Int textureAtlasCoord; // 
    // Примечание: для более сложных блоков можно добавить отдельные координаты для верха, низа и боковых граней.

    [Tooltip("Звук, который проигрывается при разрушении этого типа вокселя.")]
    public AudioClip destructionSound;
    public Color color = Color.white;

    [Header("Настройки материала")]
    public Color gapColor = new Color(0.1f, 0.1f, 0.1f, 1.0f); // Цвет швов/зазоров
    [Range(0f, 1f)] public float smoothness = 0.5f;             // Гладкость/глянцевость
    [Range(0f, 1f)] public float metallic = 0.0f;               // Металличность

    [Header("Настройки свечения (Emission)")]
    [ColorUsage(true, true)] // Этот атрибут даст красивый HDR-колорпикер
    public Color emissionColor = Color.black; // По умолчанию блоки не светятся
    public float emissionStrength = 0f;
}