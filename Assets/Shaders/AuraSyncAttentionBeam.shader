// V3 Phase 8: the view cones and rings of attention awareness.
//
// Unlit and see-through, drawn from both sides, with no depth write. Unity's Standard shader was
// used at first, but its transparent variant is stripped from a phone build (nothing in the build
// uses it at build time), so the cones came out solid. This one is in Always Included Shaders
// (GltfShaderInclusion), has no keywords, and costs one variant.
//
// The cone mesh carries uv.y = 0 at its tip and 1 at its wide end; _Fade fades it towards the tip
// so it reads as a soft beam. Faces seen edge-on stay stronger than faces seen head-on, which
// keeps the outline crisp without a solid fill.
Shader "AuraSync/AttentionBeam"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 0.3)
        _Fade ("Fade towards the tip", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            half _Fade;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float  facing : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float3 view = normalize(WorldSpaceViewDir(v.vertex));
                o.facing = abs(dot(n, view));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = _Color;
                c.a *= lerp(1.0, saturate(i.uv.y), _Fade);
                c.a *= lerp(1.0, 0.6, saturate(i.facing));
                return c;
            }
            ENDCG
        }
    }

    Fallback Off
}
