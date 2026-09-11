using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide.Hub
{
    /// <summary>
    /// Opens the Hub once per session while the project has no registry credential, which is the only
    /// moment a user cannot get any further without it.
    /// </summary>
    [InitializeOnLoad]
    internal static class HubAutoOpen
    {
        private const string SessionKey = "LightSide.Hub.ShownThisSession";

        static HubAutoOpen()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            EditorApplication.delayCall += () =>
            {
                if (HubRegistry.IsConfigured) return;
                SessionState.SetBool(SessionKey, true);
                HubWindow.Open();
            };
        }
    }

    /// <summary>
    /// The LightSide Hub: licence setup, the product catalogue, and the Hub's own updates. It compiles
    /// against no LightSide package, because it is what puts them in the project.
    /// </summary>
    internal sealed class HubWindow : EditorWindow
    {
        private static readonly Vector2 MinimumSize = new(720f, 480f);

        private string selectedProductId;
        private VisualElement rail;
        private VisualElement page;
        private VisualElement updateBar;

        [MenuItem("Tools/LightSide/Hub", false, 0)]
        public static void Open()
        {
            var window = GetWindow<HubWindow>();
            window.titleContent = new GUIContent("LightSide");
            window.minSize = MinimumSize;
            window.Show();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            HubVisuals.Attach(root);

            root.Add(BuildHeader());

            updateBar = new VisualElement();
            root.Add(updateBar);

            var body = new VisualElement();
            body.AddToClassList("hub__body");

            rail = new VisualElement();
            rail.AddToClassList("hub__rail");
            body.Add(rail);

            var scroll = new ScrollView();
            scroll.AddToClassList("hub__page");
            page = scroll;
            body.Add(scroll);

            root.Add(body);

            selectedProductId ??= HubRegistry.IsConfigured && HubCatalog.Products.Count > 0
                ? HubCatalog.Products[0].Id
                : null;

            BuildRail();
            BuildPage();
            RefreshCatalog();
            if (HubUpdater.DueForCheck()) CheckForUpdate(false);
        }

        private VisualElement BuildHeader()
        {
            var mark = new VisualElement { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("hub__brand-mark");

            var brand = new Label("LightSide Hub") { pickingMode = PickingMode.Ignore };
            brand.AddToClassList("hub__brand");

            var header = HubVisuals.Row(
                mark,
                brand,
                HubVisuals.Spacer(),
                HubVisuals.Badge(HubConfig.Version),
                HubVisuals.QuietButton("Check for updates", () => CheckForUpdate(true)));
            header.AddToClassList("hub__header");
            return header;
        }

        private void BuildRail()
        {
            rail.Clear();
            rail.Add(RailHeading("Account"));
            rail.Add(RailItem("Licence", null, HubRegistry.IsConfigured));

            rail.Add(RailHeading("Products"));
            foreach (var product in HubCatalog.Products)
                rail.Add(RailItem(product.DisplayName, product.Id,
                    HubRegistry.InstalledVersion(product.PackageName) != null));
        }

        private Label RailHeading(string text)
        {
            var label = new Label(text.ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("hub__rail-heading");
            return label;
        }

        private Button RailItem(string text, string productId, bool installed)
        {
            var button = new Button(() =>
            {
                if (selectedProductId == productId) return;
                selectedProductId = productId;
                BuildRail();
                BuildPage();
            });
            button.AddToClassList("hub__rail-item");
            button.EnableInClassList("hub__rail-item--selected", selectedProductId == productId);

            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("hub__rail-dot");
            dot.EnableInClassList("hub__rail-dot--on", installed);
            button.Add(dot);
            button.Add(new Label(text) { pickingMode = PickingMode.Ignore });
            return button;
        }

        /// <summary>Rebuilds the content pane, and the rail with it, since installing changes both.</summary>
        private void Reload()
        {
            BuildRail();
            BuildPage();
        }

        private void BuildPage()
        {
            page.Clear();

            if (selectedProductId == null)
            {
                page.Add(new HubLicencePage(Reload).Build());
                return;
            }

            foreach (var product in HubCatalog.Products)
                if (product.Id == selectedProductId)
                {
                    page.Add(new HubProductPage(product, Reload).Build());
                    return;
                }

            page.Add(HubVisuals.Empty("This product is no longer in the catalogue."));
        }

        private void RefreshCatalog()
        {
            var request = UnityEngine.Networking.UnityWebRequest.Get(HubConfig.CatalogUrl);
            request.SendWebRequest().completed += _ =>
            {
                var body = request.result == UnityEngine.Networking.UnityWebRequest.Result.Success
                    ? request.downloadHandler.text
                    : null;
                request.Dispose();
                if (body == null || this == null) return;

                var before = HubCatalog.Products.Count;
                HubCatalog.Accept(body);
                if (HubCatalog.Products.Count != before) Reload();
            };
        }

        private void CheckForUpdate(bool announceWhenCurrent)
        {
            HubUpdater.CheckLatest((release, error) =>
            {
                if (this == null) return;
                updateBar.Clear();

                if (error != null)
                {
                    if (announceWhenCurrent)
                        updateBar.Add(HubVisuals.Status(
                            "Could not reach GitHub: " + error, HubStatus.Warning));
                    return;
                }

                if (!release.IsNewer)
                {
                    if (announceWhenCurrent)
                        updateBar.Add(HubVisuals.Status(
                            "The Hub is up to date.", HubStatus.Info));
                    return;
                }

                updateBar.Add(BuildUpdateBar(release));
            });
        }

        private VisualElement BuildUpdateBar(HubRelease release)
        {
            var card = HubVisuals.Card();
            card.Add(HubVisuals.Row(
                HubVisuals.Text($"LightSide Hub {release.Version} is available."),
                HubVisuals.Spacer(),
                HubVisuals.PrimaryButton("Update", () => ApplyUpdate(release))));
            return card;
        }

        private void ApplyUpdate(HubRelease release)
        {
            updateBar.Clear();
            updateBar.Add(HubVisuals.Status($"Downloading {release.Version}…", HubStatus.Info));

            HubUpdater.Apply(release, error =>
            {
                if (this == null) return;
                updateBar.Clear();
                updateBar.Add(error == null
                    ? HubVisuals.Status(
                        $"Hub {release.Version} imported. Unity is recompiling.", HubStatus.Info)
                    : HubVisuals.Status("Update failed: " + error, HubStatus.Error));
            });
        }
    }
}
