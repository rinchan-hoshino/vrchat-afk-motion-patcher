#if UNITY_EDITOR
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.fluent;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(RinChan.AfkMotionPatcher.AfkMotionPatchPlugin))]

namespace RinChan.AfkMotionPatcher
{
    internal sealed class AfkMotionPatchPlugin : Plugin<AfkMotionPatchPlugin>
    {
        public override string QualifiedName => "com.rinchan.afk-motion-patcher";
        public override string DisplayName => "AFK Motion Patcher";

        protected override void Configure()
        {
            var sequence = InPhase(BuildPhase.Transforming);
            sequence.WithRequiredExtension(typeof(AnimatorServicesContext), _ =>
            {
                sequence.Run(AfkMotionPatchPass.Instance);
            });
        }
    }

    [RunsOnPlatforms(WellKnownPlatforms.VRChatAvatar30)]
    [DependsOnContext(typeof(AnimatorServicesContext))]
    internal sealed class AfkMotionPatchPass : Pass<AfkMotionPatchPass>
    {
        protected override void Execute(nadena.dev.ndmf.BuildContext context)
        {
            var avatarRoot = context.AvatarRootObject;
            if (!AfkMotionPatchAuthoring.TryGetActivePatch(avatarRoot, out var patch)) return;
            if (!patch.patchActionLayer) return;

            if (!AfkMotionPatchAuthoring.GeneratedClipsReady(patch))
            {
                var message = $"[AfkMotionPatch] Generated AFK clips are missing for avatar={avatarRoot.name}, patch={AfkMotionPatchAuthoring.GetPath(patch.transform)}. Reopen/edit the patch component in Unity so authoring can materialize clips before build/preview.";
                if (patch.failOnMissingSource) Debug.LogError(message, patch);
                else Debug.LogWarning(message, patch);
                return;
            }

            var controllerContext = context.Extension<AnimatorServicesContext>().ControllerContext;
            if (!controllerContext.Controllers.TryGetValue(VRCAvatarDescriptor.AnimLayerType.Action, out var actionController) || actionController == null)
            {
                Debug.LogWarning($"[AfkMotionPatch] Action controller is unavailable in AnimatorServicesContext for avatar={avatarRoot.name}.", patch);
                return;
            }

            var introMotion = controllerContext.Clone(patch.generatedIntroClip);
            var loopMotion = controllerContext.Clone(patch.generatedLoopClip);
            var outroMotion = controllerContext.Clone(patch.generatedOutroClip);
            if (introMotion == null || loopMotion == null || outroMotion == null)
            {
                Debug.LogError($"[AfkMotionPatch] Failed to virtualize generated AFK clips for avatar={avatarRoot.name}.", patch);
                return;
            }

            var replaced = 0;
            foreach (var layer in actionController.Layers.ToArray())
            {
                if (layer.StateMachine != null)
                {
                    foreach (var state in layer.StateMachine.AllStates())
                    {
                        replaced += PatchStateMotion(state, patch, introMotion, loopMotion, outroMotion);
                    }
                }

                if (layer.SyncedLayerMotionOverrides.Count > 0)
                {
                    var overrides = layer.SyncedLayerMotionOverrides;
                    foreach (var kvp in layer.SyncedLayerMotionOverrides.ToArray())
                    {
                        var replacement = SelectReplacement(kvp.Value, patch, introMotion, loopMotion, outroMotion);
                        if (replacement == null) continue;
                        overrides = overrides.SetItem(kvp.Key, replacement);
                        replaced++;
                    }
                    layer.SyncedLayerMotionOverrides = overrides;
                }
            }

            if (replaced == 0)
            {
                var message = $"[AfkMotionPatch] No target AFK motions matched in Action controller for avatar={avatarRoot.name}, patch={AfkMotionPatchAuthoring.GetPath(patch.transform)}.";
                if (patch.failOnMissingSource) Debug.LogError(message, patch);
                else Debug.LogWarning(message, patch);
                return;
            }

            Debug.Log($"[AfkMotionPatch] Patched Action AFK motions via NDMF avatar={avatarRoot.name}, patch={AfkMotionPatchAuthoring.GetPath(patch.transform)}, replaced={replaced}", patch);
        }

        private static int PatchStateMotion(VirtualState state, AfkMotionPatch patch, VirtualMotion introMotion, VirtualMotion loopMotion, VirtualMotion outroMotion)
        {
            var replacement = SelectReplacement(state.Motion, patch, introMotion, loopMotion, outroMotion);
            if (replacement == null) return 0;
            state.Motion = replacement;
            return 1;
        }

        private static VirtualMotion SelectReplacement(VirtualMotion motion, AfkMotionPatch patch, VirtualMotion introMotion, VirtualMotion loopMotion, VirtualMotion outroMotion)
        {
            if (motion == null) return null;
            var motionName = motion.Name;
            if (AfkMotionPatchAuthoring.MatchesTargetClipName(motionName, patch.targetIntroMotion)) return introMotion;
            if (AfkMotionPatchAuthoring.MatchesTargetClipName(motionName, patch.targetLoopMotion)) return loopMotion;
            if (AfkMotionPatchAuthoring.MatchesTargetClipName(motionName, patch.targetOutroMotion)) return outroMotion;
            return null;
        }
    }
}
#endif
