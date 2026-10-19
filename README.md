# FraxinusGraphics Demo

[English](README.md) | [简体中文](README.zh-CN.md)

A Sponza rendering example for **Unity 2022.3.62f3**, powered by [FraxinusGraphics](https://github.com/bluewhitebow/FraxinusGraphics). Explore **APRTV relighting**, **GI and probe debugging**, **cached shadow cascades**, and **STP / SGSR upscaling** in one scene. Materials, rendering presets and precomputed transport for **4,563 probes** are included.

[Rendering source](https://github.com/bluewhitebow/FraxinusGraphics/tree/main/Packages) · [Features](https://github.com/bluewhitebow/FraxinusGraphics/blob/main/README.md) · [Usage guide](https://github.com/bluewhitebow/FraxinusGraphics/blob/main/docs/Usage.md) · [Open the demo](#open-the-project)

![Sponza rendered by this example](Documentation/images/sponza.png)

## Open the project

Install Git so `git --version` works from a terminal, then clone the demo:

```bash
git clone https://github.com/bluewhitebow/FraxinusGraphicsDemo.git
```

1. Add the cloned folder in Unity Hub and open it with Unity **2022.3.62f3**.
2. Wait for package resolution, asset import and shader compilation.
3. Open `Assets/FraxinusSponza/Scenes/Sponza.unity`, or use **Fraxinus > Sponza > Open Demo**.
4. Enter Play mode. The demo panel appears and existing probe data loads automatically.

Unity automatically downloads the four public rendering packages from GitHub; no GitHub authorization or separate source checkout is required.

## Demo controls

| Control | Effect |
| --- | --- |
| APRTV indirect lighting / GI only | Compare final shading with and without indirect light, or inspect the GI contribution. |
| Show probes | Display small spheres at baked probe positions, colored by their lighting response. |
| Cache distant shadow cascades | Switch between the two shadow presets. Use **Refresh shadow cache** after changing shadow casters. |
| Native / STP / SGSR1 / SGSR2 | Compare full resolution with reconstruction at a `0.75` render scale. Temporal modes enable post-processing and disable MSAA. |
| Sun rotation | Rotate the directional light while retaining the baked geometry transport. |
| `1` / `2` | Jump to the courtyard or balcony camera. |
| Hold right mouse + `WASD`, `Q` / `E` | Fly the camera; hold `Shift` for faster movement. |
| `H` / `Esc` | Hide the panel / release the mouse. |
| Reset demo | Restore the initial camera, sun, lighting and rendering preset. |

Stopping Play mode restores the assigned rendering assets. Actual upscaler availability depends on the graphics device.

![APRTV probe visualization from the running example](Documentation/images/probes.png)

Probe spheres show baked positions and their lighting response; the overlay can be toggled independently of the GI view.

## Linked packages

[`Packages/manifest.json`](Packages/manifest.json) uses direct Git dependencies for RPCore, URPExtensions, URPGlobal and Light Transport, all pinned to [FraxinusGraphics commit `f48668c`](https://github.com/bluewhitebow/FraxinusGraphics/commit/f48668c738d5d219d98fbb77feac7f4b5def26da). No adjacent checkout or submodule is required; edits to a separate local checkout do not affect this demo.

When updating the packages, push the rendering changes first, update all four commit references together, then validate the demo and commit the manifest with Unity's updated `Packages/packages-lock.json`.

## Scene and lighting

The example uses the Universal Renderer and APRTV with **Live Scene Lighting**. Geometry transport is already baked; directional-light and sky changes can relight it. Editing geometry or materials requires another **Bake APRT** from the APRT Volume Inspector or Lighting window.

Scene lighting and the Volume Profile are under `Assets/FraxinusSponza/Scenes/Sponza/`; pipeline presets are under `Assets/FraxinusSponza/Rendering/`.

See the [rendering suite overview](https://github.com/bluewhitebow/FraxinusGraphics/blob/main/README.md) and [usage guide](https://github.com/bluewhitebow/FraxinusGraphics/blob/main/docs/Usage.md) for more details.

**Fraxinus > Sponza > Validate Demo** checks scene references, package sources and commits, and probe loading. Its report and preview are written under `Library/`.

## Assets

See [asset provenance](Documentation/AssetProvenance.md). Rendering packages retain their own license and third-party notices.
