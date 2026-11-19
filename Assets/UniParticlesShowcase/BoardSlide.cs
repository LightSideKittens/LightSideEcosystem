using System.Collections.Generic;
using LightSide.Promo;
using UnityEngine;
using static LightSide.ParticleShowcase.ReelKit;

namespace LightSide.ParticleShowcase
{
    /// <summary>
    /// A match-three board: the player taps a rocket, its halves blast left and right trailing smoke, fire and sparks, every
    /// gem in the row shatters into shards of its color, and the gems above tumble down to fill the gap.
    /// </summary>
    public sealed class BoardSlide : ShotSlide
    {
        private const int Size = 7;
        private const int Row = 3;
        private const float Cell = 106f;
        private const float Press = 1f;
        private const float Speed = 950f;
        private const float Fall = Press + 0.55f;
        private static readonly Color[] Colors =
        {
            new(1f, 0.34f, 0.42f), new(0.35f, 0.66f, 1f), new(0.4f, 0.95f, 0.5f), new(1f, 0.85f, 0.3f), new(0.78f, 0.5f, 1f),
        };

        private readonly List<(RectTransform rect, int row, int col, bool fresh)> gems = new();
        private readonly List<UniParticles> trails = new();
        private UniParticles pops;
        private RectTransform rocket;
        private RectTransform[] halves;
        private RectTransform combo;
        private CanvasGroup comboGroup;
        private Pointer pointer;

        private static Vector2 At(int row, int col) => new((col - (Size - 1) * 0.5f) * Cell, (row - (Size - 1) * 0.5f) * Cell);

        private static float Pass(int col) => Press + Mathf.Abs(col - Size / 2) * Cell / Speed;

        protected override void OnBuild(Stage stage)
        {
            gems.Clear();
            trails.Clear();
            var root = stage.Root;
            Backdrop(root, new Color(0.45f, 0.75f, 0.95f), new Color(0.2f, 0.42f, 0.72f));
            Glow(root, "Sun", new Vector2(-700f, 420f), 900f, new Color(1f, 0.95f, 0.7f, 0.5f));
            var frame = Vertical(Shape(root, "Board", new Vector2(0f, -40f), new Vector2(Size * Cell + 50f, Size * Cell + 50f), ShapeKind.RoundedRect, 46f, Color.white),
                new Color(0.32f, 0.2f, 0.62f), new Color(0.2f, 0.12f, 0.45f));
            Shadow(frame, new Color(0.05f, 0.05f, 0.25f, 0.5f), new Vector2(0f, -20f), 50f, 6f);
            Stroke(frame, new Color(0.95f, 0.85f, 1f, 0.9f), 8f);
            var board = Node(frame.transform, "Grid", Vector2.zero, new Vector2(Size * Cell, Size * Cell));
            board.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            for (var r = 0; r < Size; r++)
            for (var c = 0; c < Size; c++)
                Shape(board, "Tile", At(r, c), new Vector2(Cell - 8f, Cell - 8f), ShapeKind.RoundedRect, 18f,
                    (r + c) % 2 == 0 ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.06f));

            var random = new System.Random(5);
            for (var r = 0; r <= Size; r++)
            for (var c = 0; c < Size; c++)
            {
                if (r == Row && c == Size / 2) continue;
                var gem = Shape(board, "Gem", At(r, c), new Vector2(Cell - 18f, Cell - 18f), ShapeKind.RoundedRect, 0f, Color.white);
                Textured(gem, "Gem");
                ((FillLayer)gem.Layers[0]).Paint.Color = Colors[random.Next(Colors.Length)];
                gems.Add(((RectTransform)gem.transform, r, c, r == Size));
            }

            rocket = Node(board, "Rocket", At(Row, Size / 2), new Vector2(Cell - 12f, Cell - 12f));
            Stroke(Vertical(Shape(rocket, "Body", Vector2.zero, new Vector2(92f, 48f), ShapeKind.Capsule, 0f, Color.white),
                new Color(1f, 0.65f, 0.3f), new Color(0.9f, 0.25f, 0.15f)), new Color(0.5f, 0.08f, 0.02f), 5f);
            Shape(rocket, "Stripe", Vector2.zero, new Vector2(16f, 46f), ShapeKind.RoundedRect, 4f, new Color(1f, 0.95f, 0.85f));
            halves = new RectTransform[2];
            for (var i = 0; i < 2; i++)
            {
                var half = Node(frame.transform, "Half", At(Row, Size / 2), new Vector2(80f, 48f));
                Stroke(Vertical(Shape(half, "Body", Vector2.zero, new Vector2(80f, 46f), ShapeKind.Capsule, 0f, Color.white),
                    new Color(1f, 0.7f, 0.3f), new Color(0.95f, 0.3f, 0.15f)), new Color(0.5f, 0.08f, 0.02f), 5f);
                half.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 180f : 0f);
                halves[i] = half;

