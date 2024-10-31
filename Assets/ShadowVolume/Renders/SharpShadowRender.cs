using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Events;


    [Serializable]
    public class SharpShadowSettings
    {
        [SerializeField] internal RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            
    }
    
    public class SharpShadowRender : ScriptableRendererFeature
    {
        public static Action renderCreated = null;
        
        public SharpShadowSettings Settings => m_Settings;
        
        [SerializeField] private Material ShadowMaterial;
        [SerializeField] private SharpShadowSettings m_Settings = new SharpShadowSettings();
        
        private Material m_Material;
        private ScreenRenderPass m_SVRPass = null; //Visualize Shadows
        private static Mesh _fullscreenMesh;

        private static Mesh GetFullscreenMesh()
        {
            if (_fullscreenMesh != null)
                return _fullscreenMesh;

            _fullscreenMesh = new Mesh();
            _fullscreenMesh.vertices = new Vector3[] {
                new Vector3(-1, -1, 0),
                new Vector3(1, -1, 0),
                new Vector3(1, 1, 0),
                new Vector3(-1, 1, 0)
            };
            _fullscreenMesh.triangles = new int[] { 0, 1, 2, 2, 3, 0 };

            // Ensure the mesh is marked as read-only to prevent accidental modifications.
            _fullscreenMesh.MarkDynamic(); // Mark the mesh as dynamic if needed.

            return _fullscreenMesh;
        }

        public override void Create()
        {

            m_SVRPass = new ScreenRenderPass(m_Settings.renderPassEvent + 10, "Visualize Shadows");

            GetMaterial();
            
            GetFullscreenMesh();
        }
        
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (GetMaterial())
            {
                renderer.EnqueuePass(m_SVRPass);
            } 
        }
        
        private bool GetMaterial()
        {
            if (m_Material != null && m_SVRPass.material != null)
            {
                return true;
            }

            m_Material = ShadowMaterial;
            m_SVRPass.material = m_Material;
            return m_Material != null;
        }
        
        public static bool RenderRefreshed()
        {
            if (renderCreated != null)
            {
                renderCreated.Invoke();
                return true;
            }
            return false;
        }
        
        private class SharpShadowRenderPass : ScriptableRenderPass
        {
            internal LayerMask layer;
            private List<ShaderTagId> tags;
            private ProfilingSampler sampler;
            
            internal SharpShadowRenderPass(RenderPassEvent renderPassEvent, string[] tags, string sampler)
            {
                this.renderPassEvent = renderPassEvent;
                this.sampler = new ProfilingSampler(sampler);
                this.tags = new List<ShaderTagId>();
                foreach (var tag in tags)
                {
                    this.tags.Add(new ShaderTagId(tag));
                }
            }
            
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (tags == null || tags.Count == 0)
                {
                    Debug.LogError($"{GetType().Name}.Execute(): Missing tags. {GetType().Name} render pass " +
                                   $"will not execute. Check for missing reference in the renderer resources.");
                    return;
                }
                
                CommandBuffer cmd = CommandBufferPool.Get("Effects Renderers Pass");
                
                using (new ProfilingScope(cmd, sampler))
                {
                    DrawingSettings drawingSettings = CreateDrawingSettings(tags, ref renderingData, SortingCriteria.CommonOpaque);
                    drawingSettings.enableDynamicBatching = true;
                    FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.opaque, layer);
                    RenderStateBlock renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
                    context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings, ref renderStateBlock);
                }
                
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
            
            public override void OnCameraCleanup(CommandBuffer cmd)
            {
                if (cmd == null)
                {
                    throw new ArgumentNullException("cmd");
                }
            }
        }
        
        private class ScreenRenderPass : ScriptableRenderPass
        {
            internal Material material;
            
            private ProfilingSampler sampler;

            internal ScreenRenderPass(RenderPassEvent renderPassEvent, string sampler)
            {
                this.renderPassEvent = renderPassEvent;
                this.sampler = new ProfilingSampler(sampler);
            }
            
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (material == null)
                {
                    Debug.LogError($"{GetType().Name}.Execute(): Missing material. {GetType().Name} render pass " +
                                   $"will not execute. Check for missing reference in the renderer resources.");
                    return;
                }

                CommandBuffer cmd = CommandBufferPool.Get("Screen Render Pass");
                Camera camera = renderingData.cameraData.camera;

                using (new ProfilingScope(cmd, sampler))
                {
                    cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                    cmd.DrawMesh(GetFullscreenMesh(), Matrix4x4.identity, material);
                    cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
                }
                
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
            
            public override void OnCameraCleanup(CommandBuffer cmd)
            {
                if (cmd == null)
                {
                    throw new ArgumentNullException(nameof(cmd));
                }
            }
        }
    }

