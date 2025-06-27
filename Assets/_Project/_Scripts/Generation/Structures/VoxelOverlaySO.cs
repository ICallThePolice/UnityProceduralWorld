// --- ФАЙЛ: VoxelOverlaySO.cs ---
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

    [Tooltip("Приоритет наложения. Чем выше значение, тем сильнее влияние. При смешивании нескольких наложений, это значение будет определять 'победителя'.")]
    public int Priority = 100;

    [Header("Визуальные свойства для наложения")]
    [Tooltip("Текстура, которая будет смешиваться с текстурой базового вокселя.")]
    public Texture2D OverlayTexture; // Примечание: это потребует изменений в атласинге или шейдере

    [Tooltip("Цвет для тонирования. Будет смешиваться с цветом базового вокселя.")]
    public Color TintColor = Color.white;

    [Header("Параметры влияния на геометрию и швы")]
    [Tooltip("Сила 'шва' (Gap), которую это наложение проецирует на ландшафт. 0 = нет влияния.")]
    [Range(0f, 0.1f)] public float GapWidth = 0.0f;

    [Tooltip("Цвет 'шва' (Gap), который будет использоваться при смешивании.")]
    public Color GapColor = new Color(0.1f, 0.1f, 0.1f, 1.0f);

    [Header("Параметры влияния на материал")]
    [Tooltip("Гладкость (Smoothness), которую это наложение передает базовому материалу.")]
    [Range(0f, 1f)] public float Smoothness = 0.5f;

    [Tooltip("Металличность (Metallic), которую это наложение передает базовому материалу.")]
    [Range(0f, 1f)] public float Metallic = 0.0f;

    [Header("Параметры влияния на свечение")]
    [Tooltip("Цвет свечения (Emission), который добавляется к базовому вокселю.")]
    [ColorUsage(true, true)]
    public Color EmissionColor = Color.black;

    [Tooltip("Сила свечения (Emission).")]
    public float EmissionStrength = 0f;

    [Header("Параметры влияния на Bevel (фаску)")]
    [Tooltip("Ширина фаски, которую это наложение проецирует на воксель.")]
    [Range(0.01f, 0.49f)] public float BevelWidth = 0.1f;

    [Tooltip("Сила эффекта фаски. Управляет интенсивностью затемнения/высветления.")]
    [Range(0.0f, 1.0f)] public float BevelStrength = 0.8f;

    [Tooltip("Инверсия фаски. -1 = затемнение (тень), 1 = высветление (блик).")]
    [Range(-1f, 1f)] public float BevelDirection = -1.0f;
}