                var trail = Host(frame.transform, "Trail", At(Row, Size / 2), new Vector2(10f, 10f));
                var smoke = Emitter(trail.Effect, 0, "Smoke", (uint)(300 + i), 200);
                smoke.Space = ParticleSpace.World;
                smoke.Modules.Add(new EmissionModule { Rate = 0f, RateOverDistance = 0.06f });
                smoke.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Circle, Radius = 14f, Direction = ParticleDirection.Random });
                smoke.Modules.Add(new StartModule
                {
                    Lifetime = new ParticleRange(0.6f, 0.9f), Speed = new ParticleRange(20f, 60f), Size = new ParticleRange(60f, 90f),
                    Rotation = new ParticleRange(0f, 360f), Spin = new ParticleRange(-60f, 60f), Color = new Color(1f, 1f, 1f, 0.85f),
                });
                smoke.Modules.Add(new SizeOverLifeModule { From = 0.6f, To = 1.6f, Curve = Ease.Of(EasingType.QuadraticOut) });
                smoke.Modules.Add(new ColorOverLifeModule { Gradient = Ramp((0f, new Color(1f, 0.95f, 0.85f, 1f)), (0.5f, new Color(0.85f, 0.85f, 0.9f, 0.7f)), (1f, new Color(0.8f, 0.8f, 0.85f, 0f))) });
                smoke.Look = Painted("Smoke", LayerBlend.Normal);

                var fire = Emitter(trail.Effect, 1, "Fire", (uint)(310 + i), 200);
                fire.Space = ParticleSpace.World;
                fire.Modules.Add(new EmissionModule { Rate = 0f, RateOverDistance = 0.1f });
                fire.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point });
                fire.Modules.Add(new StartModule { Lifetime = new ParticleRange(0.2f, 0.35f), Speed = new ParticleRange(0f, 30f), Size = new ParticleRange(60f, 90f), Color = new Color(1f, 0.6f, 0.2f) });
                fire.Modules.Add(new SizeOverLifeModule { From = 1f, To = 0.3f });
                fire.Modules.Add(new ColorOverLifeModule { Gradient = Ramp((0f, new Color(1f, 1f, 0.8f, 1f)), (1f, new Color(1f, 0.3f, 0.1f, 0f))) });
                fire.Look = Glowing(1f);

                var sparks = Emitter(trail.Effect, 2, "Sparks", (uint)(320 + i), 300);
                sparks.Space = ParticleSpace.World;
                sparks.Modules.Add(new EmissionModule { Rate = 0f, RateOverDistance = 0.12f });
                sparks.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Point, Direction = ParticleDirection.Random });
                sparks.Modules.Add(new StartModule { Lifetime = new ParticleRange(0.4f, 0.7f), Speed = new ParticleRange(150f, 420f), Size = new ParticleRange(14f, 22f), Color = new Color(1f, 0.85f, 0.4f) });
                sparks.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -1400f, 0f), Drag = 1f });
                sparks.Modules.Add(new ColorOverLifeModule { Gradient = Fade(0f, 0.5f) });
                sparks.Look = Glowing(0.6f);
                trails.Add(trail);
            }

            pops = Host(frame.transform, "Pops", Vector2.zero, new Vector2(Size * Cell, Size * Cell));
            var row = new List<(int col, Color color)>();
            foreach (var (_, r, c, _) in gems)
                if (r == Row) row.Add((c, Colors[0]));
            for (var i = 0; i < gems.Count; i++)
                if (gems[i].row == Row)
                {
                    var index = row.FindIndex(e => e.col == gems[i].col);
                    row[index] = (gems[i].col, ((FillLayer)gems[i].rect.GetComponent<UniShape>().Layers[0]).Paint.Color);
                }

            foreach (var (col, color) in row)
            {
                var shards = Add(pops.Effect, "Shards", (uint)(340 + col), 16);
                Once(shards, 9f, Pass(col));
                shards.Modules.Add(new ShapeModule { Kind = ParticleShapeKind.Circle, Radius = 24f, Position = At(Row, col), Direction = ParticleDirection.Shape });
                shards.Modules.Add(new StartModule
                {
                    Lifetime = new ParticleRange(0.7f, 1f), Speed = new ParticleRange(350f, 750f), Size = new ParticleRange(30f, 46f),
                    Rotation = new ParticleRange(0f, 360f), Spin = new ParticleRange(-500f, 500f), Color = color,
                });
                shards.Modules.Add(new ForceModule { Gravity = new Vector3(0f, -2000f, 0f), Drag = 0.8f });
                shards.Modules.Add(new SizeOverLifeModule { From = 1f, To = 0.3f, Curve = Ease.Of(EasingType.QuadraticIn) });
                shards.Look = Painted("Gem", LayerBlend.Normal);
            }

            foreach (var (col, color) in row) Flash(pops.Effect, (uint)(360 + col), Pass(col), At(Row, col), 300f, Color.Lerp(color, Color.white, 0.5f), 0.35f);
            foreach (var (col, _) in row) Flares(pops.Effect, (uint)(380 + col), Pass(col), At(Row, col), 2f, 170f, 400f, new Color(1f, 0.97f, 0.85f));

            combo = Node(root, "Combo", new Vector2(0f, 440f), new Vector2(700f, 160f));
            comboGroup = combo.gameObject.AddComponent<CanvasGroup>();
            Outline(Text(combo, "LINE BLAST!", Vector2.zero, new Vector2(700f, 160f), 120f, new Color(1f, 0.92f, 0.4f)), new Color(0.45f, 0.1f, 0.35f), 0.1f);

            pointer = stage.Pointer(new Vector2(500f, -500f), new[]
            {
                Beat.Wait(0.3f), Beat.To(new Vector2(10f, -45f), "rocket", 90f), Beat.Click("rocket", accent: true), Beat.Wait(0.2f),
                Beat.To(new Vector2(380f, -470f)),
            });
            Wake(stage.Root);
        }

        private static float HalfX(int side, float t) => side == 0 ? -Speed * Mathf.Max(0f, t - Press) : Speed * Mathf.Max(0f, t - Press);

        protected override void OnRender(float local)
        {
            pointer.Pose(local);
            var charge = local < Press ? Mathf.Sin(local * 30f) * 3f * Window(local, 0.6f, Press) : 0f;
            rocket.localRotation = Quaternion.Euler(0f, 0f, charge);
            rocket.gameObject.SetActive(local < Press);
            for (var i = 0; i < 2; i++)
            {
                halves[i].gameObject.SetActive(local >= Press);
                halves[i].anchoredPosition = At(Row, Size / 2) + new Vector2(HalfX(i, local), 0f);
                var trail = (RectTransform)trails[i].transform;
                var side = i;
                Replay(trails[i], local, t => trail.anchoredPosition = At(Row, Size / 2) + new Vector2(HalfX(side, t) - (side == 0 ? -40f : 40f) * (t >= Press ? 1f : 0f), 0f));
            }

            foreach (var (rect, r, c, fresh) in gems)
            {
                if (r == Row)
                {
                    rect.gameObject.SetActive(local < Pass(c));
                    continue;
                }

                if (r < Row) continue;
                var drop = Ease.Of(EasingType.BounceOut).Evaluate(Window(local, Fall + 0.04f * Mathf.Abs(c - Size / 2) + 0.03f * (r - Row), Fall + 0.04f * Mathf.Abs(c - Size / 2) + 0.03f * (r - Row) + 0.45f));
                rect.anchoredPosition = At(r, c) - new Vector2(0f, Cell * drop);
            }

            Show(pops, local);
            var pop = Window(local, Press + 0.25f, Press + 0.6f);
            comboGroup.alpha = Smooth(Window(local, Press + 0.25f, Press + 0.35f)) * (1f - Smooth(Window(local, Press + 1.6f, Press + 2f)));
            combo.anchoredPosition = new Vector2(0f, 440f + 40f * (1f - Ease.Of(EasingType.BackOut).Evaluate(pop)));
        }
    }
}
