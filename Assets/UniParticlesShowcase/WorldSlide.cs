using LightSide.Promo;
using UnityEngine;

namespace LightSide.ParticleShowcase
{
    /// <summary>
    /// A slide showing a 3D world: objects on <see cref="Layer"/> under a root of their own, seen by a camera of their own
    /// into a texture the slide shows full frame. The world camera renders whenever the slide is posed, so the capture and
    /// the editor see the same frame.
    /// </summary>
    public abstract class WorldSlide : ShotSlide
    {
        /// <summary>The layer every world object of the showcase lives on; the reel's canvas camera never draws it.</summary>
        public const int Layer = 29;

        private RenderTexture texture;
        private readonly System.Collections.Generic.List<Object> owned = new();
        protected Camera Eye { get; private set; }
        protected Transform World { get; private set; }

        protected override void OnBuild(Stage stage)
        {
            Release();
            var previous = transform.Find("World");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var worldObject = new GameObject("World") { layer = Layer };
            worldObject.transform.SetParent(transform, false);
            worldObject.transform.localPosition = new Vector3(0f, 0f, 5000f);
            World = worldObject.transform;

            texture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "Showcase World", hideFlags = HideFlags.HideAndDontSave };
            var eyeObject = new GameObject("Eye", typeof(Camera)) { layer = Layer };
            eyeObject.transform.SetParent(World, false);
            Eye = eyeObject.GetComponent<Camera>();
            Eye.enabled = false;
            Eye.cullingMask = 1 << Layer;
            Eye.clearFlags = CameraClearFlags.SolidColor;
            Eye.nearClipPlane = 0.05f;
            Eye.farClipPlane = 200f;
            Eye.targetTexture = texture;
            if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene)) Eye.scene = gameObject.scene;

            var view = ReelKit.Node(stage.Root, "View", Vector2.zero, new Vector2(1920f, 1080f));
            var image = view.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            BuildWorld(stage);
            foreach (var t in World.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            foreach (var host in World.GetComponentsInChildren<UniParticlesWorld>(true))
            {
                host.gameObject.SetActive(true);
                host.Play();
            }
        }

        /// <summary>Builds the world under <see cref="World"/> and the slide's overlay under the stage.</summary>
        protected abstract void BuildWorld(Stage stage);

        /// <summary>Poses the world for <paramref name="local"/> seconds.</summary>
        protected abstract void PoseWorld(float local);

        protected override void OnRender(float local)
        {
            PoseWorld(local);
            Eye.Render();
        }

        /// <summary>A world particle host under <see cref="World"/> on the manual clock, built inactive with its first emitter emptied.</summary>
        protected UniParticlesWorld Host(string name, Vector3 at)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(World, false);
            go.transform.localPosition = at;
            go.SetActive(false);
            var host = go.AddComponent<UniParticlesWorld>();
            host.Effect.Clock = PlaybackClock.Manual;
            host.Effect.PlayOnEnable = false;
            host.Effect.Emitters[0].Modules.Clear();
            return host;
        }

        protected static void Show(UniParticlesWorld host, float seconds)
        {
            host.Play();
            host.Seek(Mathf.Max(0f, seconds));
        }

        protected GameObject Primitive(PrimitiveType type, Vector3 at, Vector3 scale, Material material, Quaternion turn = default)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(World, false);
            go.transform.localPosition = at;
            go.transform.localRotation = turn == default ? Quaternion.identity : turn;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private void OnDestroy() => Release();

        /// <summary>Keeps an object made for the world, hidden from saving, and destroys it with the slide or its next build.</summary>
        protected T Own<T>(T asset) where T : Object
        {
            asset.hideFlags = HideFlags.HideAndDontSave;
            owned.Add(asset);
            return asset;
        }

        /// <summary>A URP Lit material of <paramref name="color"/>, destroyed with the slide.</summary>
        protected Material Lit(Color color, float smoothness = 0.2f)
        {
            var material = Own(new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color });
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private void Release()
        {
            foreach (var asset in owned)
                if (asset != null) Object.DestroyImmediate(asset);
            owned.Clear();
            if (texture == null) return;
            texture.Release();
            Object.DestroyImmediate(texture);
            texture = null;
        }
    }
}
