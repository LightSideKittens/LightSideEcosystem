#if LIGHTSIDE_HAS_UNIPARTICLES
using System;
using LightSide;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Particle workloads of <see cref="UniTextScenarioSuite"/>: the same fountains, and the same fireworks, drawn by
/// UniParticles on the closed-form tier, on Burst, and by Unity's <c>ParticleSystem</c>, on the rig's Canvas and in
/// world space, at two densities.
/// </summary>
/// <remarks>
/// Every fountain participant runs <see cref="Effects"/> prewarmed looping fountains living <see cref="Lifetime"/>
/// seconds, 100 births a second each (about 4,800 particles) or 1,000 in the <c>dense</c> variants (about 48,000).
/// Every firework participant runs as many prewarmed looping launchers: rockets living
/// <see cref="RocketLifetime"/> seconds, each bursting into <see cref="Sparks"/> sparks at its death through a death
/// sub-emitter, 2 launches a second (about 7,200 particles) or 8 in the dense variants (about 29,000). All are alive
/// from the first frame; validation throws when a participant holds fewer than 70 % of them. Nothing is stepped: the
/// frame is each engine's own advance and draw. A <c>ParticleSystem</c> on a Canvas is the usual workaround, a world
/// renderer in the canvas plane, scaled by the hierarchy and drawn with the render pipeline's default particle material.
/// </remarks>
static class UniParticlesScenarios
{
    const int Columns = 6;
    const int Rows = 4;
    const int Effects = Columns * Rows;
    const float Lifetime = 2f;
    const float Rate = 100f;
    const float DenseRate = 1000f;
    const float WorldUnit = 0.01f;
    const float RocketLifetime = 1f;
    const float SparkLifetime = 1.5f;
    const int Sparks = 100;
    const float Launches = 2f;
    const float DenseLaunches = 8f;

    public static UniTextScenario[] Create() => new UniTextScenario[]
    {
        new Fountains("particles.closed", Engine.ClosedForm, false, Rate),
        new Fountains("particles.burst", Engine.Burst, false, Rate),
        new Fountains("particles.shuriken", Engine.Shuriken, false, Rate),
        new Fountains("particles.world.closed", Engine.ClosedForm, true, Rate),
        new Fountains("particles.world.burst", Engine.Burst, true, Rate),
        new Fountains("particles.world.shuriken", Engine.Shuriken, true, Rate),
        new Fountains("particles.dense.closed", Engine.ClosedForm, false, DenseRate),
        new Fountains("particles.dense.burst", Engine.Burst, false, DenseRate),
        new Fountains("particles.dense.shuriken", Engine.Shuriken, false, DenseRate),
        new Fountains("particles.dense.world.closed", Engine.ClosedForm, true, DenseRate),
        new Fountains("particles.dense.world.burst", Engine.Burst, true, DenseRate),
        new Fountains("particles.dense.world.shuriken", Engine.Shuriken, true, DenseRate),
        new Fountains("particles.fireworks.closed", Engine.ClosedForm, false, Launches, true),
        new Fountains("particles.fireworks.burst", Engine.Burst, false, Launches, true),
        new Fountains("particles.fireworks.shuriken", Engine.Shuriken, false, Launches, true),
        new Fountains("particles.fireworks.world.closed", Engine.ClosedForm, true, Launches, true),
        new Fountains("particles.fireworks.world.burst", Engine.Burst, true, Launches, true),
        new Fountains("particles.fireworks.world.shuriken", Engine.Shuriken, true, Launches, true),
        new Fountains("particles.fireworks.dense.closed", Engine.ClosedForm, false, DenseLaunches, true),
        new Fountains("particles.fireworks.dense.burst", Engine.Burst, false, DenseLaunches, true),
        new Fountains("particles.fireworks.dense.shuriken", Engine.Shuriken, false, DenseLaunches, true),
        new Fountains("particles.fireworks.dense.world.closed", Engine.ClosedForm, true, DenseLaunches, true),
        new Fountains("particles.fireworks.dense.world.burst", Engine.Burst, true, DenseLaunches, true),
        new Fountains("particles.fireworks.dense.world.shuriken", Engine.Shuriken, true, DenseLaunches, true),
    };

    /// <summary>Spark slots a launcher of <paramref name="launches"/> a second holds: every launch whose sparks can live at once, worst case.</summary>
    static int SparkCapacity(float launches) => (Mathf.CeilToInt(launches * (RocketLifetime + SparkLifetime)) + 1) * Sparks;

