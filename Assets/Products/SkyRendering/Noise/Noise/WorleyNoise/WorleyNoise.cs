using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public partial class WorleyNoise : UnifiedNoise {
    public enum ReturnType {
        Cell = 0,
        IrregularCell = 1,
        Rock = 2,
        IrregularRock = 3,
    }

    [Space]
    [Space]
    [Space]
    public ReturnType returnType;

    private void OnValidate() {
        noiseKind = NoiseKind.Worley;
        if (noiseShader == null) {
            noiseShader = worleyShader != null ? worleyShader : cs_core;
        }

        worleyReturnType = (WorleyReturnType)returnType;
    }

    public override void Generate() {
        noiseKind = NoiseKind.Worley;
        if (noiseShader == null) {
            noiseShader = worleyShader != null ? worleyShader : cs_core;
        }

        worleyReturnType = (WorleyReturnType)returnType;

        base.Generate();
    }
}