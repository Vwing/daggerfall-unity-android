Shader "Daggerfall/GhostShader" {
    Properties {
        _Color("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _BumpMap ("Bumpmap", 2D) = "bump" {}
        _EmissionMap("Emission Map", 2D) = "white" {}
        _EmissionColor("Emission Color", Color) = (1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Glossiness ("Smoothness", Range(0.0, 1.0)) = 0.0
        _Metallic ("Metallic", Range(0.0, 1.0)) = 0.0
    }

    // 1) SM3.0 / GLES3+ variant
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        CGPROGRAM
        #pragma surface surf Standard alpha:blend
        #pragma target 3.0

        half4 _Color;
        sampler2D _MainTex, _BumpMap, _EmissionMap;
        half4 _EmissionColor;
        half _Cutoff, _Glossiness, _Metallic;

        struct Input {
            float2 uv_MainTex;
            float2 uv_BumpMap;
            float2 uv_EmissionMap;
        };

        void surf (Input IN, inout SurfaceOutputStandard o) {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            clip(c.a - _Cutoff);

            half3 emission = tex2D(_EmissionMap, IN.uv_EmissionMap).rgb * _EmissionColor;
            o.Albedo   = c.rgb - emission;
            o.Normal   = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
            o.Emission = emission;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha    = c.a;
        }
        ENDCG
    }

    // 2) SM2.0 / GLES2 fallback
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        CGPROGRAM
        #pragma surface surf Lambert alpha:blend   // use a simpler lighting model
        #pragma target 2.0

        half4 _Color;
        sampler2D _MainTex, _BumpMap, _EmissionMap;
        half4 _EmissionColor;
        half _Cutoff;

        struct Input {
            float2 uv_MainTex;
            float2 uv_BumpMap;
            float2 uv_EmissionMap;
        };

        void surf (Input IN, inout SurfaceOutput o) {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            clip(c.a - _Cutoff);

            o.Albedo = c.rgb;
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
            o.Emission = tex2D(_EmissionMap, IN.uv_EmissionMap).rgb * _EmissionColor.rgb;
            o.Alpha = c.a;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
