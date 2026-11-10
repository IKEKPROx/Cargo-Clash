using UnityEngine;

namespace CargoClash
{
    /// <summary>Shared particle systems for debris, dust and comic flashes at any position.</summary>
    public static class FxBurst
    {
        private static ParticleSystem chips;
        private static ParticleSystem puffs;
        private static ParticleSystem flashes;
        private static ParticleSystem blasts;
        private static ParticleSystem wisps;

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

        /// <summary>A burst of air from a vacuum: fast at first, then spreading out and fading.</summary>
        public static void PlayBlast(Vector3 origin, Vector3 direction)
        {
            if (Application.isBatchMode) return;
            if (blasts == null) CreateBlasts();
            Vector3 forward = direction.normalized;
            for (int i = 0; i < 18; i++)
            {
                Vector3 spread = (forward + Random.insideUnitSphere * 0.3f).normalized;
                ParticleSystem.EmitParams parameters = new ParticleSystem.EmitParams
                {
                    position = origin + spread * Random.Range(0f, 0.3f),
                    velocity = spread * Random.Range(9f, 19f),
                    startColor = new Color(0.94f, 0.98f, 1f),
                    startSize = Random.Range(0.08f, 0.22f),
                    startLifetime = Random.Range(0.16f, 0.32f),
                };
                blasts.Emit(parameters, 1);
            }
            PlayFlash(origin + forward * 0.25f, new Color(0.9f, 0.97f, 1f), 0.55f);
        }

        /// <summary>Specks of air rushing toward a vacuum from in front of it. Meant to be called many times a second.</summary>
        public static void PlaySuction(Vector3 mouth, Vector3 direction)
        {
            if (Application.isBatchMode) return;
            if (wisps == null) CreateWisps();
            Vector3 forward = direction.normalized;
            for (int i = 0; i < 2; i++)
            {
                float distance = Random.Range(1.2f, 6f);
                Vector3 start = mouth + forward * distance + Vector3.ProjectOnPlane(Random.insideUnitSphere, forward) * (distance * 0.4f);
                float life = Random.Range(0.22f, 0.4f);
                ParticleSystem.EmitParams parameters = new ParticleSystem.EmitParams
                {
                    position = start,
                    velocity = (mouth - start) / life,
                    startColor = new Color(0.86f, 0.95f, 1f),
                    startSize = Random.Range(0.025f, 0.07f),
                    startLifetime = life,
                };
                wisps.Emit(parameters, 1);
            }
        }

        /// <summary>The ground taking a hammer: a ring of dust going out, and bits of dirt going up.</summary>
        public static void PlaySlam(Vector3 impact)
        {
            if (Application.isBatchMode) return;
            if (blasts == null) CreateBlasts();
            Vector3 ground = new Vector3(impact.x, 0.12f, impact.z);
            for (int i = 0; i < 26; i++)
            {
                float angle = i / 26f * Mathf.PI * 2f;
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0.08f, Mathf.Sin(angle));
                ParticleSystem.EmitParams parameters = new ParticleSystem.EmitParams
                {
                    position = ground + outward * 0.3f,
                    velocity = outward * Random.Range(9f, 15f),
                    startColor = new Color(0.8f, 0.72f, 0.56f),
                    startSize = Random.Range(0.2f, 0.42f),
                    startLifetime = Random.Range(0.28f, 0.48f),
                };
                blasts.Emit(parameters, 1);
            }
            Play(ground + Vector3.up * 0.3f, new Color(0.45f, 0.36f, 0.24f), 16);
            PlayFlash(ground + Vector3.up * 0.5f, new Color(1f, 0.85f, 0.5f), 2.6f);
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

        private static void CreateBlasts()
        {
            blasts = NewSystem("FxBlasts", 300, 0f);
            ParticleSystem.MainModule main = blasts.main;
            main.startSpeed = 0f;

            ParticleSystem.ShapeModule shape = blasts.shape;
            shape.enabled = false;

            // Air slows down quickly once it's out.
            ParticleSystem.LimitVelocityOverLifetimeModule drag = blasts.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 0.5f;
            drag.dampen = 0.16f;

            ParticleSystem.SizeOverLifetimeModule size = blasts.sizeOverLifetime;
            size.enabled = true;
            AnimationCurve curve = new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.45f, 1.4f), new Keyframe(1f, 0f));
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);

            ParticleSystemRenderer renderer = blasts.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            renderer.sharedMaterial = GameConfig.Get().puffMaterial;
        }

        private static void CreateWisps()
        {
            wisps = NewSystem("FxWisps", 200, 0f);
            ParticleSystem.MainModule main = wisps.main;
            main.startSpeed = 0f;

            ParticleSystem.ShapeModule shape = wisps.shape;
            shape.enabled = false;

            ParticleSystemRenderer renderer = wisps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1f;
            renderer.velocityScale = 0.06f;
            renderer.sharedMaterial = GameConfig.Get().particleMaterial;
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
