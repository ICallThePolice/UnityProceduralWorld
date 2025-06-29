// CustomDoFRendererFeature.cs (Stable API Version)
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class CustomDoFSettings
{
    [Header("Focus Settings")]
    [Tooltip("Расстояние от камеры до точки фокуса (персонажа)")]
    public float FocusDistance = 16f;

    [Tooltip("Размер полностью резкой зоны вокруг точки фокуса (+- это значение)")]
    [Range(0.1f, 50f)]
    public float SharpRange = 16f;

    [Tooltip("Размер переходной зоны от резкости к полному размытию")]
    [Range(0.1f, 50f)]
    public float BlurFalloff = 8f;
}

public class CustomDoFRendererFeature : ScriptableRendererFeature
{
    public CustomDoFSettings settings = new CustomDoFSettings();
    private CustomDoFPass m_CustomDoFPass;

    public override void Create()
    {
        m_CustomDoFPass = new CustomDoFPass(settings);
        m_CustomDoFPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (m_CustomDoFPass.HasMaterial())
        {
            // --- ИЗМЕНЕНО: Мы снова просто добавляем пасс в очередь. Это самый надежный способ. ---
            renderer.EnqueuePass(m_CustomDoFPass);
        }
    }
    
    protected override void Dispose(bool disposing)
    {
        m_CustomDoFPass.Dispose();
        base.Dispose(disposing);
    }
}

// --- ИЗМЕНЕНО: Мы вернулись к наследованию от ScriptableRenderPass ---
public class CustomDoFPass : ScriptableRenderPass
{
    private Material m_Material;
    private CustomDoFSettings m_Settings;

    // --- ИЗМЕНЕНО: Идентификатор для временной текстуры ---
    private int m_TempTextureID = Shader.PropertyToID("_TempColorTexture");
    
    public CustomDoFPass(CustomDoFSettings settings)
    {
        m_Settings = settings;
        m_Material = CoreUtils.CreateEngineMaterial("Hidden/CustomDoF");
    }

    public bool HasMaterial()
    {
        return m_Material != null;
    }
    
    public void Dispose()
    {
        CoreUtils.Destroy(m_Material);
    }

    // --- ИЗМЕНЕНО: Execute снова используется, но с современным содержанием ---
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (!HasMaterial()) return;
        
        var cmd = CommandBufferPool.Get("CustomDoF");

        m_Material.SetFloat("_FocusDistance", m_Settings.FocusDistance);
        m_Material.SetFloat("_SharpRange", m_Settings.SharpRange);
        m_Material.SetFloat("_BlurFalloff", m_Settings.BlurFalloff);

        var camera = renderingData.cameraData.camera;
        var viewProjectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * camera.worldToCameraMatrix;
        m_Material.SetMatrix("_InverseViewProjectionMatrix", viewProjectionMatrix.inverse);
        
        // Эта строка по-прежнему будет давать ПРЕДУПРЕЖДЕНИЕ (желтая линия), и это нормально в данном контексте.
        // Оно говорит нам, что мы в режиме совместимости.
        var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
        
        var descriptor = renderingData.cameraData.cameraTargetDescriptor;
        m_Material.SetVector("_MainTex_TexelSize", new Vector4(1f / descriptor.width, 1f / descriptor.height, descriptor.width, descriptor.height));
        descriptor.depthBufferBits = 0;

        cmd.GetTemporaryRT(m_TempTextureID, descriptor, FilterMode.Bilinear);
        var tempTarget = new RenderTargetIdentifier(m_TempTextureID);

        // --- ИЗМЕНЕНО: Просто передаем 'source' напрямую, без '.Identifier()' ---
        // Это исправит КРАСНУЮ ОШИБКУ компиляции.
        cmd.Blit(source, tempTarget, m_Material);
        cmd.Blit(tempTarget, source);
        
        cmd.ReleaseTemporaryRT(m_TempTextureID);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
}