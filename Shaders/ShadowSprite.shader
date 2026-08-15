// Sprite shader for generated shadows. Renders just before the regular
// transparent queue and uses the stencil buffer so overlapping shadows
// merge into one uniform patch instead of double-darkening.
// Set Stencil Comparison to "Always" on the material to disable that.
Shader "SleepyHeadStudios/ShadowSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0

        [Header(Overlap Handling)]
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Comparison", Float) = 6
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilOp ("Stencil Pass Op", Float) = 2
        _StencilRef ("Stencil Ref", Range(0, 255)) = 64
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent-1"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest LEqual
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
            };

            fixed4 _Color;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                #ifdef PIXELSNAP_ON
                OUT.vertex = UnityPixelSnap(OUT.vertex);
                #endif

                return OUT;
            }

            sampler2D _MainTex;

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;
                // Discard fully transparent pixels so the sprite's empty rect
                // doesn't stamp the stencil and block neighbouring shadows.
                clip(c.a - 0.004);
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
