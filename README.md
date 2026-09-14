<p align="center">
  <a href="https://lightside.media">
    <img src="docs/images/lightside-logo.png" width="96" alt="LightSide">
  </a>
</p>

<h1 align="center">LightSide Ecosystem</h1>

<p align="center"><strong>Text, vector graphics, animation and editing for Unity. A shared foundation for styles, state, input, geometry, rendering and authoring tools.</strong></p>

<p align="center">
  <a href="https://unity.com/releases/editor/qa/lts-releases"><img src="https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity" alt="Unity 2022.3 LTS or newer"></a>
  <a href="docs/hub/LICENSE.md"><img src="https://img.shields.io/badge/Hub_licence-MIT-green" alt="LightSide Hub licence: MIT"></a>
  <a href="https://discord.gg/ynRHp3wRmb"><img src="https://img.shields.io/discord/1474286776884396055?color=5865F2&amp;logo=discord&amp;logoColor=white&amp;label=Discord" alt="Discord community"></a>
</p>

<p align="center">
  <a href="https://github.com/LightSideKittens/LightSideEcosystem/releases"><img src=".github/assets/hub-cta.svg" alt="LightSide Hub releases" width="340"></a>
  &nbsp;
  <a href="https://discord.gg/ynRHp3wRmb"><img src=".github/assets/discord-cta.svg" alt="LightSide Discord" width="340"></a>
</p>

<p align="center">
  <a href="#products">Products</a>
  &nbsp; · &nbsp;
  <a href="https://unity.lightside.media">Documentation &amp; licences</a>
  &nbsp; · &nbsp;
  <a href="https://github.com/LightSideKittens/LightSideEcosystem/releases">Hub releases</a>
</p>

> [!TIP]
> **[Discord](https://discord.gg/ynRHp3wRmb) is the primary support and discussion channel for every LightSide product.** Questions, bug reports, help and feature requests all belong there.

## LightSide Hub

The LightSide catalogue, installation, versions, updates and licences in one Unity window. Multiple tokens, automatic product access, stable releases and pre-releases.

| Setup | Location |
| --- | --- |
| Installer | **LightSideHub.unitypackage** in [Releases](https://github.com/LightSideKittens/LightSideEcosystem/releases) |
| Import | **Assets → Import Package → Custom Package** |
| Hub window | **Tools → LightSide → Hub** |
| Licensed product access | Access tokens in **Licences**, with automatic product identification |

The Hub requires **Unity 2022.3 LTS or newer**. Each product retains its own licence; adding a token preserves access to the other products. **Check for updates** lists new Hub releases.

## Products

<table>
  <tr>
    <td width="50%" valign="top">
      <img src="docs/images/unitext-banner.jpg" alt="UniText" width="400">
      <p>Every language. Every style. No fonts required. Top performance. Rich editing, documents, layered effects and animation. Pixel-perfect fonts and Font Memory Mapping.</p>
    </td>
    <td width="50%" valign="top">
      <img src="docs/images/unishapes-banner.jpg" alt="UniShapes" width="400">
      <p>Vector graphics for UI and 3D, sharp at any scale. Shape and layer constructors, editable paths, boolean operations, gradients, textures, effects and animated control states.</p>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <img src="docs/images/unilottie-banner.jpg" alt="UniLottie" width="400">
      <p>Lottie animation for UI and 3D. Over 1,000 included animations, adaptive resolution, live previews and shared rendering for synchronized copies. Common shaders, batching and atlas infrastructure with LightSide.</p>
    </td>
    <td width="50%" valign="top">
      <img src="docs/images/moveit-banner.jpg" alt="MoveIt" width="400">
      <p>Animation for component and nested layer properties. Tweens, springs, inertia, sequences and reusable clips, with one engine for code and visual editing. Retargeting, reverse playback, timelines and live previews.</p>
    </td>
  </tr>
</table>

**LightSide Core** — The foundation of LightSide: rendering and GPU atlases, paints and effects, state and property access, motion, input, geometry, editor tools and shared runtime infrastructure.

**uGUI Fork** — Unity UI without bundled TextMeshPro. Canvas rendering, layout, controls, events and masks through the standard uGUI API. Free, without an account or access token.

Published versions and product availability are listed in the Hub. [Documentation and licences](https://unity.lightside.media).

## Shared systems

- **Effect builders.** UniText modifiers and UniShapes layers support nested, reusable effect stacks with the same authoring approach and editor controls.
- **Paints and styles.** Text and shapes share colors, gradients, textures and filters from Core.
- **One draw call.** Shared shaders and materials batch text, shapes and Lottie together.
- **Motion.** Shared clocks, easing and property access connect text animation, shape transitions and MoveIt timelines.

Text and shapes use the same paints and gradient editors. Their effect builders share a familiar workflow. MoveIt animates exposed component and layer properties. Core supplies these common systems once.

<details>
<summary>Rendering details</summary>

Batching follows material, texture, blend, mask, sorting and geometry boundaries. UI and world rendering have separate batches; lighting and shadow passes can add draws.

</details>

## Community & support

**[Discord](https://discord.gg/ynRHp3wRmb) is the preferred channel** for questions, bug reports, troubleshooting, feature requests and discussion across the ecosystem.

<p align="center">
  <a href="https://discord.gg/ynRHp3wRmb"><img src=".github/assets/discord-cta.svg" alt="LightSide Discord" width="340"></a>
</p>

A useful bug report contains the product and package version, Unity version, target platform, reproduction steps and relevant logs or screenshots. Access tokens and other credentials are private.

[GitHub Issues](https://github.com/LightSideKittens/LightSideEcosystem/issues) are also available for tracking reports.

## Licences

LightSide Hub is distributed under the [MIT licence](docs/hub/LICENSE.md). Each product has its own licence; installing the Hub does not grant a product licence.
