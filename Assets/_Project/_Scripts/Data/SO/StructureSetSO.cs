using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ScriptableObject, который содержит набор правил и/или префабов для генерации
/// процедурных структур (например, деревень, руин, лесов).
/// </summary>
[CreateAssetMenu(fileName = "NewStructureSet", menuName = "Procedural Voxel World/Structure Set")]
public class StructureSetSO : ScriptableObject
{
    [Header("Набор структур")]
    [Tooltip("Список префабов, которые могут быть размещены в мире как часть этого набора.")]
    public List<GameObject> StructurePrefabs;

    // Примечание для будущего расширения:
    // Позже сюда можно будет добавить более сложные наборы правил,
    // например, для алгоритма Wave Function Collapse (WFC), как указано в документе.
    // public WfcRuleset wfcRules;
}