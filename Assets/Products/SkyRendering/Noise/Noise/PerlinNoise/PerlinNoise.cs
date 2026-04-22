using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[ExecuteInEditMode]
public partial class PerlinNoise : UnifiedNoise {
	private void OnValidate() {
		noiseKind = NoiseKind.Perlin;
		if (noiseShader == null) {
			noiseShader = perlinShader != null ? perlinShader : cs_core;
		}
	}

	public override void Generate() {
		noiseKind = NoiseKind.Perlin;
		if (noiseShader == null) {
			noiseShader = perlinShader != null ? perlinShader : cs_core;
		}

		base.Generate();
	}
}