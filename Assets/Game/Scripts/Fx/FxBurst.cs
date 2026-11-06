using UnityEngine;

namespace CargoClash
{
    /// <summary>One shared particle system that throws out a burst of debris at any position.</summary>
    public static class FxBurst
    {
        private static ParticleSystem system;

        public static void Play(Vector3 position, Color color, int count)
        {
            if (Application.isBatchMode) return;
            if (system == null) Create();

            ParticleSystem.EmitParams parameters = new ParticleSystem.EmitParams
            {
                position = position,
                startColor = color,
                applyShapeToPosition = true,
            };
            system.Emit(parameters, count);
        }

        private static void Create()
        {
            GameObject host = new GameObject("FxBurst");
            Object.DontDestroyOnLoad(host);
            system = host.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.34f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 1.6f;
            main.maxParticles = 400;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;

            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GameConfig.Get().particleMaterial;
        }
    }
}
