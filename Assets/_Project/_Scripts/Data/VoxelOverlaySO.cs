// --- НОВЫЙ ФАЙЛ: VoxelOverlaySO.cs ---
using UnityEngine;

/// <summary>
/// ScriptableObject, описывающий "наложение" свойств на базовый воксель.
/// Используется для руд, кристаллов, мха и других эффектов, которые
/// влияют на внешний вид ландшафта, не являясь самостоятельным блоком.
/// </summary>
[CreateAssetMenu(fileName = "NewVoxelOverlay", menuName = "Procedural Voxel World/Voxel Overlay")]
public class VoxelOverlaySO : ScriptableObject
{
    [Header("Идентификация и Приоритет")]
    [Tooltip("Уникальный ID этого наложения. Нужен для быстрой выборки в Jobs.")]
    public ushort OverlayID;

    [Tooltip("Приоритет наложения. Чем выше значение, тем 'важнее' это наложение. При смешивании нескольких влияний в одной точке, победит то, у которого выше приоритет.")]
    public int Priority = 100;

    [Header("Визуальные свойства для наложения")]
    [Tooltip("Цвет для тонирования. Будет смешиваться с цветом базового вокселя.")]
    public Color TintColor = Color.white;
    
    [Tooltip("Координаты текстуры в общем атласе для этого наложения.")]
    public Vector2Int textureAtlasCoord;

    [Header("Параметры влияния на PBR материал")]
    [Tooltip("Гладкость (Smoothness), которую это наложение передает.")]
    [Range(0f, 1f)] 
    public float Smoothness = 0.5f;

    [Tooltip("Металличность (Metallic), которую это наложение передает.")]
    [Range(0f, 1f)] 
    public float Metallic = 0.0f;

    [Header("Параметры влияния на швы (Gap)")]
    [Tooltip("Ширина 'шва' (Gap), которую это наложение проецирует на ландшафт.")]
    [Range(0f, 0.1f)] 
    public float GapWidth = 0.0f;

    [Tooltip("Цвет 'шва' (Gap), который будет использоваться, если это наложение доминирует.")]
    public Color GapColor = new Color(0.1f, 0.1f, 0.1f, 1.0f);

    [Header("Параметры влияния на свечение (Emission)")]
    [Tooltip("Цвет свечения, который добавляется к базовому вокселю.")]
    [ColorUsage(true, true)] // Позволяет настраивать HDR цвета в инспекторе
    public Color EmissionColor = Color.black;

    [Tooltip("Сила свечения (Emission).")]
    [Range(0f, 10f)]
    public float EmissionStrength = 0f;

    [Header("Параметры влияния на Bevel (фаску)")]
    [Tooltip("Ширина фаски, которую это наложение проецирует на воксель.")]
    [Range(0.01f, 0.49f)] public float BevelWidth = 0.1f;

    [Tooltip("Сила эффекта фаски. Управляет интенсивностью затемнения/высветления.")]
    [Range(0.0f, 1.0f)] public float BevelStrength = 0.8f;

    [Tooltip("Направление фаски. -1 = затемнение (тень), 1 = высветление (блик).")]
    [Range(-1f, 1f)] public float BevelDirection = -1.0f;
}