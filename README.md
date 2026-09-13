<p align="center">
  <img src="docs/images/lightside-logo.png" width="80" alt="LightSide">
</p>

<h1 align="center">LightSide Ecosystem</h1>

<p align="center">Text, vector UI, animation and motion tools for Unity.</p>

<p align="center">
  <a href="https://github.com/LightSideKittens/LightSideEcosystem/releases"><strong>Download LightSide Hub</strong></a>
  &nbsp; · &nbsp;
  <a href="https://unity.lightside.media">Documentation &amp; licences</a>
  &nbsp; · &nbsp;
  <a href="https://github.com/LightSideKittens/LightSideEcosystem/issues">Report an issue</a>
</p>

## Start with LightSide Hub

The Hub brings the product catalogue, licences and package installation into one Unity Editor window.

1. Download **LightSideHub.unitypackage** from [Releases](https://github.com/LightSideKittens/LightSideEcosystem/releases).
2. Import it into your Unity project.
3. Open **Tools → LightSide → Hub**, add your access tokens in **Licences**, and select the products to install.

The Hub requires **Unity 2022.3 LTS or newer**. Each token identifies its products automatically; adding another licence preserves access to your other products. **Check for updates** finds new Hub releases in this repository.

## Products

<table>
  <tr>
    <td width="50%" valign="top">
      <img src="docs/images/unitext-banner.jpg" alt="UniText" width="400">
      <p>Unicode text rendering with complex-script shaping, right-to-left text and extensible markup.</p>
    </td>
    <td width="50%" valign="top">
      <img src="docs/images/unishapes-banner.jpg" alt="UniShapes" width="400">
      <p>SDF vector UI for uGUI, with composable fills, strokes, shadows and effects.</p>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <img src="docs/images/unilottie-banner.jpg" alt="UniLottie" width="400">
      <p>Lottie animation playback with native ThorVG rendering.</p>
    </td>
    <td width="50%" valign="top">
      <img src="docs/images/moveit-banner.jpg" alt="MoveIt" width="400">
      <p>Motion tools built around tweens, springs and timelines, evaluated with Burst.</p>
    </td>
  </tr>
</table>

**LightSide Core** supplies the shared runtime and Editor foundation. **uGUI Fork** is available as a separate product in the Hub and provides LightSide's Unity UI integration.

The Hub shows published versions and availability for each product. Documentation, product requirements and licence details are available at [unity.lightside.media](https://unity.lightside.media).

## Support

For bugs and feature requests, [open an issue](https://github.com/LightSideKittens/LightSideEcosystem/issues) with the product, package version, Unity version and steps to reproduce. Keep access tokens and other credentials out of public reports.

## Development workspace

This repository hosts the shared Unity development project and the public Hub releases. Package sources live in separate submodules, some of which require private repository access.

Contributors with access can clone the workspace with:

```sh
git clone --recurse-submodules https://github.com/LightSideKittens/LightSideEcosystem.git
```

Use the Unity Editor version recorded in [`ProjectSettings/ProjectVersion.txt`](ProjectSettings/ProjectVersion.txt).

## Licences

LightSide Hub is distributed under the [MIT licence](docs/hub/LICENSE.md). Each product has its own licence; installing the Hub does not grant a product licence.
