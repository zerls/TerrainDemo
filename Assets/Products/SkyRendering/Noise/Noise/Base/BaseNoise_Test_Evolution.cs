using UnityEngine;

public class BaseNoise_Test_Evolution : MonoBehaviour {
    public float evolutionSpeed;
    public BaseNoise baseNoise;

    protected virtual BaseNoise ResolveNoise() {
        return baseNoise;
    }

    private void FixedUpdate() {
        BaseNoise noise = ResolveNoise();
        if (noise == null) {
            return;
        }

        float time = Time.realtimeSinceStartup;
        if (time % 3 < 1) {
            noise.evolution.x += Time.fixedDeltaTime * evolutionSpeed * 1.0f;
            noise.evolution.y += Time.fixedDeltaTime * evolutionSpeed * 0.75f;
            noise.evolution.z += Time.fixedDeltaTime * evolutionSpeed * 0.5f;
        }
        else if (time % 3 < 2) {
            noise.evolution.x += Time.fixedDeltaTime * evolutionSpeed * 0.75f;
            noise.evolution.y += Time.fixedDeltaTime * evolutionSpeed * 1.0f;
            noise.evolution.z += Time.fixedDeltaTime * evolutionSpeed * 0.5f;
        }
        else {
            noise.evolution.x += Time.fixedDeltaTime * evolutionSpeed * 1.0f;
            noise.evolution.y += Time.fixedDeltaTime * evolutionSpeed * 0.5f;
            noise.evolution.z += Time.fixedDeltaTime * evolutionSpeed * 0.75f;
        }

        noise.Generate();
    }
}