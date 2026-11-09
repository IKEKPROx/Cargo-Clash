using UnityEngine;

namespace CargoClash
{
    /// <summary>Shared particle systems for debris, dust and comic flashes at any position.</summary>
    public static class FxBurst
    {
        private static ParticleSystem chips;
        private static ParticleSystem puffs;
        private static ParticleSystem flashes;

        public static void Play(Vector3 position, Color color, int count)
        {
            if (Application.isBatchMode) return;
            if (TooCloseToSee(position)) return;
            if (chips == null) CreateChips();
            Camera view = Camera.main;
            ParticleSystem.EmitParams parameters = new ParticleSystem.EmitParams
            {
                position = position,
                startColor = color,
                applyShapeToPosition = true,
            };
            Vector3 toViewer = view != null ? (view.transform.position - position).normalized : Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                // Up and out, but never straight at the viewer: in first person a chip that close would fill the screen.
                Vector3 direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y);
                float facing = Vector3.Dot(direction, toViewer);
                if (facing > 0.1f) direction -= toViewer * (facing * 2f);
                parameters.velocity = direction * Random.Range(2.5f, 7f);
                chips.Emit(parameters, 1);
            }
        }

        /// <summary>Round puffs of dust that drift apart and fade away.</summary>
        public static void PlayPuff(Vector3 position, Color color, int count)
        {
            if (Application.isBatchMode) return;
            if (puffs == null) CreatePuffs();
            ParticleSystem.EmitParams parameters = new ParticleSystem.EmitParams
            {
                position = position,
                startColor = color,
                applyShapeToPosition = true,
            };
            puffs.Emit(parameters, count);
        }

        /// <summary>A comic-book impact star that's there for an instant. `size` is its width in metres.</summary>
        public static void PlayFlash(Vector3 position, Color color, float size)
        {
            if (Application.isBatchMode) return;
            if (TooCloseToSee(position)) return;
            if (flashes == null) CreateFlashes();
            ParticleSystem.EmitParams parameters = new ParticleSystem.EmitParams
            {
                position = position,
                startColor = color,
                startSize = size,
                rotation = Random.Range(0f, 360f),
                startLifetime = 0.11f,
            };
            flashes.Emit(parameters, 1);
        }

        /// <summary>When it's you getting hit, the effect is inside your own head. All you'd see is chips filling the screen.</summary>
        private static bool TooCloseToSee(Vector3 position)
        {
            Camera view = Camera.main;
            return view != null && (view.transform.position - position).sqrMagnitude < 0.81f;
        }

        private static ParticleSystem NewSystem(string name, int maxParticles, float gravity)
        {
            GameObject host = new GameObject(name);
            Object.DontDestroyOnLoad(host);
            ParticleSystem system = host.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.maxParticles = maxParticles;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            return system;
        }

        private static void CreateChips()
        {
            chips = NewSystem("FxChips", 400, 1.6f);
            ParticleSystem.MainModule main = chips.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.11f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            ParticleSystem.ShapeModule shape = chips.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;

            // Small tumbling chunks rather than flat squares.
            ParticleSystem.RotationOverLifetimeModule spin = chips.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-8f, 8f);
            spin.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
            spin.z = new ParticleSystem.MinMaxCurve(-8f, 8f);

            ParticleSystemRenderer renderer = chips.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            renderer.sharedMaterial = GameConfig.Get().particleMaterial;
        }

        private static void CreatePuffs()
        {
            puffs = NewSystem("FxPuffs", 120, -0.08f);
            ParticleSystem.MainModule main = puffs.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);

            ParticleSystem.ShapeModule shape = puffs.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;

            ParticleSystem.SizeOverLifetimeModule size = puffs.sizeOverLifetime;
            size.enabled = true;
            AnimationCurve curve = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f));
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);

            ParticleSystemRenderer renderer = puffs.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            renderer.sharedMaterial = GameConfig.Get().puffMaterial;
        }

        private static void CreateFlashes()
        {
            flashes = NewSystem("FxFlashes", 24, 0f);
            ParticleSystem.MainModule main = flashes.main;
            main.startSpeed = 0f;

            ParticleSystem.ShapeModule shape = flashes.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule size = flashes.sizeOverLifetime;
            size.enabled = true;
            AnimationCurve curve = new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.35f, 1.1f), new Keyframe(1f, 0.85f));
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);

            ParticleSystemRenderer renderer = flashes.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = GameConfig.Get().flashMaterial;
            renderer.sortingFudge = -40f;
        }
    }
}
