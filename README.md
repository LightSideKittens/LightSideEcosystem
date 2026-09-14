<p align="center">
  <a href="https://lightside.media">
    <img src="docs/images/lightside-logo.png" width="96" alt="LightSide">
  </a>
</p>

<h1 align="center">LightSide Ecosystem</h1>

<p align="center"><strong>Unity products with a common foundation. Text, documents, vector graphics and property animation use reusable systems throughout their runtime and editor workflows. Product-specific features extend the same infrastructure.</strong></p>

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

The entry point to the LightSide ecosystem inside Unity. Product discovery, installation, release channels and licence management share one window. Features include multiple tokens, automatic product access and update checks. The catalogue distinguishes released products from upcoming ones. Packages retain their own release histories and licences.

| Setup | Location |
| --- | --- |
| Installer | **LightSideHub.unitypackage** in [Releases](https://github.com/LightSideKittens/LightSideEcosystem/releases) |
| Import | **Assets → Import Package → Custom Package** |
| Installed package | `Packages/media.lightside.hub` |
| Hub window | **Tools → LightSide → Hub** |
| Licensed product access | Access tokens in **Licences**, with automatic product identification |

The Hub requires **Unity 2022.3 LTS or newer**. Each product retains its own licence; adding a token preserves access to the other products. **Check for updates** lists new Hub releases; **Update** imports their unitypackage into the same embedded package location.

## Products

<table>
  <tr>
    <td width="50%" valign="top">
      <img src="docs/images/unitext-banner.jpg" alt="UniText" width="400">
      <p>Text and document engine for Unity UI and 3D. Every language. Any style. No fonts required. Top performance. Capabilities include pixel fonts, Font Memory Mapping, rich editing, text animation and virtualized documents. Reusable constructors define styles, input behavior and decoration. Custom modifiers, parsing rules and interactions extend the same pipeline.</p>
    </td>
    <td width="50%" valign="top">
      <img src="docs/images/unishapes-banner.jpg" alt="UniShapes" width="400">
      <p>A vector graphics system for Unity UI and 3D, sharp at any scale. Capabilities include editable geometry, boolean shapes, layered effects, gradients, textures and animated control states. Shape providers and layers form reusable constructors. Custom geometry and effects extend the same workflow and share LightSide infrastructure with the other products.</p>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <img src="docs/images/unilottie-banner.jpg" alt="UniLottie" width="400">
      <p>A Lottie animation system for Unity UI and 3D, with over 1,000 included animations. Features include adaptive resolution, shared frame rendering, runtime animation sources and editor previews. Synchronized copies share frame processing while keeping their own appearance and placement. Common LightSide infrastructure supports playback, rendering and asset tools.</p>
    </td>
    <td width="50%" valign="top">
      <img src="docs/images/moveit-banner.jpg" alt="MoveIt" width="400">
      <p>A property animation system for Unity. Code and visual authoring share one engine. Motion options include tweens, springs and inertia; sequences and reusable clips coordinate them across targets. Supported targets include component properties, UI Toolkit styles and nested layer settings. Custom setters and shared property bindings extend animation to application data and new components.</p>
    </td>
  </tr>
</table>

**LightSide Core** — The shared foundation of LightSide. Systems include rendering, GPU atlases, paints, state, property access, motion, input, geometry, editor tools and runtime services. Asset selectors, layer editors and timelines use common authoring infrastructure. Workers, pools, serialization and resource management serve the packages throughout their lifecycle. Each product adds its own behavior while reusing those implementations.

**uGUI Fork** — The Unity Canvas UI foundation for LightSide, without bundled TextMeshPro. Its systems include Canvas rendering, layout, controls, events and masking through the standard uGUI API. UniText, UniShapes and UniLottie add their own content through this foundation. Free to browse and install without an account or access token.

Published versions and product availability are listed in the Hub. [Documentation and licences](https://unity.lightside.media).

## Shared systems

- **Construction workflow.** UniText modifiers and UniShapes layers use nested settings, reusable presets and common property editors. Custom modifiers and layers participate in the same workflow.
- **Paints and rendering.** Text and shapes share colors, gradients, textures and filters. Common shaders and material pools allow compatible text, shapes and Lottie to batch in one draw call.
- **State and interaction.** Shared property access, observable state and pointer routing connect editing, runtime changes and interaction. Nested settings remain accessible to the systems that animate them.
- **Motion and timelines.** Common clocks, curves and property bindings support text animation, shape transitions and MoveIt. Reusable timeline controls provide the editor workflow.
- **Editor tools.** Asset selectors, layer lists, gradient and curve editors, scene tools and command palettes reuse Core implementations. Undo, multi-object editing and prefab overrides follow common interaction rules.
- **Runtime infrastructure.** GPU atlases and uploads, worker threads, pools, collections, serialization and resource management serve the products throughout their lifecycle.

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
