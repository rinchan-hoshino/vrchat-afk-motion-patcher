# VRChat AFK Motion Patcher

A generic pure-NDMF VRChat avatar build-time component for replacing AFK motions inside the avatar's existing Action controller.

This package is the **core plugin only**. It does not contain Chocolat/Plum-specific paths, presets, source animations, generated clips, or Modular Avatar components.

## What it does

- Lets an adapter component name three target Action motions to replace: intro, loop, outro.
- Lets an adapter component provide three replacement source clips.
- Optionally remaps renderer paths and blendShape names while creating transient build-time replacement clips.
- During NDMF processing, patches the virtualized Action controller via `AnimatorServicesContext`.
- Leaves the avatar descriptor and source controller assets untouched.

## What it does not do

- Does not write `VRCAvatarDescriptor.baseAnimationLayers`.
- Does not replace the whole Action controller.
- Does not depend on Modular Avatar Merge Animator.
- Does not install a custom AFK state machine.
- Does not add a Play Mode preprocess trigger.

## Requirements

- Unity 2022.3.x VRChat avatar project
- VRChat SDK Avatars 3.x
- NDMF

## Installation

Copy or clone this repository's folder into a Unity project:

```text
Assets/RinChan/AfkMotionPatcher
```

Then add `AfkMotionPatch` to a GameObject under the avatar root, or use an adapter pack that configures it for a specific source/target avatar pair.

## Generated assets

The core plugin does not write persistent remapped clips into the project. Replacement clips are created transiently inside the NDMF preprocess pass and are emitted only as part of NDMF's normal processed-avatar output.

## Validation

Use:

```text
Tools > RinChan > AFK Motion Patcher > Validate Selected Avatar
```

The validator runs the same VRC/NDMF preprocess path on a temporary clone and checks that the processed Action controller contains generated replacement AFK motions.

## Companion adapter packs

Character-specific presets should live outside this core package. For example:

- `chocolat-afk-adapters`: Chocolat VRSuya AFK source clips configured for target avatars such as Plum.

## License

MIT. See [LICENSE](LICENSE).
