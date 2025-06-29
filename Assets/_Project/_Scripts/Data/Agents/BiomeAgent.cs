using Unity.Mathematics;
using UnityEngine;

public enum AgentStatus { Growing, Paused, Paired, Merged, Stalled, Culled }

public class BiomeAgent
{
    public readonly int uniqueInstanceId;
    public readonly BiomePlacementSettingsSO settings;
    public AgentStatus status;
    public float2 position;
    public float currentRadius;
    public float maxRadius;
    public int partnerId = -1;
    public int clusterId = -1;

    public BiomeAgent(int id, float2 pos, BiomePlacementSettingsSO biomeSettings, float initialAggressiveness)
    {
        this.uniqueInstanceId = id;
        this.settings = biomeSettings;
        this.position = pos;
        this.status = AgentStatus.Growing;
        this.currentRadius = 1.0f;
        this.clusterId = id;

        // ИСПРАВЛЕНИЕ: Агрессивность теперь работает как множитель
        float baseRadius = Mathf.Lerp(settings.influenceRadius.x, settings.influenceRadius.y, 0.5f);
        // Формула (1.0f + initialAggressiveness * 4.0f) дает множитель от 1.0 до 5.0
        // Вы можете легко ее изменить, чтобы настроить влияние агрессивности.
        this.maxRadius = baseRadius * (1.0f + initialAggressiveness * 4.0f);
    }

    public void Grow(float growthStep)
    {
        if (status == AgentStatus.Growing && currentRadius < maxRadius)
        {
            currentRadius += growthStep;
            if (currentRadius >= maxRadius)
            {
                currentRadius = maxRadius;
                status = AgentStatus.Stalled;
            }
        }
    }
}