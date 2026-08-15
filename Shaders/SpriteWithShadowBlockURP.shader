// Universal Render Pipeline variant of DryFlyStudio/SpriteWithShadowBlock.
//
// Unlit sprite with ZWrite enabled, applied to shadow casters so the depth buffer
// keeps later-drawn shadows from crossing casters that sit at a nearer Z.
//
// This is unlit, exactly like its built-in counterpart. Shadow2DConfig will only ever
// swap a caster onto it from Sprites/Default or URP's Sprite-Unlit-Default, both of
// which are also unlit - a caster on Sprite-Lit-Default is never touched, so enabling
// caster material replacement can't silently drop an object out of 2D lighting.
//
// See ShadowSpriteURP.shader for why this includes UnityCG.cginc rather than URP's
// ShaderLibrary.
Shader "DryFlyStudio/URP/SpriteWithShadowBlock"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
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
        ZWrite On
        ZTest LEqual
        Blend One OneMinusSrcAlpha

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
        };

        sampler2D _MainTex;
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

        fixed4 frag(v2f IN) : SV_Target
        {
            fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;
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

        // Forward Renderer.
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

    Fallback "DryFlyStudio/SpriteWithShadowBlock"
}
