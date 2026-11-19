using LightSide.Promo;
using UnityEngine;
using static LightSide.ParticleShowcase.ReelKit;

namespace LightSide.ParticleShowcase
{
    /// <summary>A treasure chest bursts open: gold coins as lit 3D meshes tumble out and bounce on the floor, glints lighting them in flight.</summary>
    public sealed class LootSlide : WorldSlide
    {
        private const float Burst = 0.7f;
        private UniParticlesWorld loot;
        private Transform lid;
        private UniText caption;

        protected override void BuildWorld(Stage stage)
        {
            Eye.backgroundColor = new Color(0.02f, 0.025f, 0.05f);
            Eye.fieldOfView = 38f;
            var floor = Primitive(PrimitiveType.Plane, Vector3.zero, new Vector3(3f, 1f, 3f), Lit(new Color(0.22f, 0.2f, 0.28f), 0.3f));
            var wood = Lit(new Color(0.45f, 0.24f, 0.1f), 0.25f);
            var gold = Lit(new Color(1f, 0.78f, 0.3f), 0.75f);
            gold.SetFloat("_Metallic", 1f);
            Primitive(PrimitiveType.Cube, new Vector3(0f, 0.32f, 0f), new Vector3(1.2f, 0.64f, 0.8f), wood);
            Primitive(PrimitiveType.Cube, new Vector3(0f, 0.64f, 0f), new Vector3(1.24f, 0.06f, 0.84f), gold);
            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(World, false);
            hinge.localPosition = new Vector3(0f, 0.67f, 0.4f);
            var lidObject = Primitive(PrimitiveType.Cube, Vector3.zero, new Vector3(1.22f, 0.14f, 0.8f), wood);
            lidObject.transform.SetParent(hinge, false);
            lidObject.transform.localPosition = new Vector3(0f, 0.07f, -0.4f);
            lid = hinge;

            var keyObject = new GameObject("Key Light", typeof(Light));
            keyObject.transform.SetParent(World, false);
            keyObject.transform.localRotation = Quaternion.Euler(50f, -30f, 0f);
            var key = keyObject.GetComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 0.45f;
            key.color = new Color(0.6f, 0.7f, 1f);

            var glintObject = new GameObject("Glint Light", typeof(Light));
            glintObject.transform.SetParent(World, false);
            var template = glintObject.GetComponent<Light>();
            template.type = LightType.Point;
            template.range = 1.4f;
            template.intensity = 2f;
            template.enabled = false;

            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var coin = Own(Object.Instantiate(cylinder.GetComponent<MeshFilter>().sharedMesh));
            Object.DestroyImmediate(cylinder);
            var vertices = coin.vertices;
            for (var i = 0; i < vertices.Length; i++) vertices[i] = new Vector3(vertices[i].x, vertices[i].y * 0.09f, vertices[i].z);
            coin.vertices = vertices;
            coin.RecalculateBounds();
            coin.RecalculateNormals();

            loot = Host("Loot", new Vector3(0f, 0.75f, 0f));
            var coins = Emitter(loot.Effect, 0, "Coins", 141, 120);
            coins.Space = ParticleSpace.World;
            var emission = Once(coins, 70f, Burst);
            emission.Bursts.Add(new ParticleBurst(Burst + 0.5f, 40f));
            coins.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Cone, Radius = 0.25f, ConeAngle = 32f, Rotation = new Vector3(-90f, 0f, 0f) });
            coins.Modules.Add(new StartModule { Lifetime = new ParticleRange(6f), Speed = new ParticleRange(3.2f, 5.2f), Size = new ParticleRange(0.22f, 0.28f) });
            coins.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -9.8f, 0f), Drag = 0.2f });
            var collision = new CollisionModule { Bounce = 0.35f, Dampen = 0.25f, RectWalls = ParticleRectWalls.None };
            collision.Planes.Add(floor.transform);
            coins.Modules.Add(collision);
            coins.Modules.Add(new SpinBySpeedModule { From = 0f, To = 1f, MinSpeed = 0.2f, MaxSpeed = 3f });
            coins.Look = new MeshLook { Mesh = coin, Material = gold, Orientation = ParticleMeshOrientation.Tumble };

            var glints = Emitter(loot.Effect, 1, "Glints", 142, 200);
            glints.Space = ParticleSpace.World;
            Once(glints, 80f, Burst);
            glints.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Cone, Radius = 0.3f, ConeAngle = 40f, Rotation = new Vector3(-90f, 0f, 0f) });
            glints.Modules.Add(new StartModule
            {
                Lifetime = new ParticleRange(0.8f, 1.6f), Speed = new ParticleRange(1.5f, 4f), Size = new ParticleRange(0.3f, 0.5f),
                Rotation = new ParticleRange(0f, 90f), ColorMode = ParticleColorMode.BetweenTwo, Color = new Color(1f, 0.95f, 0.7f), SecondColor = new Color(1f, 0.75f, 0.3f),
            });
            glints.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -3f, 0f), Drag = 1.2f });
            glints.Modules.Add(new SizeOverLifeModule { From = 1f, To = 0f, Curve = Ease.Of(EasingType.QuadraticIn) });
            glints.Modules.Add(new LightsModule { Light = template, MaxLights = 6, Ratio = 0.1f, SizeAffectsRange = false });
            glints.Look = Painted("Flare");
            ((ShapeLook)glints.Look).Facing = ParticleFacing.Camera;
            var flash = Add(loot.Effect, "Flash", 143, 4);
            Once(flash, 1f, Burst);
            flash.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point });
            flash.Modules.Add(new StartModule { Lifetime = new ParticleRange(0.5f), Speed = new ParticleRange(0f), Size = new ParticleRange(3.2f), Color = new Color(1f, 0.85f, 0.5f, 0.9f) });
            flash.Modules.Add(new SizeOverLifeModule { From = 0.3f, To = 1f, Curve = Ease.Of(EasingType.CubicOut) });
            flash.Modules.Add(new ColorOverLifeModule { Gradient = Ramp((0f, Color.white), (1f, new Color(1f, 1f, 1f, 0f))) });
            flash.Look = Glowing(1f);
            ((ShapeLook)flash.Look).Facing = ParticleFacing.Camera;
            var rays = Add(loot.Effect, "Rays", 144, 4);
            Once(rays, 1f, Burst);
            rays.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point });
            rays.Modules.Add(new StartModule { Lifetime = new ParticleRange(4f), Speed = new ParticleRange(0f), Size = new ParticleRange(4.5f), Spin = new ParticleRange(25f), Color = new Color(1f, 0.85f, 0.45f, 0.45f) });
            rays.Modules.Add(new SizeOverLifeModule { From = 0.3f, To = 1f, Curve = Ease.Of(EasingType.BackOut) });
            rays.Modules.Add(new ColorOverLifeModule { Gradient = Ramp((0f, new Color(1f, 1f, 1f, 0f)), (0.08f, Color.white), (0.75f, Color.white), (1f, new Color(1f, 1f, 1f, 0f))) });
            rays.Look = Painted("Rays");
            ((ShapeLook)rays.Look).Facing = ParticleFacing.Camera;

            caption = Outline(Text(stage.Root, "Meshes too — lit, tumbling, bouncing.", new Vector2(0f, 430f), new Vector2(1600f, 90f), 62f, Color.white),
                new Color(0.02f, 0.02f, 0.05f), 0.06f);
        }

        protected override void PoseWorld(float local)
        {
            var a = -0.35f + local * 0.05f;
            Eye.transform.localPosition = new Vector3(Mathf.Sin(a) * 5.2f, 2.1f, -Mathf.Cos(a) * 5.2f);
            Eye.transform.LookAt(World.TransformPoint(new Vector3(0f, 0.7f, 0f)));
            var open = Ease.Of(EasingType.BackOut).Evaluate(Window(local, Burst - 0.25f, Burst + 0.15f));
            var shake = local < Burst - 0.25f ? Mathf.Sin(local * 50f) * 3f * Window(local, 0.1f, Burst - 0.25f) : 0f;
            lid.localRotation = Quaternion.Euler(-105f * open + shake, 0f, 0f);
            Show(loot, local);
            caption.color = new Color(1f, 1f, 1f, Smooth(Window(local, 1.2f, 1.8f)));
        }
    }
}
