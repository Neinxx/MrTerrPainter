Shader "Unlit/PreviewPainting_Ghost"
{
    Properties
    {
        _BaseColor ("幽灵基础色", Color) = (0.2, 0.8, 1.0, 0.3) // 偏蓝的半透明色（幽灵感）
        _GlowStrength ("自发光强度", Float) = 2.0 // 内部发光亮度
        _EdgeWidth ("边缘宽度", Float) = 0.15 // 边缘高亮范围
        _EdgeGlow ("边缘亮度", Float) = 3.0 // 边缘发光强度
        _FlickerSpeed ("闪烁速度", Float) = 1.2 // 呼吸闪烁频率
        _FlickerRange ("闪烁幅度", Float) = 0.3 // 闪烁明暗变化范围
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalRenderPipeline"
            "Queue" = "Transparent" // 透明队列确保正确排序
            "IgnoreProjector" = "True"
        }

        Pass
        {
            // 半透明混合模式（核心）
            Blend SrcAlpha OneMinusSrcAlpha
            // 关闭深度写入，避免遮挡其他透明物体
            ZWrite Off
            // 关闭背面剔除，幽灵正反面都能看到
            Cull Off
            // 深度测试设为 LEqual，确保正常显示
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 材质属性缓冲区
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _GlowStrength;
            float _EdgeWidth;
            float _EdgeGlow;
            float _FlickerSpeed;
            float _FlickerRange;
            CBUFFER_END

            // 输入结构体（添加法线用于边缘检测）
            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL; // 物体空间法线（关键：用于边缘计算）
            };

            // 输出结构体（传递法线和视角方向）
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1; // 世界空间法线
                float3 viewDirWS : TEXCOORD2; // 世界空间视角方向
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN); // 必须
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT); // 必须
                // 转换位置到裁剪空间
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                // 转换法线到世界空间（带非统一缩放支持）
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                // 计算世界空间视角方向（相机位置 - 顶点世界位置）
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.viewDirWS = normalize(_WorldSpaceCameraPos.xyz - positionWS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // 1. 归一化法线和视角方向（避免插值后精度丢失）
                float3 normalWS = normalize(IN.normalWS);
                float3 viewDirWS = normalize(IN.viewDirWS);

                // 2. 计算边缘因子：法线与视角方向的点积（值越小越靠近边缘）
                float edgeFactor = dot(normalWS, viewDirWS);
                // 边缘范围映射（0~1）：_EdgeWidth 越小，边缘越窄
                edgeFactor = smoothstep(1 - _EdgeWidth, 1.0, edgeFactor);
                // 反转因子：让边缘处值为1，中心为0
                edgeFactor = 1 - edgeFactor;

                // 3. 计算闪烁因子（基于时间的正弦波动，0.5~1.5 范围）
                float flicker = sin(_Time.y * _FlickerSpeed) * _FlickerRange + 1.0 - _FlickerRange;
                // 限制闪烁范围（避免过暗或过亮）
                flicker = clamp(flicker, 0.7, 1.3);

                // 4. 组合基础色 + 自发光 + 边缘高亮
                half3 baseColor = _BaseColor.rgb * _GlowStrength * flicker;
                half3 edgeColor = baseColor * _EdgeGlow * edgeFactor;
                half3 finalColor = baseColor + edgeColor;

                // 5. 保留 Alpha 透明度，叠加闪烁效果
                half finalAlpha = _BaseColor.a * flicker;

                return half4(finalColor, finalAlpha);
            }

            ENDHLSL
        }
    }

}