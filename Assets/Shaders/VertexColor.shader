Shader "Custom/VertexColor" {
	Properties{
		_Color("Color", Color) = (1,1,1,1)
		_MainTex("Albedo (RGB)", 2D) = "white" {}
		_PaletteTex("VGA Palette", 2D) = "VGA" {}
		_Glossiness("Smoothness", Range(0,1)) = 0.5
		_Metallic("Metallic", Range(0,1)) = 0.0
	}
	SubShader{
		Tags { "RenderType" = "Opaque" }
		LOD 200

		CGPROGRAM
		// Physically based Standard lighting model, and enable shadows on all light types
		#pragma surface surf Standard vertex:vert fullforwardshadows finalcolor:vga

		// Use shader model 3.0 target, to get nicer looking lighting
		#pragma target 3.0

		sampler2D _MainTex;
		sampler2D _PaletteTex;

		struct Input {
			float2 uv_MainTex;
			float3 vertexVGA;
		};

		void vert(inout appdata_full v, out Input o)
		{
			UNITY_INITIALIZE_OUTPUT(Input, o);
			o.vertexVGA = v.color;	// Use blue for engine glows
		}

		half _Glossiness;
		half _Metallic;
		fixed4 _Color;

		// Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
		// See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
		// #pragma instancing_options assumeuniformscaling
		UNITY_INSTANCING_BUFFER_START(Props)
			// put more per-instance properties here
		UNITY_INSTANCING_BUFFER_END(Props)

		void surf(Input IN, inout SurfaceOutputStandard o) {
			// Albedo comes from a texture tinted by color
			fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
			o.Albedo = 1;	// Let the lighting come back with a proper colour in greyscale
			// Metallic and smoothness come from slider variables
			o.Metallic = _Metallic;
			o.Smoothness = _Glossiness;
			o.Alpha = c.a;
		}

		void vga(Input IN, SurfaceOutputStandard o, inout fixed4 color)
		{
			// Verify start index is OK
			float Brightness = clamp(1 - color.r, 0, 1);
			float Index = (IN.vertexVGA.r) + (IN.vertexVGA.g - 1) * Brightness;	// Just use red brightness. Could swizzle some average or max
			//Index = clamp(Index, IN.vertexVGA.r, IN.vertexVGA.r + IN.vertexVGA.g - 1);	// Never sample outside of this range i.e. start at 16, length 8, clamp(16, 23)
			Index += 0.5;

			float2 Pal = float2(Index/512, 0);	// XW + TF

			color = IN.vertexVGA.b < 1 ? tex2D(_PaletteTex, Pal) : (IN.vertexVGA.b == 1 ? float4(0,1,0, 1) : (IN.vertexVGA.b == 2 ? float4(1, 0, 0, 1) : float4(0, 0, 1, 1)));
		}
		ENDCG
	}
	FallBack "Diffuse"
}
