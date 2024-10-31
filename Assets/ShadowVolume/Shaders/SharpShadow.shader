Shader "SharpShadow/StencilShadows"
 
{
    Properties
    {  
    }

    SubShader
    {
        Pass
        {
            Name "StencilPass"
            Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

            
            Stencil
            {
                ZFailBack IncrWrap
                ZFailFront DecrWrap
            }
            
            ColorMask 0
            Cull Off
            ZWrite Off
        }
    }
}
