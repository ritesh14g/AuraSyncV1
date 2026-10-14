// AuraSync V3 Phase 6: the section cut.
//
// While a cut is active, SectionCutter swaps every glTF material on the model for a copy using this
// shader, and swaps them back when the cut ends. It draws the model the way glTFast's
// glTF/PbrMetallicRoughness does (same property names, so Material.CopyPropertiesFromMaterial
// carries every texture and value across; same texture-transform maths, which the optimised models
// rely on through KHR_texture_transform), plus two things glTFast's shaders cannot do:
//
//   1. Clipping: everything on the removed side of _AuraSectionPlane (world space, set globally by
//      SectionCutter every frame) is discarded, including from the shadow pass.
//   2. Cut faces: where the cut opens a part up, you look at the inside of its back faces. With
//      _AuraSectionFill on, those are painted one flat colour, so a cut part reads as solid rather
//      than a hollow shell. With it off, back faces are lit normally (double-sided materials) or
//      hidden (single-sided ones, as the original culling did).
//
// Deliberately no shader keywords: glTFast's _NORMALMAP / _EMISSION / _METALLICGLOSSMAP etc. are
// replaced by always sampling textures whose defaults ("white", "bump") give the no-texture result,
// and by plain floats set from C#. An Always Included shader keeps every keyword combination, and
// keyword-heavy shaders (Standard) are what made Android builds take an hour.
Shader "AuraSync/SectionClip"
{
    Properties
    {
        [MainColor] baseColorFactor ("Base Color", Color) = (1,1,1,1)
        [MainTexture] baseColorTexture ("Base Color Tex", 2D) = "white" {}
        baseColorTexture_Rotation ("Base Color Tex Rotation", Vector) = (0,0,0,0)
        baseColorTexture_texCoord ("Base Color Tex UV", Float) = 0

        alphaCutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        roughnessFactor ("Roughness", Range(0.0, 1.0)) = 1
        [Gamma] metallicFactor ("Metallic", Range(0.0, 1.0)) = 1.0
        metallicRoughnessTexture ("Metallic-Roughness Tex", 2D) = "white" {}
        metallicRoughnessTexture_Rotation ("Metallic-Roughness Tex Rotation", Vector) = (0,0,0,0)
        metallicRoughnessTexture_texCoord ("Metallic-Roughness Tex UV", Float) = 0

        normalTexture_scale ("Normal Scale", Float) = 1.0
        [Normal] normalTexture ("Normal Tex", 2D) = "bump" {}
        normalTexture_Rotation ("Normal Tex Rotation", Vector) = (0,0,0,0)
        normalTexture_texCoord ("Normal Tex UV", Float) = 0

        occlusionTexture_strength ("Occlusion Strength", Range(0.0, 1.0)) = 1.0
        occlusionTexture ("Occlusion Tex", 2D) = "white" {}
        occlusionTexture_Rotation ("Occlusion Tex Rotation", Vector) = (0,0,0,0)
        occlusionTexture_texCoord ("Occlusion Tex UV", Float) = 0

        [HDR] emissiveFactor ("Emissive", Color) = (0,0,0,0)
        emissiveTexture ("Emission Tex", 2D) = "white" {}
        emissiveTexture_Rotation ("Emission Tex Rotation", Vector) = (0,0,0,0)
        emissiveTexture_texCoord ("Emission Tex UV", Float) = 0

        _CullMode ("Original Cull Mode", Float) = 2.0

        // Set by SectionCutter from the original material's shader and keywords.
        _AuraUnlit ("Unlit", Float) = 0
        _AuraAlphaClip ("Alpha Clip", Float) = 0
        _AuraNormalMap ("Has Normal Map", Float) = 0
        _AuraEmission ("Has Emission", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300
        Cull Off // back faces are needed for the cut faces; single-sided culling is done in surf

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert exclude_path:deferred exclude_path:prepass nolightmap nodynlightmap nodirlightmap nometa
        #pragma target 3.0

        sampler2D baseColorTexture;
        sampler2D metallicRoughnessTexture;
        sampler2D normalTexture;
        sampler2D occlusionTexture;
        sampler2D emissiveTexture;

        float4 baseColorTexture_ST;         float4 baseColorTexture_Rotation;         half baseColorTexture_texCoord;
        float4 metallicRoughnessTexture_ST; float4 metallicRoughnessTexture_Rotation; half metallicRoughnessTexture_texCoord;
        float4 normalTexture_ST;            float4 normalTexture_Rotation;            half normalTexture_texCoord;
        float4 occlusionTexture_ST;         float4 occlusionTexture_Rotation;         half occlusionTexture_texCoord;
        float4 emissiveTexture_ST;          float4 emissiveTexture_Rotation;          half emissiveTexture_texCoord;

        fixed4 baseColorFactor;
        half   alphaCutoff;
        half   roughnessFactor;
        half   metallicFactor;
        half   normalTexture_scale;
        half   occlusionTexture_strength;
        half4  emissiveFactor;
        half   _CullMode;
        half   _AuraUnlit;
        half   _AuraAlphaClip;
        half   _AuraNormalMap;
        half   _AuraEmission;

        // Globals, set by SectionCutter. Before it ever runs they are all zero, which keeps
        // everything (clip(0) does not discard) and fills nothing.
        float4 _AuraSectionPlane;    // xyz = normal towards the kept side, w = offset (world space)
        half4  _AuraSectionCapColor;
        half   _AuraSectionFill;

        struct Input
        {
            float4 rawUV;         // xy = UV0, zw = UV1, untransformed (not "uv...": surface shaders read that prefix as an automatic texture UV)
            float4 color : COLOR; // vertex colour, as glTFast multiplies it in
            float3 worldPos;
            float  facing : VFACE;
        };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.rawUV = float4(v.texcoord.xy, v.texcoord1.xy);
        }

        // glTFast's TexCoordsSingleIntern: scale and rotation as a 2x2 matrix, then offset. With no
        // transform the values are identity, so this is safe to apply to every texture.
        float2 Tc (float4 uvs, half channel, float4 st, float4 rot)
        {
            float2 uv = channel < 0.5 ? uvs.xy : uvs.zw;
            float2 sr;
            sr.x = uv.x * st.x + uv.y * rot.y;
            sr.y = uv.x * rot.x + uv.y * st.y;
            return sr + st.zw;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            clip(dot(_AuraSectionPlane.xyz, IN.worldPos) + _AuraSectionPlane.w);

            bool back = IN.facing < 0;

            if (back && _AuraSectionFill > 0.5)
            {
                // A cut face: one flat colour, no lighting, so it reads as the solid inside.
                o.Albedo = 0;
                o.Normal = float3(0, 0, 1);
                o.Emission = _AuraSectionCapColor.rgb;
                o.Metallic = 0;
                o.Smoothness = 0;
                o.Occlusion = 1;
                o.Alpha = 1;
                return;
            }

            if (back && _CullMode > 1.5) clip(-1); // the original culled back faces

            fixed4 baseCol = tex2D(baseColorTexture, Tc(IN.rawUV, baseColorTexture_texCoord, baseColorTexture_ST, baseColorTexture_Rotation))
                           * baseColorFactor * IN.color;
            if (_AuraAlphaClip > 0.5) clip(baseCol.a - alphaCutoff);

            // glTF's XYZ normal maps (glTFast does not use Unity's DXT5nm unpacking).
            float3 n = float3(0, 0, 1);
            if (_AuraNormalMap > 0.5)
            {
                n = tex2D(normalTexture, Tc(IN.rawUV, normalTexture_texCoord, normalTexture_ST, normalTexture_Rotation)).xyz * 2 - 1;
                n.xy *= normalTexture_scale;
                n = normalize(n);
            }
            if (back) n.z = -n.z; // a double-sided material seen from behind faces the viewer
            o.Normal = n;

            if (_AuraUnlit > 0.5)
            {
                o.Albedo = 0;
                o.Emission = baseCol.rgb;
                o.Metallic = 0;
                o.Smoothness = 0;
                o.Occlusion = 1;
                o.Alpha = 1;
                return;
            }

            half4 mr = tex2D(metallicRoughnessTexture, Tc(IN.rawUV, metallicRoughnessTexture_texCoord, metallicRoughnessTexture_ST, metallicRoughnessTexture_Rotation));
            half occ = tex2D(occlusionTexture, Tc(IN.rawUV, occlusionTexture_texCoord, occlusionTexture_ST, occlusionTexture_Rotation)).r;

            o.Albedo     = baseCol.rgb;
            o.Metallic   = mr.b * metallicFactor;
            o.Smoothness = 1 - mr.g * roughnessFactor;
            o.Occlusion  = lerp(1, occ, occlusionTexture_strength);
            o.Emission   = _AuraEmission > 0.5
                         ? tex2D(emissiveTexture, Tc(IN.rawUV, emissiveTexture_texCoord, emissiveTexture_ST, emissiveTexture_Rotation)).rgb * emissiveFactor.rgb
                         : half3(0, 0, 0);
            o.Alpha      = 1;
        }
        ENDCG
    }

    FallBack Off
}
