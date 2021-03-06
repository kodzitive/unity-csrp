﻿using System;
using UnityEngine;
using UnityEngine.Rendering;

public class Shadows 
{

	private const string bufferName = "Shadows";

	private const int maxShadowedDirLightCount = 200, maxCascades = 4;

	private static string[] directionalFilterKeywords = {
		"_DIRECTIONAL_PCF3",
		"_DIRECTIONAL_PCF5",
		"_DIRECTIONAL_PCF7",
	};

	private static string[] cascadeBlendKeywords = {
		"_CASCADE_BLEND_SOFT",
		"_CASCADE_BLEND_DITHER"
	};

	private static int
		dirShadowAtlasId = Shader.PropertyToID("_DirectionalShadowAtlas"),
		dirShadowMatricesId = Shader.PropertyToID("_DirectionalShadowMatrices"),
		cascadeCountId = Shader.PropertyToID("_CascadeCount"),
		cascadeCullingSpheresId = Shader.PropertyToID("_CascadeCullingSpheres"),
		cascadeDataId = Shader.PropertyToID("_CascadeData"),
		shadowAtlastSizeId = Shader.PropertyToID("_ShadowAtlasSize"),
		shadowDistanceFadeId = Shader.PropertyToID("_ShadowDistanceFade");

	private static Vector4[]
		cascadeCullingSpheres = new Vector4[maxCascades],
		cascadeData = new Vector4[maxCascades];

	private static Matrix4x4[]
		dirShadowMatrices = new Matrix4x4[maxShadowedDirLightCount * maxCascades];

	private struct ShadowedDirectionalLight {
		public int visibleLightIndex;
		public float slopeScaleBias;
		public float nearPlaneOffset;
		public LightType lightType;
	}

	private ShadowedDirectionalLight[] shadowedDirectionalLights = new ShadowedDirectionalLight[maxShadowedDirLightCount];

	private int shadowedDirLightCount;

	private CommandBuffer buffer = new CommandBuffer {
		name = bufferName
	};

	private ScriptableRenderContext context;

	private CullingResults cullingResults;

	private ShadowSettings settings;

	public void Setup (ScriptableRenderContext context, CullingResults cullingResults,ShadowSettings settings)
	{
		this.context = context;
		this.cullingResults = cullingResults;
		this.settings = settings;
		shadowedDirLightCount = 0;
	}

	public Vector3 ReserveDirectionalShadows (Light light, int visibleLightIndex) 
	{
		if 
		(
			shadowedDirLightCount < maxShadowedDirLightCount &&
			light.shadows != LightShadows.None && light.shadowStrength > 0f &&
			cullingResults.GetShadowCasterBounds(visibleLightIndex, out Bounds b)
		) 
		{
			shadowedDirectionalLights[shadowedDirLightCount] =new ShadowedDirectionalLight {
					visibleLightIndex = visibleLightIndex,
					slopeScaleBias = light.shadowBias,
					nearPlaneOffset = light.shadowNearPlane,
					lightType = light.type
				};
			return new Vector3(light.shadowStrength,settings.directional.cascadeCount * shadowedDirLightCount++,light.shadowNormalBias);
		}
		return Vector3.zero;
	}

	public Vector3 ReservePointShadows(Light light, int visibleLightIndex)
	{
		if 
		(
			shadowedDirLightCount < maxShadowedDirLightCount &&
			light.shadows != LightShadows.None && light.shadowStrength > 0f &&
			cullingResults.GetShadowCasterBounds(visibleLightIndex, out Bounds b)
		) 
		{
			shadowedDirectionalLights[shadowedDirLightCount] =new ShadowedDirectionalLight {
				visibleLightIndex = visibleLightIndex,
				slopeScaleBias = light.shadowBias,
				nearPlaneOffset = light.shadowNearPlane,
				lightType = light.type
			};
			return new Vector3(light.shadowStrength,settings.directional.cascadeCount * shadowedDirLightCount++,light.shadowNormalBias);
		}
		return Vector3.zero;
	}

	public void Render () 
	{
		if (shadowedDirLightCount > 0) 
		{
			RenderDirectionalShadows();
		}
	}

