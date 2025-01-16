Shader "Unlit/Particles"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _FoamCol ("Fluid Color", Color) = (1, 1, 1, 1)
        _NormalCol ("Normal Color", Color) = (0, 0, 1, 1)
        _PressureCol ("Pressure Color", Color) = (1, 0, 0, 1)
        _DensityThreshold ("Density Threshold", float) = .5
        _PressureThreshold ("Pressure Threshold", float) = 1.25
        _Refraction ("Refraction", float) = 0.01
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            // make fog work

            #include "UnityCG.cginc"

            struct MeshData
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Interpolators
            {
                float2 uv_main : TEXCOORD0;
                float2 uv_fluid : TEXCOORD1;
                float4 vertex : SV_POSITION;
                float4 worldPos : TEXCOORD2;
            };

            struct Particle
            {
                float2 position;
                float2 velocity;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            sampler2D _FluidSampleTex;
            float4 _FluidSampleTex_ST;

            float4 _FoamCol;
            float4 _NormalCol;
            float4 _PressureCol;
            float _DensityThreshold;
            float _PressureThreshold;
            float _Refraction;

            // uniform RWStructuredBuffer<Particle> particles : register(u2);
            int num_particles;

            Interpolators vert (MeshData v)
            {
                Interpolators o;
                o.vertex = UnityObjectToClipPos(v.vertex);

                float4 worldPos = mul(unity_CameraInvProjection, o.vertex);
                worldPos = mul(unity_MatrixInvV, worldPos);

                if (_ProjectionParams.x < 0) { worldPos.y *= -1; }
                
                o.worldPos = worldPos;
                o.uv_main = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv_fluid = TRANSFORM_TEX(v.uv, _FluidSampleTex);
                return o;
            }

            float distancesq(float2 p1, float2 p2)
            {
                float2 offset = p1 - p2;
                return offset.x * offset.x + offset.y * offset.y;
            }

            fixed4 frag (Interpolators i) : SV_Target
            {
                // for(int p = 0; p < num_particles; p++)
                // {
                //     if(distancesq(i.worldPos, particles[p].position) < .005f)
                //     {
                //         return float4(particles[p].velocity, 1, 1);
                //     }
                // }
                
                float4 fluid_data = tex2D(_FluidSampleTex, i.uv_fluid);
                float4 fluid_col = float4(0, 0, 0, 1);
                float2 refraction_sample = 0;
                if(fluid_data.y > _DensityThreshold)
                {
                    fluid_col = lerp(_NormalCol, _PressureCol, clamp(fluid_data.y - _DensityThreshold, 0, 1));
                    refraction_sample = -_Refraction * fluid_data.zw;

                    fluid_col += lerp(float4(0, 0, 0, 0), _FoamCol, clamp(fluid_data.x / 500, 0, 1));
                }
                

                return tex2D(_MainTex, i.uv_main + refraction_sample) + fluid_col * fluid_col.a;
            }
            ENDCG
        }
    }
}
