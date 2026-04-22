using UnityEngine;

[ExecuteInEditMode]
public class UnifiedNoise : BaseNoise {
    public enum NoiseKind {
        Perlin = 0,
        Worley = 1,
    }

    public enum WorleyReturnType {
        Cell = 0,
        IrregularCell = 1,
        Rock = 2,
        IrregularRock = 3,
    }

    [Space]
    [Space]
    [Space]
    public NoiseKind noiseKind = NoiseKind.Perlin;
    public ComputeShader noiseShader;

    [HideInInspector] public ComputeShader perlinShader;
    [HideInInspector] public ComputeShader worleyShader;

    [Space]
    [Space]
    [Space]
    public WorleyReturnType worleyReturnType = WorleyReturnType.Cell;

    public override void Generate() {
        ApplyShaderAndParameters();
        base.Generate();
    }

    protected override string GetCoreKernelName() {
        string preferredKernel = noiseKind.ToString();
        if (cs_core != null && cs_core.HasKernel(preferredKernel)) {
            return preferredKernel;
        }

        return "Main";
    }

    protected virtual void ApplyShaderAndParameters() {
        cs_core = ResolveCoreShader();

        switch (noiseKind) {
            case NoiseKind.Worley:
                if (cs_core != null) {
                    cs_core.SetInt("_ReturnType", (int)worleyReturnType);
                }
                break;

            default:
                break;
        }
    }

    private ComputeShader ResolveCoreShader() {
        if (noiseShader != null) {
            return noiseShader;
        }

        if (noiseKind == NoiseKind.Worley && worleyShader != null) {
            return worleyShader;
        }

        if (noiseKind == NoiseKind.Perlin && perlinShader != null) {
            return perlinShader;
        }

        return cs_core;
    }
}