	private void RenderDirectionalShadows () 
	{
		int atlasSize = (int)settings.directional.atlasSize;
		buffer.GetTemporaryRT(dirShadowAtlasId, atlasSize, atlasSize,32, FilterMode.Bilinear, RenderTextureFormat.Shadowmap);
		buffer.SetRenderTarget(dirShadowAtlasId,RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
		buffer.ClearRenderTarget(true, false, Color.clear);
		buffer.BeginSample(bufferName);
		ExecuteBuffer();

		int tiles = shadowedDirLightCount * settings.directional.cascadeCount;
		int split = tiles <= 1 ? 1 : tiles <= 4 ? 2 : 4;
		int tileSize = atlasSize / split;

		for (int i = 0; i < shadowedDirLightCount; i++) 
		{
			if(shadowedDirectionalLights[i].lightType == LightType.Directional)
				RenderDirectionalShadows(i, split, tileSize);
			else if (shadowedDirectionalLights[i].lightType == LightType.Point)
				RenderPointShadows(i, split, tileSize);
		}

		buffer.SetGlobalInt(cascadeCountId, settings.directional.cascadeCount);
		buffer.SetGlobalVectorArray(cascadeCullingSpheresId, cascadeCullingSpheres);
		buffer.SetGlobalVectorArray(cascadeDataId, cascadeData);
		buffer.SetGlobalMatrixArray(dirShadowMatricesId, dirShadowMatrices);
		
		float f = 1f - settings.directional.cascadeFade;
		buffer.SetGlobalVector(shadowDistanceFadeId, new Vector4(1f / settings.maxDistance, 1f / settings.distanceFade,1f / (1f - f * f)));
		SetKeywords(directionalFilterKeywords, (int)settings.directional.filter - 1);
		SetKeywords(cascadeBlendKeywords, (int)settings.directional.cascadeBlend - 1);
		buffer.SetGlobalVector(shadowAtlastSizeId, new Vector4(atlasSize, 1f / atlasSize));
		buffer.EndSample(bufferName);
		ExecuteBuffer();
	}

	private void SetKeywords (string[] keywords, int enabledIndex) 
	{
		for (int i = 0; i < keywords.Length; i++) 
		{
			if (i == enabledIndex) 
			{
				buffer.EnableShaderKeyword(keywords[i]);
			}
			else 
			{
				buffer.DisableShaderKeyword(keywords[i]);
			}
		}
	}

	private void RenderDirectionalShadows (int index, int split, int tileSize) 
	{
		ShadowedDirectionalLight light = shadowedDirectionalLights[index];
		var shadowSettings =
			new ShadowDrawingSettings(cullingResults, light.visibleLightIndex);
		
		int cascadeCount = settings.directional.cascadeCount;
		int tileOffset = index * cascadeCount;
		Vector3 ratios = settings.directional.CascadeRatios;
		float cullingFactor =
			Mathf.Max(0f, 0.8f - settings.directional.cascadeFade);

		for (int i = 0; i < cascadeCount; i++) {
			cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
				light.visibleLightIndex, i, cascadeCount, ratios, tileSize,
				light.nearPlaneOffset, out Matrix4x4 viewMatrix,
				out Matrix4x4 projectionMatrix, out ShadowSplitData splitData
			);
			splitData.shadowCascadeBlendCullingFactor = cullingFactor;
			shadowSettings.splitData = splitData;
			
			if (index == 0) {
				SetCascadeData(i, splitData.cullingSphere, tileSize);
			}
			int tileIndex = tileOffset + i;
			dirShadowMatrices[tileIndex] = ConvertToAtlasMatrix(
				projectionMatrix * viewMatrix,
				SetTileViewport(tileIndex, split, tileSize), split
			);
			buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
			buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
			ExecuteBuffer();
			context.DrawShadows(ref shadowSettings);
			buffer.SetGlobalDepthBias(0f, 0f);
		}
	}
	
