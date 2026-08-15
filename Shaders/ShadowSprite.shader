// Sprite shader for generated shadows.
//
// Shadows render in the normal transparent queue, so sorting layer, order and Y-sorting
// decide where they land exactly like any other sprite - a shadow falls across the props
// behind it and is occluded by the ones in front. It used to sit at Transparent-1, and
// because render queue is a coarser sort key than sorting layer, that forced every shadow
// behind every sprite no matter what the sorting settings said.
//
// That leaves one problem. With Y-sorting a shadow sits below its caster, so it sorts in
// front of it and paints over the thing casting it. Ordering can't fix that: to reach a
// prop standing in front of the caster, the shadow has to be drawn in front of the caster
// too. So the caster is masked out per pixel instead - the shader samples the caster's
// own alpha through _CasterMatrix and discards wherever the caster is opaque.
//
// The stencil block additionally merges overlapping shadows into one uniform patch.
// Set Stencil Comparison to "Always" on the material to disable that.
Shader "DryFlyStudio/ShadowSprite"
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
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        // Always, not LEqual: a caster material with ZWrite on would otherwise stamp depth
        // and hide every shadow that should be falling across it. Draw order here is
        // decided by sorting, not by depth.
        ZTest Always
        Blend One OneMinusSrcAlpha

        Stencil
        {
            Ref [_StencilRef]
            Comp [_StencilComp]
            Pass [_StencilOp]
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile _ PIXELSNAP_ON
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

            fixed4 _Color;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                // Kept in the shadow's own object space; _CasterMatrix maps straight from
                // here into the caster's sprite, so no world-space round trip is needed.
                OUT.objPos = IN.vertex.xy;
                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif

                return OUT;
            }

            sampler2D _MainTex;
            sampler2D _CasterTex;
            float4 _CasterST;
            float4x4 _CasterMatrix;
            float _SelfMask;
            float _DebugMask;

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;
                // Discard fully transparent pixels so the sprite's empty rect
                // doesn't stamp the stencil and block neighbouring shadows.
                clip(c.a - 0.004);

                // Discard wherever this shadow's own caster is drawn. _CasterMatrix maps
                // straight to page UV, so the bounds test is against the sprite's own
                // rect on that page. Branchless: outside it the sample is meaningless, so
                // it is zeroed rather than skipped.
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
            ENDCG
        }
    }
}