    /// <summary>
    /// Makes <paramref name="effect"/> one firework launcher at <paramref name="unit"/> world units per canvas unit:
    /// rockets, and sparks answering their deaths; a zero-strength noise module holds the Burst participant off the
    /// closed-form tier.
    /// </summary>
    static void Firework(ParticleEffect effect, int index, float unit, float launches, bool burst, bool world)
    {
        var rockets = effect.Emitters[0];
        rockets.Seed = (uint)(2000 + index);
        rockets.Capacity = Mathf.CeilToInt(launches * RocketLifetime) + 4;
        rockets.Prewarm = true;
        var modules = rockets.Modules;
        modules.Clear();
        modules.Add(new EmissionModule { Rate = launches });
        modules.Add(new ShapeModule
        {
            Kind = ParticleShapeKind.Point, Direction = ParticleDirection.Fixed, DirectionVector = Vector3.up, Spread = 10f,
        });
        modules.Add(new StartModule
        {
            Lifetime = new ParticleRange(RocketLifetime), Speed = new ParticleRange(450f * unit, 550f * unit),
            Size = new ParticleRange(8f * unit),
            ColorMode = ParticleColorMode.BetweenTwo, Color = new Color(1f, 0.85f, 0.3f), SecondColor = new Color(0.3f, 0.6f, 1f),
        });
        modules.Add(new ForceModule { Gravity = new Vector3(0f, -300f * unit, 0f) });
        if (burst) modules.Add(new NoiseModule { Strength = 0f });
        var facing = world ? ParticleFacing.Camera : ParticleFacing.Plane;
        rockets.Look = new ShapeLook { Outline = new ParticleOutline(ShapeKind.Circle), Softness = 1f, Facing = facing };

        var sparks = new ParticleEmitter { Name = "Sparks", Seed = (uint)(3000 + index), Capacity = SparkCapacity(launches) };
        sparks.Modules.Clear();
        sparks.Modules.Add(new SubEmitterModule { Source = rockets.NodeId, Count = new ParticleRange(Sparks), InheritColor = true });
        sparks.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point, Direction = ParticleDirection.Random });
        sparks.Modules.Add(new StartModule
        {
            Lifetime = new ParticleRange(SparkLifetime), Speed = new ParticleRange(100f * unit, 250f * unit),
            Size = new ParticleRange(6f * unit, 10f * unit),
        });
        sparks.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -150f * unit, 0f) });
        sparks.Modules.Add(new SizeOverLifeModule { From = 1f, To = 0.2f });
        sparks.Modules.Add(new ColorOverLifeModule());
        if (burst) sparks.Modules.Add(new NoiseModule { Strength = 0f });
        sparks.Look = new ShapeLook { Outline = new ParticleOutline(ShapeKind.Circle), Softness = 1f, Facing = facing };
        effect.Emitters.Add(sparks);
    }

    /// <summary>
    /// The same launcher as a <c>ParticleSystem</c> with a death sub-emitter on the inactive <paramref name="go"/>, run
    /// on past the longest spark life: right after a prewarm, Play Mode spawns the sparks of the deaths the prewarm
    /// simulated at age 0, 100–200 sparks above the steady count for the first 1.5 s.
    /// </summary>
    static ParticleSystem ShurikenFirework(GameObject go, int index, float unit, float launches, Material material)
    {
        var sparkObject = new GameObject("Sparks");
        sparkObject.layer = go.layer;
        sparkObject.transform.SetParent(go.transform, false);
        var spark = sparkObject.AddComponent<ParticleSystem>();
        sparkObject.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        spark.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        spark.useAutoRandomSeed = false;
        spark.randomSeed = (uint)(3000 + index);
        var sparkMain = spark.main;
        sparkMain.loop = false;
        sparkMain.playOnAwake = false;
        sparkMain.maxParticles = SparkCapacity(launches);
        sparkMain.scalingMode = ParticleSystemScalingMode.Hierarchy;
        sparkMain.simulationSpace = ParticleSystemSimulationSpace.Local;
        sparkMain.startLifetime = SparkLifetime;
        sparkMain.startSpeed = new ParticleSystem.MinMaxCurve(100f * unit, 250f * unit);
        sparkMain.startSize = new ParticleSystem.MinMaxCurve(6f * unit, 10f * unit);
        var sparkEmission = spark.emission;
        sparkEmission.rateOverTime = 0f;
        sparkEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Sparks) });
        var sparkShape = spark.shape;
        sparkShape.shapeType = ParticleSystemShapeType.Sphere;
        sparkShape.radius = 0.0001f;
        var sparkForce = spark.forceOverLifetime;
        sparkForce.enabled = true;
        sparkForce.space = ParticleSystemSimulationSpace.Local;
        sparkForce.y = -150f * unit;
        var sparkColor = spark.colorOverLifetime;
        sparkColor.enabled = true;
        var fade = new UnityEngine.Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        sparkColor.color = fade;
        var sparkSize = spark.sizeOverLifetime;
        sparkSize.enabled = true;
        sparkSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

        var system = go.AddComponent<ParticleSystem>();
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        system.useAutoRandomSeed = false;
        system.randomSeed = (uint)(2000 + index);
        var main = system.main;
        main.duration = 5f;
        main.loop = true;
        main.prewarm = true;
        main.maxParticles = Mathf.CeilToInt(launches * RocketLifetime) + 4;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = RocketLifetime;
        main.startSpeed = new ParticleSystem.MinMaxCurve(450f * unit, 550f * unit);
        main.startSize = 8f * unit;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.3f), new Color(0.3f, 0.6f, 1f));
        var emission = system.emission;
        emission.rateOverTime = launches;
        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 10f;
        shape.radius = 0.0001f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var force = system.forceOverLifetime;
        force.enabled = true;
        force.space = ParticleSystemSimulationSpace.Local;
        force.y = -300f * unit;
        var subs = system.subEmitters;
        subs.enabled = true;
        subs.AddSubEmitter(spark, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritColor);
        go.SetActive(true);
        system.Play(false);
        system.Simulate(RocketLifetime + SparkLifetime, true, false, true);
        system.Play(false);
        return system;
    }

    enum Engine
    {
        ClosedForm,
        Burst,
        Shuriken,
    }

    static int Capacity(float rate) => Mathf.CeilToInt(rate * Lifetime * 1.25f) + 16;

    /// <summary>Makes <paramref name="effect"/> one fountain at <paramref name="unit"/> world units per canvas unit; a zero-strength noise module holds the Burst participant off the closed-form tier.</summary>
    static void Fountain(ParticleEffect effect, int index, float unit, float rate, bool burst, bool world)
    {
        var emitter = effect.Emitters[0];
        emitter.Seed = (uint)(1000 + index);
        emitter.Capacity = Capacity(rate);
        emitter.Prewarm = true;
        var modules = emitter.Modules;
        modules.Clear();
        modules.Add(new EmissionModule { Rate = rate });
        modules.Add(new ShapeModule
        {
            Kind = ParticleShapeKind.Point, Direction = ParticleDirection.Fixed, DirectionVector = Vector3.up, Spread = 15f,
        });
        modules.Add(new StartModule
        {
            Lifetime = new ParticleRange(Lifetime), Speed = new ParticleRange(300f * unit, 450f * unit),
            Size = new ParticleRange(12f * unit, 20f * unit),
            ColorMode = ParticleColorMode.BetweenTwo, Color = new Color(1f, 0.85f, 0.3f), SecondColor = new Color(1f, 0.4f, 0.15f),
        });
        modules.Add(new ForceModule { Gravity = new Vector3(0f, -400f * unit, 0f) });
        modules.Add(new SizeOverLifeModule { From = 1f, To = 0.2f });
        modules.Add(new ColorOverLifeModule());
        if (burst) modules.Add(new NoiseModule { Strength = 0f });
        emitter.Look = new ShapeLook
        {
            Outline = new ParticleOutline(ShapeKind.Circle), Softness = 1f,
            Facing = world ? ParticleFacing.Camera : ParticleFacing.Plane,
        };
    }

    /// <summary>The same fountain as a <c>ParticleSystem</c> on the inactive <paramref name="go"/>, at <paramref name="unit"/> local units per canvas unit.</summary>
    static ParticleSystem Shuriken(GameObject go, int index, float unit, float rate, Material material)
    {
        var system = go.AddComponent<ParticleSystem>();
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        system.useAutoRandomSeed = false;
        system.randomSeed = (uint)(1000 + index);
        var main = system.main;
        main.duration = 5f;
        main.loop = true;
        main.prewarm = true;
        main.maxParticles = Capacity(rate);
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = Lifetime;
        main.startSpeed = new ParticleSystem.MinMaxCurve(300f * unit, 450f * unit);
        main.startSize = new ParticleSystem.MinMaxCurve(12f * unit, 20f * unit);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.4f, 0.15f));
        var emission = system.emission;
        emission.rateOverTime = rate;
        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;
        shape.radius = 0.0001f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var force = system.forceOverLifetime;
        force.enabled = true;
        force.space = ParticleSystemSimulationSpace.Local;
        force.y = -400f * unit;
        var colorOverLife = system.colorOverLifetime;
        colorOverLife.enabled = true;
        var fade = new UnityEngine.Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = fade;
        var sizeOverLife = system.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
        go.SetActive(true);
        system.Play();
        return system;
    }

    /// <summary>A grid of fountains on the rig's Canvas, or camera-facing ones 12 units before the rig's camera.</summary>
    sealed class Fountains : UniTextScenario
    {
        readonly Engine engine;
        readonly bool world;
        readonly float rate;
        readonly bool fireworks;
        readonly Func<int>[] counts = new Func<int>[Effects];

        public Fountains(string id, Engine engine, bool world, float rate, bool fireworks = false) : base(id, true)
        {
            this.engine = engine;
            this.world = world;
            this.rate = rate;
            this.fireworks = fireworks;
        }

        int Expected => (int)(Effects * (fireworks ? rate * (RocketLifetime + Sparks * SparkLifetime) : rate * Lifetime));

        public override void Setup(ScenarioRig rig)
        {
            var root = world ? rig.CreateWorldRoot(Id) : rig.CreateRoot(Id);
            var unit = world ? WorldUnit : 1f;
            for (var i = 0; i < Effects; i++)
            {
                var go = world ? new GameObject("Fountain " + i) : new GameObject("Fountain " + i, typeof(RectTransform)) { layer = 5 };
                go.SetActive(false);
                go.transform.SetParent(root, false);
                var place = new Vector2((i % Columns + 0.5f) / Columns, (i / Columns + 0.2f) / Rows);
                if (world) go.transform.position = rig.Camera.ViewportToWorldPoint(new Vector3(place.x, place.y, 12f));
                else
                {
                    var rect = (RectTransform)go.transform;
                    rect.anchorMin = rect.anchorMax = place;
                    rect.sizeDelta = new Vector2(100f, 100f);
                }

                if (engine == Engine.Shuriken)
                {
                    if (!fireworks)
                    {
                        var system = Shuriken(go, i, unit, rate, rig.ParticleMaterial);
                        counts[i] = () => system.particleCount;
                        continue;
                    }

                    var rockets = ShurikenFirework(go, i, unit, rate, rig.ParticleMaterial);
                    var sparks = go.transform.GetChild(0).GetComponent<ParticleSystem>();
                    counts[i] = () => rockets.particleCount + sparks.particleCount;
                    continue;
                }

                if (world)
                {
                    var particles = go.AddComponent<UniParticlesWorld>();
                    Build(particles.Effect, i, unit, true);
                    counts[i] = () => particles.ParticleCount;
                }
                else
                {
                    var particles = go.AddComponent<UniParticles>();
                    Build(particles.Effect, i, unit, false);
                    counts[i] = () => particles.ParticleCount;
                }

                go.SetActive(true);
            }
        }

        void Build(ParticleEffect effect, int index, float unit, bool inWorld)
        {
            if (fireworks) Firework(effect, index, unit, rate, engine == Engine.Burst, inWorld);
            else Fountain(effect, index, unit, rate, engine == Engine.Burst, inWorld);
        }

        public override void Validate()
        {
            var live = Live();
            if (live * 10 < Expected * 7)
                throw new InvalidOperationException($"{live} particles live where about {Expected} were expected.");
        }

        public override void Describe(JObject workload)
        {
            workload["engine"] = engine switch
            {
                Engine.ClosedForm => "UniParticles closed form",
                Engine.Burst => "UniParticles Burst",
                _ => "ParticleSystem",
            };
            workload["space"] = world ? "world" : "canvas";
            workload["effect"] = fireworks ? "fireworks" : "fountains";
            workload["effects"] = Effects;
            workload["particles"] = Live();
        }

        int Live()
        {
            var live = 0;
            for (var i = 0; i < Effects; i++) live += counts[i]();
            return live;
        }
    }
}
#endif
