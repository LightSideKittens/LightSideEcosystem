// Before editing, read Packages/media.lightside.core/Docs~/SHADER_DIAGNOSTICS.md - the compiler traps this codebase has already paid for.
// Unified text rendering — one material serves SDF, MSDF and color glyphs; the per-glyph mode
// rides the vertex stream (UV1.w, see LightSideGlyphMode). Text quads = coverage(mode) x paint(kind):
//   coverage mode (TEXCOORD2.x): 0 fill, 1 stroke, 2 shadow/glow, 3 inner-shadow.
//   paint kind  (TEXCOORD3.w):  0 solid (vertex colour), 1/2/3 gradient (ramp), 4 texture, 5 tiled texture.
// Plain glyphs carry no UV2/UV3 → 0 → fill + solid. One atlas sample (two for inner-shadow),
// one draw call per material. Color quads bypass coverage/paint: one bitmap sample x alpha tint,
// then the colour-matrix row + 1 their TEXCOORD3.z carries (0 = none), applied premultiplied.

Shader "Diag_Full_noimage" {

Properties {
	[HideInInspector] _LightSidePaintTexture ("Paint Texture", 2D) = "white" {}
	[HideInInspector] _SrcBlend ("Source RGB Blend", Float) = 1
	[HideInInspector] _DstBlend ("Destination RGB Blend", Float) = 10

	_ClipRect			("Clip Rect", vector) = (-32767, -32767, 32767, 32767)

	_StencilComp		("Stencil Comparison", Float) = 8
	_Stencil			("Stencil ID", Float) = 0
	_StencilOp			("Stencil Operation", Float) = 0
	_StencilWriteMask	("Stencil Write Mask", Float) = 255
	_StencilReadMask	("Stencil Read Mask", Float) = 255

	_CullMode			("Cull Mode", Float) = 0
	_ColorMask			("Color Mask", Float) = 15
}

CGINCLUDE
    #define LIGHTSIDE_SHADER_VARIANTS
    #define LIGHTSIDE_GLYPHS
    #define LIGHTSIDE_ROUNDED_RECTANGLES
    #define LIGHTSIDE_NO_IMAGE_SURFACE
ENDCG

SubShader {
	Tags
	{
		"Queue"="Transparent"
		"IgnoreProjector"="True"
		"RenderType"="Transparent"
		"CanvasGroupAlpha"="Premultiplied"
	}

	Stencil
	{
		Ref [_Stencil]
		Comp [_StencilComp]
		Pass [_StencilOp]
		ReadMask [_StencilReadMask]
		WriteMask [_StencilWriteMask]
	}

	Cull [_CullMode]
	ZWrite Off
	Lighting Off
	Fog { Mode Off }
	ZTest [unity_GUIZTestMode]
	Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
	ColorMask [_ColorMask]

	Pass {
		Name "SDF_DECORATION"
		CGPROGRAM
		#pragma vertex VertShader
		#pragma fragment PixShader
		#pragma target 3.0
		#pragma require 2darray integers

		// Clipping and the paint resolve are uniform branches, not variants: each changes a few
		// instructions of a program that carries the whole surface, and a variant would copy all of it.
		// SDF vs MSDF vs color is NOT a keyword either: the per-glyph mode rides the vertex stream (UV1.w).
		#pragma dynamic_branch _ UNITY_UI_CLIP_RECT
		#pragma dynamic_branch _ UNITY_UI_ALPHACLIP
		#pragma dynamic_branch _ LIGHTSIDE_PAINT_TEXTURE
		#define LIGHTSIDE_PAINT_TEXTURE_DYNAMIC

		#include "Assets/UniText_MySpace/Slideshow/DiagShaders/Diag_Full_noimage/LightSideGlyphField.cginc"

		sampler2D _LightSideGradientRamp;
		float _LightSideGradientRampRows;
		sampler2D _LightSidePaintTexture;
		sampler2D _LightSideColorMatrixAtlas;
		float _LightSideColorMatrixRows;
		// Explicit LOD 0: the ramp has no mips — see LightSidePaint.hlsl for why implicit
		// derivatives would flatten the gradient branch.
		#define LIGHTSIDE_SAMPLE_RAMP(u, v) tex2Dlod(_LightSideGradientRamp, float4(u, v, 0, 0))
		#define LIGHTSIDE_SAMPLE_PAINT(uv)  tex2D(_LightSidePaintTexture, uv)
        #define LIGHTSIDE_SAMPLE_IMAGE_GRAD(uv, dx, dy) tex2Dgrad(_LightSidePaintTexture, uv, dx, dy)
		#define LIGHTSIDE_SAMPLE_MATRIX(u, v) tex2Dlod(_LightSideColorMatrixAtlas, float4(u, v, 0, 0))

#include "Assets/UniText_MySpace/Slideshow/DiagShaders/Diag_Full_noimage/LightSideSurfacePaint.hlsl"


		// Surface kind 4 samples this global array atlas; it is never a material property, so a
		// vector-animation quad and a glyph run share one material and one batch.
		float4 _LightSidePaintTexture_TexelSize;

		struct pixel_t
		{
			float4 vertex   : SV_POSITION;
			float4 atlasUV  : TEXCOORD0; // xy = atlas UV, z = page layer, w = glyph mode (quad-constant)
			float2 glyphUV  : TEXCOORD1; // for fwidth AA
			half4  cov      : TEXCOORD2; // coverageMode, p0, p1, softness (em-scale — half is plenty)
			float4 paint    : TEXCOORD3; // paintU, paintV, rampRow, paintKind + 8 * spread (tiled coords exceed half range)
			half4  mask     : TEXCOORD4;
			fixed4 color    : TEXCOORD5; // straight vertex colour (premultiplied in fragment)
			half4  extra    : TEXCOORD6; // glyphs: faceDilate, glyphH, sdfScale.xy — shapes: .x is the paint scale/fit
			float4 shapeGeom   : TEXCOORD7; // halfSize.xy, aux, trailing mode param — glyphs: .x is the UV0.z lane, .z the deform lane (UV1.x)
			float4 shapeParams : TEXCOORD8; // per-shape params (radii / ratios / counts) — glyphs: the union gate
			UNITY_VERTEX_INPUT_INSTANCE_ID
			UNITY_VERTEX_OUTPUT_STEREO
		};

		pixel_t VertShader(LightSideSurfaceVertex input)
		{
			pixel_t output;

			UNITY_INITIALIZE_OUTPUT(pixel_t, output);
			UNITY_SETUP_INSTANCE_ID(input);
			UNITY_TRANSFER_INSTANCE_ID(input, output);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

			float4 vert = input.vertex;
			float4 vPosition = UnityObjectToClipPos(vert);

			float2 pixelSize = vPosition.w;
			pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

			float glyphMode = LightSideSurfaceKind(input.texcoord1.w);
			float glyphH = input.texcoord0.w;
			float faceDilate = input.texcoord1.y;
			float2 sdfScale = float2(0.0, LightSideSurfaceFraction(input.texcoord1.w));
			float pageLayer = input.texcoord0.z;
			float2 atlasXY = input.texcoord0.xy;
			float4 shapeParams = LightSideShapeParams(input.tangent.w);
#if defined(LIGHTSIDE_GLYPHS)
			if (glyphMode < 1.5)
			{
				float4 t = LightSideLoadGlyphTransform(input.texcoord0.z);
				sdfScale = t.xx;
				atlasXY = input.texcoord0.xy * t.x + t.yz;
				pageLayer = t.w;
#if !defined(LIGHTSIDE_NO_GLYPH_UNIONS)
				shapeParams = LightSideGlyphUnionGate(input.texcoord0.z);
#endif
			}
#endif

			output.vertex = vPosition;
			output.atlasUV = float4(atlasXY, pageLayer, glyphMode);
			output.glyphUV = input.texcoord0.xy;
			output.cov = input.texcoord2;
			output.paint = input.texcoord3;
			output.mask = ComputeMask(vert, pixelSize);
			output.color = GammaToLinearIfNeeded(input.color, _UIVertexColorAlwaysGammaSpace != 0);
			output.extra = float4(faceDilate, glyphH, sdfScale.x, sdfScale.y);
			output.shapeGeom = float4(input.texcoord0.zw, input.texcoord1.x, input.texcoord1.z);
			output.shapeParams = shapeParams;

			return output;
		}

		#define LIGHTSIDE_SURFACE_VARYINGS pixel_t
		#include "Assets/UniText_MySpace/Slideshow/DiagShaders/Diag_Full_noimage/LightSideSurfaceResolve.hlsl"

		half4 ApplyClipping(half4 color, half4 mask)
		{
			UNITY_BRANCH
			if (UNITY_UI_CLIP_RECT)
			{
				half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(mask.xy)) * mask.zw);
				color *= m.x * m.y;
			}
			UNITY_BRANCH
			if (UNITY_UI_ALPHACLIP)
				clip(color.a - 0.001);
			return color;
		}

		fixed4 PixShader(pixel_t input) : SV_Target
		{
			UNITY_SETUP_INSTANCE_ID(input);
			return ApplyClipping(LightSideResolveSurface(input), input.mask);
		}
		ENDCG
	}
}
}
