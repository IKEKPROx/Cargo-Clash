using UnityEngine;

namespace CargoClash.RiverV10
{
    // Local visual effect. No NGO dependency, scoring, Rigidbody forces, or gameplay clock changes.
    [DisallowMultipleComponent]
    public sealed class RiverWeather : MonoBehaviour
    {
        public Camera targetCamera;
        [Tooltip("Optional overview-demo focus. Leave empty for first-person camera-follow rain.")]
        public Transform precipitationAnchor;
        public Material rainMaterial;
        public Material splashMaterial;
        public ParticleSystem rainfall;
        public ParticleSystem splashes;
        public RiverLevelReferences level;
        public Renderer[] rippleSurfaces;
        [Range(0, 1)] public float intensity = 0.65f;
        public float radius = 18f;
        public float heightAboveCamera = 9f;
        public float dropsPerSecond = 1800f;
        public float fallSpeed = 16f;
        public Vector2 wind = new Vector2(-1.1f, 0.2f);
        public LayerMask groundLayers = 1;
        [Range(0, 160)] public float splashesPerSecond = 55f;
        private float splashRemainder;
        private MaterialPropertyBlock propertyBlock;
        private System.Random rng = new System.Random(1042);

        private void OnEnable()
        {
            if (!targetCamera) targetCamera = Camera.main;
            if (!rainfall || !splashes) Configure();
            rainfall.Play(); splashes.Play();
        }

        public void Configure()
        {
            if (!rainfall) rainfall = new GameObject("CameraRain").AddComponent<ParticleSystem>();
            rainfall.transform.SetParent(transform, false);
            rainfall.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = rainfall.main;
            main.loop = true; main.duration = 4; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1.4f; main.startSpeed = 0; main.startSize = new ParticleSystem.MinMaxCurve(.014f,.026f);
            main.startColor = new Color(.85f,.93f,1,.48f); main.maxParticles = 5500; main.playOnAwake = true;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = rainfall.emission; emission.rateOverTime = dropsPerSecond * intensity;
            var shape = rainfall.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(radius*2,.1f,radius*2);
            var velocity = rainfall.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = wind.x; velocity.y = -fallSpeed; velocity.z = wind.y;
            var collision = rainfall.collision; collision.enabled = true; collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D; collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.collidesWith = groundLayers; collision.enableDynamicColliders = false; collision.lifetimeLoss = 1;
            var renderer = rainfall.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = rainMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.velocityScale = .02f; renderer.lengthScale = 9;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            if (!splashes) splashes = new GameObject("SurfaceSplashes").AddComponent<ParticleSystem>();
            splashes.transform.SetParent(transform,false);splashes.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var sm = splashes.main; sm.loop=true;sm.startLifetime=.45f;sm.startSpeed=0;sm.startSize=new ParticleSystem.MinMaxCurve(.12f,.3f);sm.maxParticles=300;sm.simulationSpace=ParticleSystemSimulationSpace.World;sm.playOnAwake=true;
            var se=splashes.emission;se.enabled=false;var ss=splashes.shape;ss.enabled=false;
            var size=splashes.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.1f,1,1));
            var fade=splashes.colorOverLifetime;fade.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(.65f,0),new GradientAlphaKey(0,1)});fade.color=gradient;
            var sr=splashes.GetComponent<ParticleSystemRenderer>();sr.sharedMaterial=splashMaterial;sr.renderMode=ParticleSystemRenderMode.HorizontalBillboard;sr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            rainfall.Play();splashes.Play();
        }

        private void LateUpdate() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            if (!targetCamera || !rainfall) return;
            Vector3 camera = precipitationAnchor ? precipitationAnchor.position : targetCamera.transform.position;
            rainfall.transform.position = camera + Vector3.up * heightAboveCamera;
            var emission = rainfall.emission; emission.rateOverTime = dropsPerSecond * intensity;
            splashRemainder += deltaTime * splashesPerSecond * intensity;
            int count = Mathf.Min(12, (int)splashRemainder); splashRemainder -= count;
            for (int i=0;i<count;++i)
            {
                Vector3 p=camera+new Vector3((float)(rng.NextDouble()*2-1)*radius,16,(float)(rng.NextDouble()*2-1)*radius);
                bool found=Physics.Raycast(p,Vector3.down,out RaycastHit hit,70,groundLayers,QueryTriggerInteraction.Ignore);
                Vector3 surface=found?hit.point:Vector3.zero;
                if(level && level.flowPath && level.flowPath.TrySample(level.transform,p,out Vector3 water,out _) && (!found || water.y>surface.y)) {surface=water;found=true;}
                if(found && (!hit.collider || hit.normal.y>.65f || surface.y>hit.point.y+.01f))
                    splashes.Emit(new ParticleSystem.EmitParams{position=surface+Vector3.up*.018f},1);
            }
            propertyBlock ??= new MaterialPropertyBlock();
            foreach(var surface in rippleSurfaces)
            {
                if(!surface)continue;
                surface.GetPropertyBlock(propertyBlock);propertyBlock.SetFloat("_RainStrength",.24f*intensity);surface.SetPropertyBlock(propertyBlock);
            }
        }

        private void OnDisable()
        {
            if(rainfall)rainfall.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(splashes)splashes.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(rippleSurfaces!=null) foreach(var r in rippleSurfaces) if(r) r.SetPropertyBlock(null);
        }
    }
}
