﻿using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Cel-Shaded Render Pipeline")]
public class CelShadedRenderPipelineAsset : RenderPipelineAsset {
	
	public bool useDynamicBatching = true, useGPUInstancing = true, useSRPBatcher = true;
	
	public float defaultShadowBrightness = 0.1f, brightnessMultiplier = 1.0f, shadowTreshold = 0.15f;

	public ShadowSettings shadows = default;

	protected override RenderPipeline CreatePipeline () 
	{
		return new CelShadedRenderPipeline(useDynamicBatching, useGPUInstancing, useSRPBatcher, shadows, defaultShadowBrightness, brightnessMultiplier, shadowTreshold);
	}
}