	private void RenderPointShadows (int index, int split, int tileSize) 
	{
		ShadowedDirectionalLight light = shadowedDirectionalLights[index];
		var shadowSettings = new ShadowDrawingSettings(cullingResults, light.visibleLightIndex);
		
		int cascadeCount = settings.directional.cascadeCount;
		int tileOffset = index * cascadeCount;
		Vector3 ratios = settings.directional.CascadeRatios;
		float cullingFactor =
			Mathf.Max(0f, 0.8f - settings.directional.cascadeFade);

		for (int j = 5; j >= 0; j--)
		{
			for (int i = 0; i < cascadeCount; i++)
			{
				cullingResults.ComputePointShadowMatricesAndCullingPrimitives(light.visibleLightIndex, (CubemapFace) j,
					light.nearPlaneOffset,
					out Matrix4x4 viewMatrix, out Matrix4x4 projectionMatrix, out ShadowSplitData splitData);
				
				splitData.shadowCascadeBlendCullingFactor = cullingFactor;
				shadowSettings.splitData = splitData;

				if (index == 0)
				{
					SetCascadeData(i, splitData.cullingSphere, tileSize);
				}

				int tileIndex = tileOffset + i;
				dirShadowMatrices[tileIndex] = ConvertToAtlasMatrix(
					projectionMatrix * viewMatrix,
					SetTileViewport(tileIndex, split, tileSize), split
				);

				buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
				buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
				ExecuteBuffer();
				context.DrawShadows(ref shadowSettings);
				buffer.SetGlobalDepthBias(0f, 0f);
			}
		}
	}

	private void SetCascadeData (int index, Vector4 cullingSphere, float tileSize) 
	{
		float texelSize = 2f * cullingSphere.w / tileSize;
		float filterSize = texelSize * ((float)settings.directional.filter + 1f);
		cullingSphere.w -= filterSize;
		cullingSphere.w *= cullingSphere.w;
		cascadeCullingSpheres[index] = cullingSphere;
		cascadeData[index] = new Vector4(
			1f / cullingSphere.w,
			filterSize * 1.4142136f
		);
	}

	private Matrix4x4 ConvertToAtlasMatrix (Matrix4x4 m, Vector2 offset, int split) 
	{
		if (SystemInfo.usesReversedZBuffer) 
		{
			m.m20 = -m.m20;
			m.m21 = -m.m21;
			m.m22 = -m.m22;
			m.m23 = -m.m23;
		}
		float scale = 1f / split;
		m.m00 = (0.5f * (m.m00 + m.m30) + offset.x * m.m30) * scale;
		m.m01 = (0.5f * (m.m01 + m.m31) + offset.x * m.m31) * scale;
		m.m02 = (0.5f * (m.m02 + m.m32) + offset.x * m.m32) * scale;
		m.m03 = (0.5f * (m.m03 + m.m33) + offset.x * m.m33) * scale;
		m.m10 = (0.5f * (m.m10 + m.m30) + offset.y * m.m30) * scale;
		m.m11 = (0.5f * (m.m11 + m.m31) + offset.y * m.m31) * scale;
		m.m12 = (0.5f * (m.m12 + m.m32) + offset.y * m.m32) * scale;
		m.m13 = (0.5f * (m.m13 + m.m33) + offset.y * m.m33) * scale;
		m.m20 = 0.5f * (m.m20 + m.m30);
		m.m21 = 0.5f * (m.m21 + m.m31);
		m.m22 = 0.5f * (m.m22 + m.m32);
		m.m23 = 0.5f * (m.m23 + m.m33);
		return m;
	}

	private Vector2 SetTileViewport (int index, int split, float tileSize) 
	{
		Vector2 offset = new Vector2(index % split, index / split);
		buffer.SetViewport(new Rect(offset.x * tileSize, offset.y * tileSize, tileSize, tileSize));
		return offset;
	}

	private void ExecuteBuffer () 
	{
		context.ExecuteCommandBuffer(buffer);
		buffer.Clear();
	}
	
	public void Cleanup () 
	{
		if (shadowedDirLightCount > 0) {
			buffer.ReleaseTemporaryRT(dirShadowAtlasId);
			ExecuteBuffer();
		}
	}
}