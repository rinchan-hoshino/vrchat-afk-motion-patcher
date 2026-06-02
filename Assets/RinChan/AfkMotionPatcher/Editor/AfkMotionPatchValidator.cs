#if UNITY_EDITOR
using System;
using System.Linq;
using nadena.dev.ndmf;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace RinChan.AfkMotionPatcher.Editor
{
    internal static class AfkMotionPatchValidator
    {
        private const string MenuPath = "Tools/RinChan/AFK Motion Patcher/Validate Selected Avatar";

        [MenuItem(MenuPath, true)]
        private static bool ValidateMenu()
        {
            var selected = Selection.activeGameObject;
            return selected != null && selected.GetComponentInParent<VRCAvatarDescriptor>() != null;
        }

        [MenuItem(MenuPath, false, 100)]
        private static void RunMenu()
        {
            var selected = Selection.activeGameObject;
            var descriptor = selected != null ? selected.GetComponentInParent<VRCAvatarDescriptor>() : null;
            if (descriptor == null)
            {
                EditorUtility.DisplayDialog("AFK Motion Patcher", "Select an avatar or an object under an avatar.", "OK");
                return;
            }

            RunValidation(descriptor.gameObject);
        }

        private static void RunValidation(GameObject avatarRoot)
        {
            var patches = avatarRoot.GetComponentsInChildren<AfkMotionPatch>(true);

            Debug.Log($"[AfkMotionPatchValidate] avatar={avatarRoot.name} active={avatarRoot.activeInHierarchy} patches={patches.Length}", avatarRoot);
            foreach (var patch in patches)
            {
                Debug.Log($"[AfkMotionPatchValidate] patch={AfkMotionPatchEditorUtil.GetPath(patch.transform)} enabled={patch.enabled} patchAction={patch.patchActionLayer}", patch);
            }

            if (patches.Length == 0)
            {
                EditorUtility.DisplayDialog("AFK Motion Patcher", "No AfkMotionPatch was found under the selected avatar.", "OK");
                return;
            }

            var clone = UnityEngine.Object.Instantiate(avatarRoot);
            clone.name = avatarRoot.name + "__AfkMotionPatchValidationClone";
            clone.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                VRCBuildPipelineCallbacks.OnPreprocessAvatar(clone);

                var descriptor = clone.GetComponent<VRCAvatarDescriptor>();
                var action = descriptor != null ? descriptor.baseAnimationLayers.FirstOrDefault(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.Action) : default;
                var actionController = action.animatorController as AnimatorController;
                var replacementHits = 0;

                Debug.Log($"[AfkMotionPatchValidate] actionActive={action.isDefault == false} actionController={(action.animatorController != null ? action.animatorController.name : "NULL")}", descriptor);
                if (actionController != null)
                {
                    for (var i = 0; i < actionController.layers.Length; i++)
                    {
                        var layer = actionController.layers[i];
                        foreach (var childState in layer.stateMachine.states)
                        {
                            var motionName = childState.state.motion != null ? childState.state.motion.name : "NULL";
                            var stateName = childState.state.name ?? string.Empty;
                            if ((stateName + " " + motionName).IndexOf("afk", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            Debug.Log($"[AfkMotionPatchValidate] layer[{i}] afkState={stateName} motion={motionName}", actionController);
                            if (motionName.StartsWith("AfkMotionPatch_", StringComparison.Ordinal)) replacementHits++;
                        }
                    }
                }

                var ok = actionController != null && replacementHits > 0;
                Debug.Log($"[AfkMotionPatchValidate] result={(ok ? "OK" : "FAILED")} replacementHits={replacementHits}", avatarRoot);
                EditorUtility.DisplayDialog(
                    "AFK Motion Patcher",
                    ok ? "OK: processed Action controller contains generated replacement AFK motions. See Console for details." : "FAILED: processed Action controller does not show generated replacement AFK motions. See Console for details.",
                    "OK");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("AFK Motion Patcher", "Validation threw an exception. See Console for details.", "OK");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clone);
                AvatarProcessor.CleanTemporaryAssets();
            }
        }
    }
}
#endif
