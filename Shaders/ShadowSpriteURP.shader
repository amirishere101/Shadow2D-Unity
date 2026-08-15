// Universal Render Pipeline variant of DryFlyStudio/ShadowSprite.
//
// Same unlit, stencil-merged shadow, but tagged for URP so the renderer draws it in
// a real Universal2D / UniversalForward pass. The built-in variant does still render
// under URP - it just isn't part of any URP LightMode pass, which is why it batches
// and sorts unpredictably there.
//
// Whether overlapping shadows merge under URP depends on the 2D Renderer keeping the
// depth-stencil attachment across its light-blend passes, which varies by URP version
// and renderer settings. Verify it in your project before relying on it; if overlaps
// still double-darken, set Stencil Comparison to Always and accept the seam.
//
// Deliberately written against UnityCG.cginc rather than URP's ShaderLibrary: those
// includes only exist when com.unity.render-pipelines.universal is installed, and a
// missing include is a hard shader compile error in every project that isn't on URP.
// The RenderPipeline tag already keeps this shader out of built-in RP projects.
Shader "DryFlyStudio/URP/ShadowSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0

        [Header(Self Masking)]
        [PerRendererData] _CasterTex ("Caster Texture", 2D) = "black" {}
        [PerRendererData] _CasterST ("Caster Atlas Rect", Vector) = (1,1,0,0)
        _SelfMask ("Skip Own Caster", Float) = 1

        [Header(Debug)]
        [MaterialToggle] _DebugMask ("Debug  show mask", Float) = 0

        [Header(Overlap Handling)]
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Comparison", Float) = 6
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilOp ("Stencil Pass Op", Float) = 2
        _StencilRef ("Stencil Ref", Range(0, 255)) = 64
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        // Always, not LEqual: a caster material with ZWrite on would otherwise stamp depth
        // and hide every shadow that should be falling across it.
        ZTest Always
        Blend One OneMinusSrcAlpha

        Stencil
        {
            Ref [_StencilRef]
            Comp [_StencilComp]
            Pass [_StencilOp]
        }

        HLSLINCLUDE
        #include "UnityCG.cginc"

        struct appdata_t
        {
            float4 vertex   : POSITION;
            float4 color    : COLOR;
            float2 texcoord : TEXCOORD0;
        };

        struct v2f
        {
            float4 vertex   : SV_POSITION;
            fixed4 color    : COLOR;
            float2 texcoord : TEXCOORD0;
            float2 objPos   : TEXCOORD1;
        };

        sampler2D _MainTex;
        sampler2D _CasterTex;
        float4 _CasterST;
        float4x4 _CasterMatrix;
        float _SelfMask;
        float _DebugMask;
        fixed4 _Color;

        v2f vert(appdata_t IN)
        {
            v2f OUT;
            OUT.vertex = UnityObjectToClipPos(IN.vertex);
            OUT.texcoord = IN.texcoord;
            OUT.color = IN.color * _Color;
            OUT.objPos = IN.vertex.xy;
            #ifdef PIXELSNAP_ON
            OUT.vertex = UnityPixelSnap(OUT.vertex);
            #endif

            return OUT;
        }

        fixed4 frag(v2f IN) : SV_Target
        {
            fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;
            // Discard fully transparent pixels so the sprite's empty rect
            // doesn't stamp the stencil and block neighbouring shadows.
            clip(c.a - 0.004);

            // Discard wherever this shadow's own caster is drawn - see ShadowSprite.shader.
            float2 casterUV = mul(_CasterMatrix, float4(IN.objPos, 0.0, 1.0)).xy;
            float2 spriteMin = _CasterST.zw;
            float2 spriteMax = _CasterST.zw + _CasterST.xy;
            float inside = step(spriteMin.x, casterUV.x) * step(casterUV.x, spriteMax.x) *
                           step(spriteMin.y, casterUV.y) * step(casterUV.y, spriteMax.y);
            float casterAlpha = tex2D(_CasterTex, casterUV).a;
            if (_DebugMask > 0.5)
                return fixed4(inside, casterAlpha, 0.0, 1.0);

            clip(0.5 - casterAlpha * inside * _SelfMask);

            c.rgb *= c.a;
            return c;
        }
        ENDHLSL

        // 2D Renderer.
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile _ PIXELSNAP_ON
            ENDHLSL
        }

        // Forward Renderer, for 2D projects that never switched off the 3D renderer.
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile _ PIXELSNAP_ON
            ENDHLSL
        }
    }

    Fallback "DryFlyStudio/ShadowSprite"
}
