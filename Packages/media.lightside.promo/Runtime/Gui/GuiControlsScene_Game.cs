using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    public sealed partial class GuiControlsScene
    {
        /// <summary>
        /// Ready-made game UI: a top-down action game and its whole HUD. The joystick walks the hero from the camp to three
        /// slimes, the camera following; the attack button swings at the first — a hit, then a critical that slays it — a
        /// slime strikes back, the whirlwind fells the other two, their coins fly into the purse, the quest completes, the
        /// hero levels up and drinks a potion; the joystick then runs the hero toward the gate the quest now points at, and
        /// the next slide pushes in along the run.
        /// </summary>
        /// <remarks>
        /// The game's world is the film's own shapes, laid out in world units the camera follows; the minimap is the kit's,
        /// drawing a world of plain blocks laid out the same, one tenth the scale, on a layer only its camera sees, so map and
        /// view always agree. The HUD is the kit's controls — joystick, cooldowns, minimap, world marker, segmented
        /// progress, toasts — with the game's frames and bars drawn around them.
        /// </remarks>
        private sealed class GamePart : KitPart
        {
            public GamePart(GuiControlsScene scene, Stage stage) : base(scene, stage, "Game", Board, Scale)
            {
                this.scene = scene;
                this.stage = stage;
                face = scene.theme.DisplayFace;
                var view = stage.Shape("View", Holder, ShapeKind.RoundedRect, BoardRadius / Scale);
                stage.Box(view.Rect, Vector2.zero, Board / Scale);
                Stage.Solid(view.Fill, Grass);
                view.Rect.gameObject.AddComponent<UniShapeMask>().ShowShape = true;
                world = stage.Node("World", view.Rect);
                stage.Box(world, Vector2.zero, Vector2.zero);
                Ground();
                foes = new Foe[FoeStart.Length];
                for (var i = 0; i < foes.Length; i++) foes[i] = Slime(i);
                Knight();
                Effects();
                Hud();
                Map();
                Loot();

                Plan(At(StickAt + new Vector2(60f, 80f)), new[]
                {
                    Beat.Wait(0.3f),
                    Beat.To(At(StickAt), null, Wide(140f)),
                    Beat.Drag(At(StickAt + Approach), "approach", Wide(56f), ApproachHold),
                    Beat.To(At(AttackAt), null, Wide(AttackSize)),
                    Beat.Click("attack"),
                    Beat.Wait(0.18f),
                    Beat.Click("critical", false, true),
                    Beat.Wait(0.35f),
                    Beat.To(At(WhirlAt), null, Wide(SkillSize)),
                    Beat.Click("whirl", true, true),
                    Beat.Wait(LevelRead),
                    Beat.To(At(PotionAt), null, Wide(SkillSize)),
                    Beat.Click("potion", true),
                    Beat.To(At(StickAt), null, Wide(140f)),
                    Beat.Drag(At(StickAt + Run), "run", Wide(56f), RunHold)
                }, Pace);
                Aim("attack", attack);
                Aim("critical", attack);
                Aim("whirl", skills[0]);
                Aim("potion", skills[2]);
                Aim("approach", stick);
                Aim("run", stick);
            }

            private readonly GuiControlsScene scene;
            private readonly Stage stage;
            private readonly UniTextFont face;
            private readonly RectTransform world;
            private readonly Foe[] foes;
            private RectTransform hero;
            private RectTransform blade;
            private RectTransform cape;
            private UniShape heroFlash;
            private RectTransform slash;
            private RectTransform whirl;
            private RectTransform healRing;
            private readonly List<RectTransform> dust = new();
            private readonly List<(RectTransform node, int foe)> coins = new();
            private readonly List<(RectTransform node, string id, int foe)> numbers = new();
            private RectTransform[] flames;
            private RectTransform portal;
            private RectTransform health;
            private RectTransform mana;
            private UniText healthText;
            private UniText goldText;
            private UniText questTitle;
            private UniText questCount;
            private UniText levelText;
            private RectTransform target;
            private RectTransform targetLife;
            private RectTransform banner;
            private UniShapeValue experience;
            private UniShapeValue horizontal;
            private float deadZone;
            private RectTransform spokes;
            private UniShapeValue vertical;
            private GameObject stick;
            private UniShapeValue attack;
            private readonly UniShapeValue[] skills = new UniShapeValue[3];
            private UniMessages toasts;
            private Transform mapWorld;
            private Transform mapHero;
            private readonly Transform[] mapFoes = new Transform[FoeStart.Length];
            private bool toasted;

            /// <summary>A slime: where it stands, its body, its hit flash and the bar of its life on its nameplate.</summary>
            private sealed class Foe
            {
                public RectTransform Root;
                public RectTransform Body;
                public UniShape Flash;
                public RectTransform Life;
                public RectTransform Plate;
            }

            #region World

            private Widget Blob(Transform parent, string name, ShapeKind kind, Vector2 at, Vector2 size, Color color, float radius = 0f)
            {
                var shape = stage.Shape(name, parent, kind, radius);
                stage.Box(shape.Rect, at, size);
                Stage.Solid(shape.Fill, color);
                return shape;
            }

            /// <summary>The ground: grass in patches and tufts, the dirt road from the camp to the gate, flowers, rocks, trees, the camp and the gate.</summary>
            private void Ground()
            {
                foreach (var (at, size) in Patches) Blob(world, "Patch", ShapeKind.Ellipse, at, size, GrassLight);
                foreach (var (width, color) in new[] { (RoadWidth + 14f, DirtEdge), (RoadWidth, Dirt), (RoadWidth * 0.35f, DirtLit) })
                {
                    var road = Blob(world, "Road", ShapeKind.RoundedRect, Vector2.zero, Vector2.one * 2400f, color);
                    var path = Stage.Contour(Road, 1f, false, true);
                    path.Width = width;
                    path.Cap = EndCap.Round;
                    road.Shape.Shape = path;
                }

                for (var i = 0; i < Tufts.Length; i++)
                {
                    var tuft = Blob(world, "Tuft", ShapeKind.Triangle, Tufts[i], new Vector2(12f, 14f), GrassDark);
                    tuft.Rect.localRotation = Quaternion.Euler(0f, 0f, (i * 37f) % 30f - 15f);
                }

                for (var i = 0; i < Flowers.Length; i++)
                    Blob(world, "Flower", ShapeKind.Circle, Flowers[i], Vector2.one * 7f, FlowerHues[i % FlowerHues.Length]);
                foreach (var (at, size) in Rocks)
                {
                    Blob(world, "Shade", ShapeKind.Ellipse, at + new Vector2(6f, -8f), size, new Color(0f, 0f, 0f, 0.22f));
                    var rock = Blob(world, "Rock", ShapeKind.Octagon, at, size, Stone, size.x * 0.25f);
                    Stage.Linear(rock.Fill, StoneLit, Stone, 300f);
                    Stage.AddStroke(rock.Shape, StoneDark, 2f, -1f);
                }

                Camp();
                Gate();
                foreach (var (at, size) in Trees) Tree(at, size);
            }

            private void Tree(Vector2 at, float size)
            {
                Blob(world, "Shade", ShapeKind.Circle, at + new Vector2(14f, -18f), Vector2.one * size, new Color(0f, 0.05f, 0f, 0.3f));
                var crown = Blob(world, "Crown", ShapeKind.Circle, at, Vector2.one * size, Leaf);
                Stage.Radial(crown.Fill, LeafLit, Leaf);
                Stage.AddStroke(crown.Shape, LeafDark, 2.5f, -1f);
                Blob(crown.Rect, "Tuft", ShapeKind.Circle, new Vector2(size * 0.18f, size * 0.12f), Vector2.one * size * 0.42f, Theme.Fade(LeafLit, 0.85f));
                Blob(crown.Rect, "Glint", ShapeKind.Circle, new Vector2(-size * 0.2f, size * 0.22f), Vector2.one * size * 0.24f, new Color(1f, 1f, 0.8f, 0.18f));
            }

            /// <summary>The camp the hero starts in: two tents, a log and a fire in a ring of stones, its light on the ground.</summary>
            private void Camp()
            {
                var glow = Blob(world, "Firelight", ShapeKind.Circle, FireAt, Vector2.one * 260f, Color.white);
                Stage.Radial(glow.Fill, new Color(1f, 0.7f, 0.3f, 0.35f), new Color(1f, 0.6f, 0.2f, 0f));
                foreach (var (at, turn) in Tents)
                {
                    var tent = stage.Node("Tent", world);
                    stage.Box(tent, at, Vector2.zero);
                    tent.localRotation = Quaternion.Euler(0f, 0f, turn);
                    Blob(tent, "Shade", ShapeKind.RoundedRect, new Vector2(10f, -14f), new Vector2(126f, 96f), new Color(0f, 0f, 0f, 0.25f), 8f);
                    var lit = Blob(tent, "Lit", ShapeKind.RoundedRect, new Vector2(0f, 23f), new Vector2(120f, 46f), CanvasLit, 6f);
                    Stage.AddStroke(lit.Shape, CanvasDark, 2.5f, -1f);
                    var shaded = Blob(tent, "Shaded", ShapeKind.RoundedRect, new Vector2(0f, -23f), new Vector2(120f, 46f), Canvas, 6f);
                    Stage.AddStroke(shaded.Shape, CanvasDark, 2.5f, -1f);
                    Blob(tent, "Ridge", ShapeKind.Capsule, Vector2.zero, new Vector2(126f, 6f), CanvasDark);
                    var door = Blob(tent, "Door", ShapeKind.Triangle, new Vector2(54f, 0f), new Vector2(40f, 22f), WoodDark, 2f);
                    door.Rect.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }

                var log = Blob(world, "Log", ShapeKind.Capsule, FireAt + new Vector2(-6f, -54f), new Vector2(84f, 20f), Wood);
                Stage.AddStroke(log.Shape, WoodDark, 2f, -1f);
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * Mathf.PI / 4f;
                    Blob(world, "Stone", ShapeKind.Circle, FireAt + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 22f, Vector2.one * 13f, StoneLit);
                }

                flames = new RectTransform[3];
                for (var i = 0; i < flames.Length; i++)
                {
                    var flame = Blob(world, "Flame", ShapeKind.Circle, FireAt + new Vector2((i - 1) * 7f, 2f), Vector2.one * (24f - i * 5f), Color.white);
                    Stage.Radial(flame.Fill, i == 2 ? new Color(1f, 0.98f, 0.8f) : new Color(1f, 0.82f, 0.3f), i == 0 ? new Color(1f, 0.35f, 0.1f, 0.9f) : new Color(1f, 0.55f, 0.15f, 0.9f));
                    flames[i] = flame.Rect;
                }
            }

            /// <summary>The quest's gate: two stone pillars and a lintel round a portal that turns.</summary>
            private void Gate()
            {
                var glow = Blob(world, "Glow", ShapeKind.Circle, GateAt, Vector2.one * 300f, Color.white);
                Stage.Radial(glow.Fill, new Color(0.7f, 0.4f, 1f, 0.45f), new Color(0.5f, 0.2f, 1f, 0f));
                var floor = Blob(world, "Dais", ShapeKind.Octagon, GateAt, Vector2.one * 170f, Stone, 20f);
                Stage.Linear(floor.Fill, StoneLit, Stone, 300f);
                Stage.AddStroke(floor.Shape, StoneDark, 3f, -1f);
                portal = stage.Node("Portal", world);
                stage.Box(portal, GateAt, Vector2.zero);
                var core = Blob(portal, "Core", ShapeKind.Circle, Vector2.zero, Vector2.one * 104f, Color.white);
                Stage.Radial(core.Fill, new Color(0.95f, 0.85f, 1f), new Color(0.45f, 0.15f, 0.9f));
                for (var i = 0; i < 3; i++)
                {
                    var arc = stage.Shape("Swirl", portal, ShapeKind.Arc);
                    stage.Box(arc.Rect, Vector2.zero, Vector2.one * (120f - i * 26f));
                    arc.Outline.Start = i * 120f;
                    arc.Outline.End = i * 120f + 200f;
                    arc.Outline.Thickness = 0.12f;
                    arc.Outline.Cap = EndCap.Round;
                    Stage.Solid(arc.Fill, new Color(1f, 0.9f, 1f, 0.75f));
                }

                foreach (var side in new[] { -1f, 1f })
                {
                    var pillar = Blob(world, "Pillar", ShapeKind.RoundedRect, GateAt + new Vector2(side * 80f, 0f), new Vector2(40f, 40f), StoneLit, 6f);
                    Stage.AddStroke(pillar.Shape, StoneDark, 3f, -1f);
                    Stage.AddShadow(pillar.Shape, new Color(0f, 0f, 0f, 0.3f), new Vector2(8f, -10f), 6f);
                }
            }

            /// <summary>A slime of the camp: its shadow, a glossy body with eyes, a white flash for hits and a nameplate with its life.</summary>
            private Foe Slime(int index)
            {
                var root = stage.Node("Slime", world);
                stage.Box(root, FoeStart[index], Vector2.zero);
                Blob(root, "Shadow", ShapeKind.Ellipse, new Vector2(0f, -18f), new Vector2(64f, 22f), new Color(0f, 0f, 0f, 0.3f));
                var body = Blob(root, "Body", ShapeKind.Circle, Vector2.zero, Vector2.one * 58f, Color.white);
                Stage.Radial(body.Fill, SlimeLit, SlimeDark);
                Stage.AddStroke(body.Shape, SlimeEdge, 2.5f, -1f);
                Blob(body.Rect, "Shine", ShapeKind.Ellipse, new Vector2(-12f, 13f), new Vector2(18f, 11f), new Color(1f, 1f, 1f, 0.7f));
                foreach (var side in new[] { -1f, 1f })
                {
                    Blob(body.Rect, "Eye", ShapeKind.Circle, new Vector2(side * 10f - 4f, 2f), Vector2.one * 12f, Color.white);
                    Blob(body.Rect, "Pupil", ShapeKind.Circle, new Vector2(side * 10f - 6f, 1f), Vector2.one * 6f, Ink);
                }

                var flash = Blob(body.Rect, "Flash", ShapeKind.Circle, Vector2.zero, Vector2.one * 58f, Color.white);
                Stage.Alpha(flash.Shape, 0f);
                var plate = stage.Node("Nameplate", root);
                stage.Box(plate, new Vector2(0f, 48f), Vector2.zero);
                var name = stage.Label(plate, "Slime", 13f, Color.white, HorizontalAlignment.Left, stretch: false, face: face);
                stage.Box(name.rectTransform, new Vector2(-14f, 12f), new Vector2(60f, 18f));
                name.WordWrap = false;
                var level = stage.Label(plate, "Lv 3", 12f, new Color(1f, 0.85f, 0.5f), HorizontalAlignment.Right, stretch: false, face: face);
                stage.Box(level.rectTransform, new Vector2(22f, 12f), new Vector2(40f, 18f));
                level.WordWrap = false;
                Blob(plate, "Track", ShapeKind.Capsule, Vector2.zero, new Vector2(64f, 9f), new Color(0.08f, 0.03f, 0.06f, 0.85f));
                var life = Blob(plate, "Life", ShapeKind.Capsule, new Vector2(-30f, 0f), new Vector2(60f, 5f), Blood);
                life.Rect.pivot = new Vector2(0f, 0.5f);
                life.Rect.anchoredPosition = new Vector2(-30f, 0f);
                return new Foe { Root = root, Body = body.Rect, Flash = flash.Shape, Life = life.Rect, Plate = plate };
            }

            /// <summary>The hero seen from above: a crimson cape, steel shoulders, a helmet and the sword in its hand, all turning to face where it goes.</summary>
            private void Knight()
            {
                hero = stage.Node("Hero", world);
                stage.Box(hero, Vector2.zero, Vector2.zero);
                Blob(hero, "Shadow", ShapeKind.Ellipse, new Vector2(4f, -10f), new Vector2(66f, 38f), new Color(0f, 0f, 0f, 0.3f));
                var cloak = Blob(hero, "Cape", ShapeKind.Ellipse, new Vector2(-14f, 0f), new Vector2(48f, 58f), Color.white);
                Stage.Linear(cloak.Fill, CapeLit, Cape, 180f);
                cape = cloak.Rect;
                var shoulders = Blob(hero, "Shoulders", ShapeKind.Capsule, new Vector2(2f, 0f), new Vector2(30f, 54f), Color.white);
                Stage.Linear(shoulders.Fill, SteelLit, Steel, 300f);
                Stage.AddStroke(shoulders.Shape, SteelDark, 2f, -1f);
                var helm = Blob(hero, "Helm", ShapeKind.Circle, new Vector2(4f, 0f), Vector2.one * 30f, Color.white);
                Stage.Radial(helm.Fill, SteelLit, Steel);
                Stage.AddStroke(helm.Shape, SteelDark, 2f, -1f);
                Blob(helm.Rect, "Plume", ShapeKind.Capsule, new Vector2(-6f, 0f), new Vector2(18f, 6f), Cape);
                blade = stage.Node("Arm", hero);
                stage.Box(blade, new Vector2(4f, -20f), Vector2.zero);
                var sword = Blob(blade, "Blade", ShapeKind.Capsule, new Vector2(30f, 0f), new Vector2(50f, 8f), Color.white);
                Stage.Linear(sword.Fill, Color.white, SteelLit, 90f);
                Stage.AddShadow(sword.Shape, new Color(1f, 1f, 1f, 0.45f), Vector2.zero, 6f);
                Blob(blade, "Guard", ShapeKind.Capsule, new Vector2(4f, 0f), new Vector2(6f, 22f), Gold);
                heroFlash = Blob(hero, "Hurt", ShapeKind.Circle, new Vector2(2f, 0f), Vector2.one * 60f, new Color(1f, 0.2f, 0.2f)).Shape;
                Stage.Alpha(heroFlash, 0f);
            }

            /// <summary>The fight's effects: the sword's slash, the whirlwind, the heal, dust and the numbers the hits throw up.</summary>
            private void Effects()
            {
                var cut = stage.Shape("Slash", world, ShapeKind.Arc);
                stage.Box(cut.Rect, Vector2.zero, Vector2.one * 150f);
                cut.Outline.Start = -60f;
                cut.Outline.End = 60f;
                cut.Outline.Thickness = 0.16f;
                cut.Outline.Cap = EndCap.Round;
                Stage.Solid(cut.Fill, new Color(1f, 1f, 1f, 0.95f));
                Stage.AddShadow(cut.Shape, new Color(0.7f, 0.9f, 1f, 0.8f), Vector2.zero, 12f, 2f);
                slash = cut.Rect;

                whirl = stage.Node("Whirlwind", world);
                stage.Box(whirl, Vector2.zero, Vector2.zero);
                for (var i = 0; i < 3; i++)
                {
                    var ring = stage.Shape("Gust", whirl, ShapeKind.Arc);
                    stage.Box(ring.Rect, Vector2.zero, Vector2.one * (WhirlReach * 2f - i * 46f));
                    ring.Outline.Start = i * 120f;
                    ring.Outline.End = i * 120f + 240f;
                    ring.Outline.Thickness = 0.09f;
                    ring.Outline.Cap = EndCap.Round;
                    Stage.Solid(ring.Fill, new Color(0.85f, 0.95f, 1f, 0.8f - i * 0.15f));
                    Stage.AddShadow(ring.Shape, new Color(0.6f, 0.85f, 1f, 0.6f), Vector2.zero, 10f);
                }

                var heal = Blob(world, "Heal", ShapeKind.Ring, Vector2.zero, Vector2.one * 120f, Mint);
                heal.Outline.Thickness = 0.1f;
                Stage.AddShadow(heal.Shape, Theme.Fade(Mint, 0.8f), Vector2.zero, 14f, 2f);
                healRing = heal.Rect;

                for (var i = 0; i < DustCount; i++)
                {
                    var puff = Blob(world, "Dust", ShapeKind.Circle, Vector2.zero, Vector2.one * 18f, new Color(0.85f, 0.75f, 0.55f, 0.6f));
                    dust.Add(puff.Rect);
                }

                numbers.Add((Number("148", Color.white, 30f), "attack", 0));
                numbers.Add((Number("CRIT 296!", Crit, 36f), "critical", 0));
                numbers.Add((Number("-80", Blood, 30f), "struck", -1));
                numbers.Add((Number("196", Color.white, 30f), "whirl", 1));
                numbers.Add((Number("196", Color.white, 30f), "whirl", 2));
                numbers.Add((Number("+260", Mint, 32f), "potion", -1));
            }

            /// <summary>Each slime's coins, above the HUD so they fly into the purse over it.</summary>
            private void Loot()
            {
                for (var f = 0; f < FoeStart.Length; f++)
                for (var i = 0; i < CoinsPerFoe; i++)
                {
                    var coin = Blob(Holder, "Coin", ShapeKind.Circle, Vector2.zero, Vector2.one * 20f, Color.white);
                    Stage.Radial(coin.Fill, new Color(1f, 0.92f, 0.55f), Gold);
                    Stage.AddStroke(coin.Shape, GoldDark, 2f, -1f);
                    coin.Rect.gameObject.SetActive(false);
                    coins.Add((coin.Rect, f));
                }
            }

            private RectTransform Number(string text, Color color, float size)
            {
                var node = stage.Node("Number", world);
                stage.Box(node, Vector2.zero, new Vector2(240f, size * 1.5f));
                var shade = stage.Label(node, text, size, new Color(0.05f, 0.02f, 0.05f, 0.8f), face: face);
                shade.WordWrap = false;
                shade.rectTransform.anchoredPosition = new Vector2(2f, -3f);
                var label = stage.Label(node, text, size, color, face: face);
                label.WordWrap = false;
                node.gameObject.AddComponent<CanvasGroup>();
                node.gameObject.SetActive(false);
                return node;
            }

            #endregion

            #region HUD

            private void Hud()
            {
                Plate(new Vector2(Left + 180f, Top - 40f), new Vector2(360f, 80f));
                Avatar(new Vector2(Left + 40f, Top - 40f), 62f, "M", 35f);
                var badge = Blob(Holder, "Level", ShapeKind.Circle, new Vector2(Left + 64f, Top - 64f), Vector2.one * 26f, Gold);
                Stage.AddStroke(badge.Shape, GoldDark, 2f, -1f);
                levelText = stage.Label(badge.Rect, "12", 13f, Ink, face: face);
                levelText.WordWrap = false;
                Banner();
                Label("Mia", new Vector2(Left + 84f, Top - 20f), 20f, "Ink", display: true);
                Label("Knight", new Vector2(Left + 136f, Top - 21f), 13f, "InkDim");
                health = Bar(new Vector2(Left + 84f, Top - 44f), 260f, 14f, Blood, BloodLit);
                healthText = stage.Label(Holder, string.Empty, 11f, Color.white, HorizontalAlignment.Center, stretch: false, face: face);
                stage.Box(healthText.rectTransform, new Vector2(Left + 214f, Top - 44f), new Vector2(160f, 16f));
                healthText.WordWrap = false;
                mana = Bar(new Vector2(Left + 84f, Top - 62f), 200f, 9f, Sea, SeaLit);

                Plate(TrackerAt, new Vector2(TrackerWidth, 64f));
                var skull = Blob(Holder, "Objective", ShapeKind.Circle, TrackerAt + new Vector2(-TrackerWidth * 0.5f + 28f, 0f), Vector2.one * 30f, Gold);
                Stage.AddStroke(skull.Shape, GoldDark, 2f, -1f);
                Blob(skull.Rect, "Mark", ShapeKind.Rhombus, Vector2.zero, new Vector2(12f, 16f), Ink);
                questTitle = Label("Clear the camp", TrackerAt + new Vector2(-TrackerWidth * 0.5f + 52f, 12f), 18f, "Ink", display: true);
                questCount = Label("0 / 3 slimes", TrackerAt + new Vector2(-TrackerWidth * 0.5f + 52f, -10f), 13f, "InkDim");

                target = stage.Node("Target", Holder);
                stage.Box(target, new Vector2(TrackerAt.x, Top - 96f), Vector2.zero);
                var targetPlate = Blob(target, "Plate", ShapeKind.Capsule, Vector2.zero, new Vector2(230f, 38f), new Color(0.07f, 0.03f, 0.08f, 0.7f));
                Stage.AddStroke(targetPlate.Shape, new Color(1f, 0.4f, 0.4f, 0.5f), 1.5f, -1f);
                var portrait = Blob(target, "Portrait", ShapeKind.Circle, new Vector2(-96f, 0f), Vector2.one * 28f, Color.white);
                Stage.Radial(portrait.Fill, SlimeLit, SlimeDark);
                var targetName = stage.Label(target, "Slime  Lv 3", 13f, Color.white, HorizontalAlignment.Left, stretch: false, face: face);
                stage.Box(targetName.rectTransform, new Vector2(-24f, 8f), new Vector2(120f, 16f));
                targetName.WordWrap = false;
                Blob(target, "Track", ShapeKind.Capsule, new Vector2(10f, -7f), new Vector2(150f, 8f), new Color(0f, 0f, 0f, 0.6f));
                targetLife = Blob(target, "Life", ShapeKind.Capsule, Vector2.zero, new Vector2(150f, 8f), Blood).Rect;
                targetLife.pivot = new Vector2(0f, 0.5f);
                targetLife.anchoredPosition = new Vector2(-65f, -7f);
                target.gameObject.SetActive(false);
                toasts = Add("Toasts", new Vector2(TrackerAt.x, Top - 150f)).GetComponentInChildren<UniMessages>(true);

                var pause = Blob(Holder, "Pause", ShapeKind.RoundedRect, new Vector2(-Left - MapSize - 34f, Top - 22f), Vector2.one * 40f, new Color(0.07f, 0.03f, 0.08f, 0.6f), 12f);
                Stage.AddStroke(pause.Shape, new Color(1f, 0.85f, 0.75f, 0.2f), 1.5f, -1f);
                foreach (var side in new[] { -1f, 1f }) Blob(pause.Rect, "Bar", ShapeKind.RoundedRect, new Vector2(side * 6f, 0f), new Vector2(6f, 18f), Color.white, 2f);
                Plate(new Vector2(Left + 74f, PurseY), new Vector2(148f, 38f));
                var coin = Blob(Holder, "Coin", ShapeKind.Circle, PurseAt, Vector2.one * 24f, Color.white);
                Stage.Radial(coin.Fill, new Color(1f, 0.92f, 0.55f), Gold);
                Stage.AddStroke(coin.Shape, GoldDark, 2f, -1f);
                goldText = Label("1,240", PurseAt + new Vector2(20f, 0f), 17f, "Ink", display: true);

                Label("XP", new Vector2(-XpWidth * 0.5f - 34f, Bottom + 12f), 14f, "InkDim");
                experience = Add("Segmented Progress", new Vector2(-XpWidth * 0.5f, Bottom + 12f), 0f).GetComponent<UniShapeValue>();

                stick = Add("Joystick", StickAt);
                stick.transform.localScale = Vector3.one * StickGrow;
                deadZone = stick.GetComponentInChildren<UniJoystick>(true).DeadZone;
                var axes = stick.GetComponents<UniShapeValue>();
                horizontal = axes[0];
                vertical = axes[1];

                attack = Skill(AttackAt, AttackSize / 64f, Glyph.Sword);
                skills[0] = Skill(WhirlAt, SkillSize / 64f, Glyph.Whirl);
                skills[1] = Skill(DashAt, SkillSize / 64f, Glyph.Dash);
                skills[2] = Skill(PotionAt, SkillSize / 64f, Glyph.Potion);

            }

            /// <summary>
            /// The level-up banner, under the HUD it must not cover: rays of light thinning to nothing outward from behind a
            /// gold ribbon, and what the new level brings on a dark chip below it.
            /// </summary>
            private void Banner()
            {
                banner = stage.Node("Level Up", Holder);
                stage.Box(banner, BannerAt, Vector2.zero);
                var glow = Blob(banner, "Glow", ShapeKind.Circle, Vector2.zero, Vector2.one * 360f, Color.white);
                Stage.Radial(glow.Fill, new Color(1f, 0.9f, 0.55f, 0.55f), new Color(1f, 0.85f, 0.4f, 0f));
                spokes = stage.Node("Rays", banner);
                stage.Box(spokes, Vector2.zero, Vector2.zero);
                for (var i = 0; i < RayCount; i++)
                {
                    var ray = Blob(spokes, "Ray", ShapeKind.Triangle, Vector2.zero, new Vector2(RayWidth, RayLength), Color.white);
                    Stage.Linear(ray.Fill, new Color(1f, 0.95f, 0.7f, 0.65f), new Color(1f, 0.9f, 0.55f, 0f), 270f);
                    ray.Rect.pivot = new Vector2(0.5f, 1f);
                    ray.Rect.anchoredPosition = Vector2.zero;
                    ray.Rect.localRotation = Quaternion.Euler(0f, 0f, i * 360f / RayCount);
                }

                var ribbon = Blob(banner, "Ribbon", ShapeKind.RoundedRect, Vector2.zero, new Vector2(330f, 72f), Color.white, 20f);
                Stage.Linear(ribbon.Fill, new Color(1f, 0.86f, 0.38f), new Color(0.95f, 0.55f, 0.1f), 270f);
                Stage.AddBevel(ribbon.Shape, 90f, 3f, 0.5f);
                Stage.AddStroke(ribbon.Shape, GoldDark, 3f, 1f);
                Stage.AddShadow(ribbon.Shape, new Color(0.45f, 0.2f, 0f, 0.9f), new Vector2(0f, -7f), 0f);
                var up = stage.Label(banner, "LEVEL UP!", 40f, Color.white, face: face);
                up.WordWrap = false;
                up.Styles.Add(Style.WholeText(new StrokeModifier { Paint = PaintRef.Solid(new Color(0.45f, 0.2f, 0f)), Width = UnitValue.Em(0.08f), Align = 1f }));
                var chip = Blob(banner, "Chip", ShapeKind.Capsule, new Vector2(0f, -54f), new Vector2(250f, 30f), new Color(0.07f, 0.03f, 0.08f, 0.78f));
                Stage.AddStroke(chip.Shape, new Color(1f, 0.85f, 0.45f, 0.5f), 1.5f, -1f);
                var next = stage.Label(chip.Rect, "Level 13 · Rally unlocked", 15f, new Color(1f, 0.93f, 0.75f), face: face);
                next.WordWrap = false;
                banner.gameObject.SetActive(false);
            }

            private enum Glyph
            {
                Sword,
                Whirl,
                Dash,
                Potion
            }

            /// <summary>A skill on a kit cooldown, grown to the HUD's size, its icon drawn in place of the kit's.</summary>
            private UniShapeValue Skill(Vector2 at, float grow, Glyph glyph)
            {
                var skill = Add("Cooldown", at);
                skill.transform.localScale = Vector3.one * grow;
                var icon = skill.transform.Find("Icon");
                var shape = icon.GetComponent<UniShape>();
                Stage.Alpha(shape, 0f);
                switch (glyph)
                {
                    case Glyph.Sword:
                        var arm = stage.Node("Sword", icon);
                        stage.Box(arm, Vector2.zero, Vector2.zero);
                        arm.localRotation = Quaternion.Euler(0f, 0f, 45f);
                        Blob(arm, "Blade", ShapeKind.Capsule, new Vector2(0f, 4f), new Vector2(7f, 30f), SteelLit);
                        Blob(arm, "Guard", ShapeKind.Capsule, new Vector2(0f, -10f), new Vector2(18f, 5f), Gold);
                        Blob(arm, "Grip", ShapeKind.Capsule, new Vector2(0f, -15f), new Vector2(5f, 9f), Wood);
                        break;
                    case Glyph.Whirl:
                        for (var i = 0; i < 2; i++)
                        {
                            var gust = stage.Shape("Gust", icon, ShapeKind.Arc);
                            stage.Box(gust.Rect, Vector2.zero, Vector2.one * (30f - i * 12f));
                            gust.Outline.Start = i * 180f;
                            gust.Outline.End = i * 180f + 250f;
                            gust.Outline.Thickness = 0.22f;
                            gust.Outline.Cap = EndCap.Round;
                            Stage.Solid(gust.Fill, new Color(0.8f, 0.95f, 1f));
                        }
                        break;
                    case Glyph.Dash:
                        for (var i = 0; i < 2; i++)
                        {
                            var chevron = Blob(icon, "Chevron", ShapeKind.Triangle, new Vector2(-6f + i * 12f, 0f), new Vector2(18f, 14f), new Color(1f, 0.95f, 0.7f), 2f);
                            chevron.Rect.localRotation = Quaternion.Euler(0f, 0f, -90f);
                        }
                        break;
                    default:
                        Blob(icon, "Neck", ShapeKind.RoundedRect, new Vector2(0f, 9f), new Vector2(8f, 9f), new Color(0.85f, 0.9f, 1f), 2f);
                        var bowl = Blob(icon, "Bowl", ShapeKind.Circle, new Vector2(0f, -3f), Vector2.one * 20f, Mint);
                        Stage.AddStroke(bowl.Shape, new Color(0.85f, 0.9f, 1f), 2f, 1f);
                        break;
                }

                return skill.GetComponent<UniShapeValue>();
            }

            /// <summary>A HUD card: dusky glass under a group of the HUD's parts.</summary>
            private void Plate(Vector2 at, Vector2 size)
            {
                var plate = Blob(Holder, "Plate", ShapeKind.RoundedRect, at, size, new Color(0.07f, 0.03f, 0.08f, 0.62f), 18f);
                Stage.AddStroke(plate.Shape, new Color(1f, 0.85f, 0.75f, 0.18f), 1.5f, -1f);
                Stage.AddShadow(plate.Shape, new Color(0f, 0f, 0f, 0.3f), new Vector2(0f, -4f), 12f);
            }

            /// <summary>A bar of the hero's: a dark well and a fill hung from its left end, returned to be sized.</summary>
            private RectTransform Bar(Vector2 left, float width, float height, Color color, Color lit)
            {
                var well = Blob(Holder, "Well", ShapeKind.Capsule, left + new Vector2(width * 0.5f, 0f), new Vector2(width, height), new Color(0f, 0f, 0f, 0.55f));
                Stage.AddInnerShadow(well.Shape, new Color(0f, 0f, 0f, 0.6f), new Vector2(0f, -2f), 3f);
                var fill = Blob(Holder, "Fill", ShapeKind.Capsule, left, new Vector2(width, height - 4f), Color.white);
                Stage.Linear(fill.Fill, lit, color, 270f);
                fill.Rect.pivot = new Vector2(0f, 0.5f);
                fill.Rect.anchoredPosition = left + new Vector2(2f, 0f);
                return fill.Rect;
            }

            #endregion

            #region Map

            /// <summary>
            /// The minimap's world: the ground, the road, the trees, the camp, the slimes and the gate as blocks a tenth the
            /// scale of the view's world, the hero among them, and the camera looking straight down; only that camera sees
            /// its layer. North stays up, as the view does.
            /// </summary>
            private void Map()
            {
                var map = Add("Minimap", new Vector2(-Left - MapSize * 0.5f, Top - MapSize * 0.5f));
                ((RectTransform)map.transform).sizeDelta = Vector2.one * MapSize;
                var root = new GameObject("Game World") { layer = WorldLayer }.transform;
                root.SetParent(map.transform, false);
                mapWorld = root;
                Block(root, "Ground", new Vector3(0f, -0.5f, 0f), new Vector3(400f, 1f, 400f), MapGrass);
                for (var i = 0; i < Road.Length - 1; i++)
                {
                    var from = Road[i] * MapScale;
                    var to = Road[i + 1] * MapScale;
                    var road = Block(root, "Road", Flat((from + to) * 0.5f, 0f), new Vector3(Vector2.Distance(from, to) + 3f, 0.2f, 3f), MapDirt);
                    road.localRotation = Quaternion.Euler(0f, -Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg, 0f);
                }

                foreach (var (at, size) in Trees) Block(root, "Tree", Flat(at * MapScale, 1f), new Vector3(size * MapScale, 2f, size * MapScale), MapLeaf);
                foreach (var (at, _) in Tents) Block(root, "Tent", Flat(at * MapScale, 1f), new Vector3(10f, 2f, 8f), MapTent);
                Block(root, "Fire", Flat(FireAt * MapScale, 1f), new Vector3(3f, 2f, 3f), MapFire);
                var gate = Block(root, "Gate", Flat(GateAt * MapScale, 1f), new Vector3(14f, 2f, 14f), MapGate);
                for (var i = 0; i < FoeStart.Length; i++) mapFoes[i] = Block(root, "Slime", Flat(FoeStart[i] * MapScale, 1f), new Vector3(4f, 2f, 4f), MapFoe);
                mapHero = new GameObject("Hero") { layer = WorldLayer }.transform;
                mapHero.SetParent(root, false);
                var eye = new GameObject("Map Camera") { layer = WorldLayer };
                eye.transform.SetParent(root, false);
                var viewer = eye.AddComponent<Camera>();
                viewer.cullingMask = 1 << WorldLayer;
                viewer.clearFlags = CameraClearFlags.SolidColor;
                viewer.backgroundColor = MapGrass;
                viewer.nearClipPlane = 0.1f;
                viewer.farClipPlane = 400f;
                var minimap = map.GetComponent<UniMinimap>();
                minimap.Follow = mapHero;
                minimap.Range = MapRange;
                minimap.RotateWithFollow = false;
                minimap.Viewer = viewer;
                var marker = Add("World Marker", Vector2.zero).GetComponent<UniWorldMarker>();
                marker.transform.SetParent(map.transform.Find("Markers"), false);
                marker.Target = gate;
                marker.Viewer = viewer;
                marker.Bounds = (RectTransform)map.transform;
                marker.Area = MarkerArea.Ellipse;
                marker.Edge = MarkerEdge.Clamp;
            }

            private static Vector3 Flat(Vector2 at, float height) => new(at.x, height, at.y);

            private static Transform Block(Transform root, string name, Vector3 at, Vector3 size, Color color)
            {
                var block = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { layer = WorldLayer };
                block.transform.SetParent(root, false);
                block.transform.localPosition = at;
                block.transform.localScale = size;
                block.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                block.GetComponent<MeshRenderer>().sharedMaterial = Paint(color);
                return block.transform;
            }

            private static readonly Dictionary<Color, Material> paints = new();

            /// <summary>An unlit material of <paramref name="color"/>, shared by every block of that color.</summary>
            private static Material Paint(Color color)
            {
                if (paints.TryGetValue(color, out var paint) && paint) return paint;
                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? throw new System.InvalidOperationException("[Promo] The URP unlit shader is missing.");
                paint = new Material(shader) { hideFlags = HideFlags.DontSave };
                paint.SetColor("_BaseColor", color);
                paints[color] = paint;
                return paint;
            }

            #endregion

            #region Pose

            /// <summary>The run carries on through the hand-off: the part leaves while the hero still runs, as the next slide pushes in.</summary>
            public override float ActionSeconds => Pointer.Timeline.Span("run").x + RunShown;

            public override bool Navigates => true;

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var t = Mathf.Max(0f, played);
                mapWorld.position = WorldAt;
                mapWorld.rotation = Quaternion.identity;
                mapWorld.localScale = Vector3.one;
                mapWorld.localScale = Vector3.one / mapWorld.lossyScale.x;

                var (place, facing, moving) = Walked(t);
                var struck = t - ClickAt("attack") - Swing;
                var crit = t - ClickAt("critical") - Swing;
                var spun = t - ClickAt("whirl") - Swing;
                var drank = t - ClickAt("potion");
                var aimed = FoeStart[0] - place;
                var fighting = t >= ClickAt("attack") - 0.2f && t < ClickAt("whirl") + 0.5f;
                if (fighting) facing = Mathf.Atan2(aimed.y, aimed.x) * Mathf.Rad2Deg;

                world.anchoredPosition = HeroScreen - place;
                hero.anchoredPosition = place + new Vector2(0f, moving * Mathf.Abs(Mathf.Sin(t * 14f)) * 3f);
                hero.localRotation = Quaternion.Euler(0f, 0f, facing);
                cape.localScale = new Vector3(1f + moving * 0.15f * Mathf.Sin(t * 14f), 1f, 1f);
                var swing = Mathf.Max(Window(struck, 0.2f), Window(crit, 0.2f));
                var spin = Window(spun, WhirlFor);
                blade.localRotation = Quaternion.Euler(0f, 0f, spin > 0f ? spin * 720f : Mathf.Lerp(-70f, 70f, swing) * (swing > 0f ? 1f : 0f) - 20f * (1f - swing));
                Stage.Alpha(heroFlash, Mathf.Clamp01(1f - (t - ClickAt("critical") - StruckLead) / 0.25f) * (t >= ClickAt("critical") + StruckLead ? 0.6f : 0f));

                PoseSlash(place, facing, struck, crit);
                PoseFoes(t, struck, crit, spun);
                PoseCoins(t, crit, spun);
                PoseNumbers(t, place, struck, crit, spun, drank);
                PoseHud(t, place, crit, spun, drank);
                PoseDust(t);
                PoseWorld(t);
                mapHero.localPosition = Flat(place * MapScale, 1f);
            }

            /// <summary>How far through a window of <paramref name="length"/> seconds a moment <paramref name="since"/> into it is; 0 outside.</summary>
            private static float Window(float since, float length) => since < 0f || since > length ? 0f : since / length;

            private void PoseSlash(Vector2 place, float facing, float struck, float crit)
            {
                var since = struck >= 0f && struck < SlashFor ? struck : crit >= 0f && crit < SlashFor ? crit : -1f;
                var shown = since >= 0f;
                if (slash.gameObject.activeSelf != shown) slash.gameObject.SetActive(shown);
                if (!shown) return;
                slash.anchoredPosition = place + new Vector2(Mathf.Cos(facing * Mathf.Deg2Rad), Mathf.Sin(facing * Mathf.Deg2Rad)) * 30f;
                slash.localRotation = Quaternion.Euler(0f, 0f, facing - 90f);
                slash.localScale = Vector3.one * (0.8f + 0.4f * since / SlashFor);
                Stage.Alpha(slash.GetComponent<UniShape>(), 1f - since / SlashFor);
            }

            /// <summary>The slimes: bouncing in place, flashing and knocked back as they are hit, bursting when their life runs out.</summary>
            private void PoseFoes(float t, float struck, float crit, float spun)
            {
                for (var i = 0; i < foes.Length; i++)
                {
                    var foe = foes[i];
                    var hit = i == 0 ? Mathf.Max(Flash(struck), Flash(crit)) : Flash(spun);
                    Stage.Alpha(foe.Flash, hit * 0.8f);
                    var dies = i == 0 ? crit : spun;
                    var gone = dies < 0f ? 0f : Mathf.Clamp01(dies / BurstFor);
                    var bounce = Mathf.Abs(Mathf.Sin(t * 3.2f + i * 1.3f));
                    foe.Body.localScale = new Vector3(1f + 0.08f * (1f - bounce) + gone * 0.4f, (1f - 0.08f * (1f - bounce)) * (1f - gone), 1f);
                    foe.Body.anchoredPosition = new Vector2(0f, bounce * 6f);
                    var lunge = i == 1 ? Window(t - ClickAt("critical") - StruckLead + 0.12f, 0.24f) : 0f;
                    var toward = (heroAt - FoeStart[i]).normalized;
                    var knock = (i == 0 ? Mathf.Max(Window(struck, 0.25f), 0f) : 0f) * 16f;
                    foe.Root.anchoredPosition = FoeStart[i] + toward * (Mathf.Sin(lunge * Mathf.PI) * 40f - knock);
                    var life = i == 0 ? (crit >= 0f ? 0f : struck >= 0f ? 0.5f : 1f) : (spun >= 0f ? 0f : 1f);
                    foe.Life.sizeDelta = new Vector2(60f * life, foe.Life.sizeDelta.y);
                    var alive = gone < 1f;
                    if (foe.Root.gameObject.activeSelf != alive) foe.Root.gameObject.SetActive(alive);
                    foe.Plate.gameObject.SetActive(dies < 0f);
                    if (mapFoes[i].gameObject.activeSelf != alive) mapFoes[i].gameObject.SetActive(alive);
                }
            }

            private static float Flash(float since) => since < 0f ? 0f : Mathf.Clamp01(1f - since / 0.22f);

            /// <summary>Each slain slime's coins: spilled round where it fell, then flown up into the purse one after another.</summary>
            private void PoseCoins(float t, float crit, float spun)
            {
                var purse = PurseAt;
                for (var k = 0; k < coins.Count; k++)
                {
                    var (node, foe) = coins[k];
                    var since = (foe == 0 ? crit : spun) - BurstFor * 0.5f;
                    var index = k % CoinsPerFoe;
                    var shown = since >= 0f && since < CoinSpill + CoinFlight + index * CoinStagger;
                    if (node.gameObject.activeSelf != shown) node.gameObject.SetActive(shown);
                    if (!shown) continue;
                    var angle = (index * 2.1f + foe) * 1.3f;
                    var ground = (FoeStart[foe] + world.anchoredPosition) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 34f;
                    var spill = Mathf.Clamp01(since / CoinSpill);
                    var hop = Mathf.Sin(spill * Mathf.PI) * 30f;
                    var at = Vector2.Lerp(FoeStart[foe] + world.anchoredPosition, ground, spill) + new Vector2(0f, hop);
                    var fly = Mathf.Clamp01((since - CoinSpill - index * CoinStagger) / CoinFlight);
                    var e = Ease.EmphasizedIn.Evaluate(fly);
                    node.anchoredPosition = Vector2.Lerp(at, purse, e) + new Vector2(0f, Mathf.Sin(fly * Mathf.PI) * 40f * (1f - e));
                    node.localScale = Vector3.one * Mathf.Lerp(1f, 0.75f, e);
                }
            }

            private void PoseNumbers(float t, Vector2 place, float struck, float crit, float spun, float drank)
            {
                foreach (var (node, id, foe) in numbers)
                {
                    var since = id switch
                    {
                        "attack" => struck,
                        "critical" => crit,
                        "struck" => t - ClickAt("critical") - StruckLead,
                        "whirl" => spun,
                        _ => drank
                    };
                    var home = foe >= 0 ? FoeStart[foe] + new Vector2(0f, 70f) : place + new Vector2(0f, 70f);
                    var shown = since >= 0f && since < FloatFor;
                    if (node.gameObject.activeSelf != shown) node.gameObject.SetActive(shown);
                    if (!shown) continue;
                    var f = since / FloatFor;
                    node.anchoredPosition = home + new Vector2(foe == 2 ? 30f : 0f, FloatRise * Ease.EmphasizedOut.Evaluate(f));
                    node.localScale = Vector3.one * Pop.Evaluate(Mathf.Clamp01(since / 0.25f)) * (id == "critical" ? 1.2f : 1f);
                    node.GetComponent<CanvasGroup>().alpha = Mathf.Clamp01(since / 0.06f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 1f, f)));
                }
            }

            /// <summary>The HUD: the hero's bars, the purse counting the coins in, the quest's progress and next goal, the target, the cooldowns, the level and its banner.</summary>
            private void PoseHud(float t, Vector2 place, float crit, float spun, float drank)
            {
                var hurt = t >= ClickAt("critical") + StruckLead;
                var healed = Ease.Emphasized.Window(t, ClickAt("potion"), HealFor);
                var life = Mathf.Lerp(hurt ? HealthHurt : HealthStart, 1f, healed);
                health.sizeDelta = new Vector2((260f - 4f) * life, health.sizeDelta.y);
                var hp = $"{Mathf.RoundToInt(life * MaxHealth)} / {MaxHealth}";
                if (healthText.Text != hp) healthText.Text = hp;
                var spent = (t >= ClickAt("whirl") ? 0.3f : 0f) + (t >= ClickAt("potion") ? -0.1f : 0f);
                mana.sizeDelta = new Vector2((200f - 4f) * (0.75f - spent), mana.sizeDelta.y);

                var landed = 0;
                for (var k = 0; k < coins.Count; k++)
                {
                    var (_, foe) = coins[k];
                    var since = (foe == 0 ? crit : spun) - BurstFor * 0.5f - CoinSpill - (k % CoinsPerFoe) * CoinStagger;
                    if (since >= CoinFlight) landed++;
                }

                var gold = (1240 + landed * 15).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
                if (goldText.Text != gold) goldText.Text = gold;

                var slain = (crit >= BurstFor ? 1 : 0) + (spun >= BurstFor ? 2 : 0);
                var done = slain == 3 && spun >= QuestTurn;
                var title = done ? "Enter the gate" : "Clear the camp";
                if (questTitle.Text != title) questTitle.Text = title;
                var count = done ? $"{Mathf.RoundToInt(Vector2.Distance(place, GateAt) / 10f)} m away" : $"{slain} / 3 slimes";
                if (questCount.Text != count) questCount.Text = count;
                var targeting = t >= ClickAt("attack") - 0.15f && crit < BurstFor;
                if (target.gameObject.activeSelf != targeting) target.gameObject.SetActive(targeting);
                targetLife.sizeDelta = new Vector2(150f * (crit >= 0f ? 0f : t >= ClickAt("attack") + Swing ? 0.5f : 1f), targetLife.sizeDelta.y);

                attack.TrackTo(Cooldown(t, Mathf.Max(ClickAt("attack") <= t ? ClickAt("attack") : -9f, ClickAt("critical") <= t ? ClickAt("critical") : -9f), 0.45f));
                skills[0].TrackTo(Cooldown(t, ClickAt("whirl"), 2.6f));
                skills[1].TrackTo(1f);
                skills[2].TrackTo(Cooldown(t, ClickAt("potion"), 3f));

                var leveled = spun >= LevelTurn;
                experience.Value = leveled ? 0.12f : spun >= QuestTurn ? 1f : crit >= BurstFor ? 0.72f : 0.6f;
                var level = leveled ? "13" : "12";
                if (levelText.Text != level) levelText.Text = level;
                var shown = spun - LevelTurn;
                Stage.Appear(banner, shown >= 0f && shown < BannerFor ? shown : -1f, Pop, 0.35f);
                if (shown >= 0f && shown < BannerFor)
                {
                    banner.GetComponent<CanvasGroup>().alpha *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BannerFor - 0.3f, BannerFor, shown));
                    spokes.localRotation = Quaternion.Euler(0f, 0f, shown * 30f);
                }

                var complete = done;
                if (complete && !toasted)
                    toasts.Show(new MessageRequest { Template = "Success", Title = "Quest complete", Text = "Clear the camp · +120 XP", Timeout = ToastRead });
                if (!complete && toasted) toasts.Clear();
                toasted = complete;

                if (Held(t) is { } push)
                {
                    horizontal.TrackTo(0.5f + 0.5f * push.x);
                    vertical.TrackTo(0.5f + 0.5f * push.y);
                }
                else
                {
                    if (!Mathf.Approximately(horizontal.Normalized, 0.5f)) horizontal.Normalized = 0.5f;
                    if (!Mathf.Approximately(vertical.Normalized, 0.5f)) vertical.Normalized = 0.5f;
                }

                PoseHeal(place, drank);
            }

            private static float Cooldown(float t, float pressed, float seconds) => t < pressed ? 1f : Mathf.Clamp01((t - pressed) / seconds);

            private void PoseHeal(Vector2 place, float drank)
            {
                var ring = drank / 0.7f;
                var shown = ring >= 0f && ring < 1f;
                if (healRing.gameObject.activeSelf != shown) healRing.gameObject.SetActive(shown);
                if (!shown) return;
                healRing.anchoredPosition = place;
                healRing.localScale = Vector3.one * (0.4f + 1.1f * Ease.EmphasizedOut.Evaluate(ring));
                Stage.Alpha(healRing.GetComponent<UniShape>(), 1f - ring);
            }

            /// <summary>Dust kicked up behind the running hero: a puff every few strides, growing and fading where it was left.</summary>
            private void PoseDust(float t)
            {
                for (var i = 0; i < dust.Count; i++)
                {
                    var born = DustAt(i);
                    var age = t - born;
                    var shown = !float.IsNaN(born) && age >= 0f && age < DustLife;
                    if (dust[i].gameObject.activeSelf != shown) dust[i].gameObject.SetActive(shown);
                    if (!shown) continue;
                    dust[i].anchoredPosition = Walked(born).place + new Vector2(-10f, -12f);
                    dust[i].localScale = Vector3.one * (0.6f + age / DustLife);
                    Stage.Alpha(dust[i].GetComponent<UniShape>(), 0.6f * (1f - age / DustLife));
                }
            }

            /// <summary>When puff <paramref name="index"/> is kicked up: puffs share out the two runs, one every <see cref="DustEvery"/> seconds of each.</summary>
            private float DustAt(int index)
            {
                var half = dust.Count / 2;
                var span = Pointer.Timeline.Span(index < half ? "approach" : "run");
                var born = span.x + 0.1f + (index % half) * DustEvery;
                return born < span.y + 0.2f ? born : float.NaN;
            }

            /// <summary>The camp's fire flickering and the gate's portal turning.</summary>
            private void PoseWorld(float t)
            {
                for (var i = 0; i < flames.Length; i++)
                    flames[i].localScale = Vector3.one * (0.85f + 0.2f * Mathf.PerlinNoise(t * 6f + i * 3.1f, i));
                portal.localRotation = Quaternion.Euler(0f, 0f, t * -90f);
                whirl.anchoredPosition = heroAt;
                var spinning = Window(t - ClickAt("whirl") - Swing, WhirlFor);
                whirl.gameObject.SetActive(spinning > 0f);
                whirl.localRotation = Quaternion.Euler(0f, 0f, spinning * 720f);
                whirl.localScale = Vector3.one * (0.6f + 0.4f * Mathf.Sin(spinning * Mathf.PI));
            }

            private Vector2 heroAt;

            /// <summary>
            /// Where the hero stands at <paramref name="played"/>, which way it faces and how much it is running: walked from
            /// the camp by what the game reads from the stick, stepped in short slices so the path is the same however the
            /// film is sought.
            /// </summary>
            private (Vector2 place, float facing, float moving) Walked(float played)
            {
                var place = HeroStart;
                var facing = 0f;
                var start = Pointer.Timeline.Span("approach").x;
                for (var t = start; t < played; t += WalkStep)
                {
                    var input = Input(t);
                    if (input == Vector2.zero) continue;
                    place += input * (HeroSpeed * Mathf.Min(WalkStep, played - t));
                    facing = Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg;
                }

                heroAt = place;
                return (place, facing, Input(played).magnitude);
            }

            /// <summary>The stick's push at <paramref name="played"/> while either drag holds it, held to its reach; null while nothing holds it.</summary>
            private Vector2? Held(float played)
            {
                foreach (var id in new[] { "approach", "run" })
                {
                    var span = Pointer.Timeline.Span(id);
                    if (played < span.x || played > span.y) continue;
                    var held = (Native(Pointer.Timeline.Sample(played).Position) - StickAt) / StickGrow;
                    return Vector2.ClampMagnitude(held / StickRange, 1f);
                }

                return null;
            }

            /// <summary>
            /// What the game reads from the stick at <paramref name="played"/>, as <see cref="UniJoystick.Value"/> reads it:
            /// the held push with the dead zone taken out, and nothing the moment it is let go, however the knob travels back.
            /// </summary>
            private Vector2 Input(float played)
            {
                if (Held(played) is not { } push) return Vector2.zero;
                var length = push.magnitude;
                return length <= deadZone ? Vector2.zero : push / length * Mathf.InverseLerp(deadZone, 1f, length);
            }

            #endregion

            private static readonly Ease Pop = Ease.Of(EasingType.BackOut);

            private static readonly Vector2 Board = new(1720f, 756f);
            private const float Scale = 1.8f;
            private const float Pace = 1.25f;
            private const float Left = -1720f / Scale * 0.5f + 22f;
            private const float Top = 756f / Scale * 0.5f - 22f;
            private const float Bottom = -Top;

            private static readonly Vector2 HeroScreen = new(-60f, -30f);
            private static readonly Vector2 HeroStart = new(-40f, -10f);
            private const float HeroSpeed = 250f;
            private const float WalkStep = 1f / 120f;
            private static readonly Vector2[] FoeStart = { new(250f, 20f), new(330f, -70f), new(230f, -130f) };
            private static readonly Vector2 FireAt = new(-170f, 110f);
            private static readonly Vector2 GateAt = new(860f, 430f);

            private static readonly Vector2 StickAt = new(Left + 96f, Bottom + 96f);
            private const float StickGrow = 1.2f;
            private const float StickRange = 42f;
            private static readonly Vector2 Approach = new(50f, 0f);
            private const float ApproachHold = 0.55f;
            private static readonly Vector2 Run = new(38f, 32f);
            private const float RunHold = 2.4f;
            private const float RunShown = 1.15f;

            private static readonly Vector2 AttackAt = new(-Left - 74f, Bottom + 74f);
            private const float AttackSize = 112f;
            private static readonly Vector2 WhirlAt = new(-Left - 196f, Bottom + 46f);
            private static readonly Vector2 DashAt = new(-Left - 178f, Bottom + 150f);
            private static readonly Vector2 PotionAt = new(-Left - 84f, Bottom + 180f);
            private static readonly Vector2 TrackerAt = new(60f, Top - 34f);
            private const float TrackerWidth = 260f;
            private static readonly Vector2 BannerAt = new(0f, 76f);
            private const int RayCount = 16;
            private const float RayWidth = 44f;
            private const float RayLength = 210f;
            private const float SkillSize = 76f;
            private const float MapSize = 150f;
            private const float PurseY = Top - 106f;
            private static readonly Vector2 PurseAt = new(Left + 22f, PurseY);
            private const float XpWidth = 240f;

            private const float Swing = 0.08f;
            private const float SlashFor = 0.22f;
            private const float StruckLead = 0.3f;
            private const float WhirlFor = 0.5f;
            private const float WhirlReach = 170f;
            private const float BurstFor = 0.28f;
            private const float CoinSpill = 0.35f;
            private const float CoinFlight = 0.55f;
            private const float CoinStagger = 0.06f;
            private const int CoinsPerFoe = 3;
            private const float FloatFor = 0.9f;
            private const float FloatRise = 46f;
            private const float QuestTurn = 0.5f;
            private const float LevelTurn = 1.75f;
            private const float BannerFor = 1.3f;
            private const float LevelRead = 2.1f;
            private const float HealFor = 0.7f;
            private const float HealthStart = 0.82f;
            private const float HealthHurt = 0.74f;
            private const int MaxHealth = 1000;
            private const float ToastRead = 1f;
            private const int DustCount = 12;
            private const float DustEvery = 0.12f;
            private const float DustLife = 0.45f;
            private const float RoadWidth = 64f;

            /// <summary>The layer only the map's camera draws, and where its world stands, far from anything the film shows.</summary>
            private const int WorldLayer = 31;

            private static readonly Vector3 WorldAt = new(0f, -5000f, 0f);
            private const float MapScale = 0.1f;
            private const float MapRange = 32f;

            private static readonly Vector2[] Road =
            {
                new(-420f, -60f), new(-40f, -10f), new(300f, -20f), new(540f, 110f), new(720f, 300f), new(860f, 430f)
            };

            private static readonly (Vector2 at, Vector2 size)[] Patches =
            {
                (new(-300f, 180f), new(420f, 240f)), (new(420f, -180f), new(520f, 260f)), (new(120f, 260f), new(380f, 200f)),
                (new(760f, 120f), new(420f, 300f)), (new(-520f, -200f), new(380f, 220f))
            };

            private static readonly Vector2[] Tufts =
            {
                new(-360f, -150f), new(-250f, -170f), new(-120f, -140f), new(60f, 160f), new(140f, 120f), new(380f, 160f), new(460f, -110f),
                new(560f, -40f), new(640f, 40f), new(110f, -200f), new(-60f, 200f), new(300f, 260f), new(700f, 180f), new(-420f, 80f)
            };

            private static readonly Vector2[] Flowers =
            {
                new(-330f, -120f), new(-310f, -135f), new(90f, 140f), new(105f, 150f), new(420f, 120f), new(520f, -150f), new(535f, -160f),
                new(20f, -170f), new(650f, 0f), new(-90f, 170f), new(260f, 190f), new(-200f, -190f)
            };

            private static readonly Color[] FlowerHues =
            {
                new Color32(0xFF, 0xE0, 0x6A, 0xFF), new Color32(0xFF, 0x8F, 0xB8, 0xFF), new Color32(0xFF, 0xFF, 0xFF, 0xFF), new Color32(0xB8, 0x9C, 0xFF, 0xFF)
            };

            private static readonly (Vector2 at, Vector2 size)[] Rocks =
            {
                (new(150f, -170f), new(44f, 34f)), (new(470f, 40f), new(36f, 30f)), (new(-280f, -60f), new(30f, 24f)), (new(620f, 250f), new(46f, 36f))
            };

            private static readonly (Vector2 at, float size)[] Trees =
            {
                (new(-460f, 120f), 130f), (new(-380f, 230f), 110f), (new(-500f, -100f), 120f), (new(-230f, -210f), 100f), (new(60f, 230f), 120f),
                (new(200f, 200f), 96f), (new(520f, 230f), 110f), (new(600f, -200f), 130f), (new(420f, -260f), 104f), (new(-40f, -240f), 116f),
                (new(760f, -60f), 120f), (new(980f, 240f), 130f), (new(700f, 470f), 110f)
            };

            private static readonly (Vector2 at, float turn)[] Tents = { (new(-300f, 60f), 10f), (new(-220f, 210f), -14f) };

            private static readonly Color Grass = new Color32(0x5C, 0x9B, 0x48, 0xFF);
            private static readonly Color GrassLight = new Color32(0x6C, 0xAE, 0x52, 0xFF);
            private static readonly Color GrassDark = new Color32(0x3F, 0x7A, 0x36, 0xFF);
            private static readonly Color Dirt = new Color32(0xC9, 0xA4, 0x6A, 0xFF);
            private static readonly Color DirtEdge = new Color32(0x9A, 0x7A, 0x48, 0xFF);
            private static readonly Color DirtLit = new Color32(0xD9, 0xBA, 0x84, 0xFF);
            private static readonly Color Leaf = new Color32(0x2F, 0x66, 0x30, 0xFF);
            private static readonly Color LeafLit = new Color32(0x4E, 0x95, 0x42, 0xFF);
            private static readonly Color LeafDark = new Color32(0x1E, 0x45, 0x22, 0xFF);
            private static readonly Color Stone = new Color32(0x8B, 0x8F, 0x96, 0xFF);
            private static readonly Color StoneLit = new Color32(0xB9, 0xBD, 0xC4, 0xFF);
            private static readonly Color StoneDark = new Color32(0x55, 0x58, 0x5E, 0xFF);
            private static readonly Color Canvas = new Color32(0xD9, 0xB6, 0x7E, 0xFF);
            private static readonly Color CanvasLit = new Color32(0xF2, 0xD8, 0xA4, 0xFF);
            private static readonly Color CanvasDark = new Color32(0x8A, 0x63, 0x2E, 0xFF);
            private static readonly Color Wood = new Color32(0x8A, 0x55, 0x2A, 0xFF);
            private static readonly Color WoodDark = new Color32(0x5A, 0x34, 0x14, 0xFF);
            private static readonly Color SlimeLit = new Color32(0xB8, 0xF5, 0x7E, 0xFF);
            private static readonly Color SlimeDark = new Color32(0x3B, 0xB0, 0x4A, 0xFF);
            private static readonly Color SlimeEdge = new Color32(0x1F, 0x6E, 0x2C, 0xFF);
            private static readonly Color Steel = new Color32(0x8C, 0x97, 0xAD, 0xFF);
            private static readonly Color SteelLit = new Color32(0xE4, 0xE9, 0xF2, 0xFF);
            private static readonly Color SteelDark = new Color32(0x4A, 0x52, 0x64, 0xFF);
            private static readonly Color Cape = new Color32(0xB8, 0x1E, 0x3E, 0xFF);
            private static readonly Color CapeLit = new Color32(0xF0, 0x46, 0x62, 0xFF);
            private static readonly Color Gold = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
            private static readonly Color GoldDark = new Color32(0x9A, 0x5E, 0x00, 0xFF);
            private static readonly Color Crit = new Color32(0xFF, 0xD2, 0x3C, 0xFF);
            private static readonly Color Blood = new Color32(0xE0, 0x34, 0x4A, 0xFF);
            private static readonly Color BloodLit = new Color32(0xFF, 0x7A, 0x7A, 0xFF);
            private static readonly Color Sea = new Color32(0x2A, 0x7B, 0xE0, 0xFF);
            private static readonly Color SeaLit = new Color32(0x7F, 0xC4, 0xFF, 0xFF);
            private static readonly Color Mint = new Color32(0x33, 0xE3, 0xA0, 0xFF);
            private static readonly Color Ink = new Color32(0x1B, 0x12, 0x2A, 0xFF);

            private static readonly Color MapGrass = new Color32(0x3E, 0x6E, 0x34, 0xFF);
            private static readonly Color MapDirt = new Color32(0xB0, 0x8E, 0x58, 0xFF);
            private static readonly Color MapLeaf = new Color32(0x1E, 0x45, 0x22, 0xFF);
            private static readonly Color MapTent = new Color32(0xD9, 0xB6, 0x7E, 0xFF);
            private static readonly Color MapFire = new Color32(0xFF, 0x8A, 0x1F, 0xFF);
            private static readonly Color MapGate = new Color32(0xB0, 0x5C, 0xFF, 0xFF);
            private static readonly Color MapFoe = new Color32(0x9C, 0xF0, 0x6A, 0xFF);
        }
    }
}
