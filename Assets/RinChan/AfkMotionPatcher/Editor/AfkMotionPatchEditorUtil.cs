#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RinChan.AfkMotionPatcher
{
    internal static class AfkMotionPatchEditorUtil
    {
        public static bool TryGetActivePatch(GameObject avatarRoot, out AfkMotionPatch patch)
        {
            patch = null;
            if (avatarRoot == null) return false;

            var patches = avatarRoot.GetComponentsInChildren<AfkMotionPatch>(true)
                .Where(p => p.patchActionLayer)
                .OrderBy(p => GetPath(p.transform))
                .ToArray();
            if (patches.Length == 0) return false;

            var activePatches = patches.Where(p => p.enabled && p.gameObject.activeInHierarchy).ToArray();
            if (activePatches.Length == 0) return false;

            patch = activePatches[0];
            if (activePatches.Length > 1)
            {
                Debug.LogWarning($"[AfkMotionPatch] Multiple active patches found under avatar={avatarRoot.name}; using {GetPath(patch.transform)}. Candidates={string.Join(", ", activePatches.Select(p => GetPath(p.transform)))}", patch);
            }

            return true;
        }

        public static bool RequiredClipsPresent(AfkMotionPatch patch)
        {
            return patch != null
                && patch.targetIntroMotion != null
                && patch.targetLoopMotion != null
                && patch.targetOutroMotion != null
                && patch.replacementIntroSource != null
                && patch.replacementLoopSource != null
                && patch.replacementOutroSource != null;
        }

        public static AnimationClip CreateAdaptedClipClone(AnimationClip source, GameObject avatarRoot, AfkMotionPatch patch, string suffix)
        {
            var clone = UnityEngine.Object.Instantiate(source);
            clone.name = $"AfkMotionPatch_{source.name}_{suffix}";

            var allBindings = AnimationUtility.GetCurveBindings(clone);
            var rebuilt = new List<(EditorCurveBinding binding, AnimationCurve curve)>();
            foreach (var binding in allBindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clone, binding);
                if (curve == null) continue;
                var newBinding = RemapBinding(binding, avatarRoot, patch);
                if (newBinding.HasValue) rebuilt.Add((newBinding.Value, curve));
            }
            foreach (var binding in allBindings) AnimationUtility.SetEditorCurve(clone, binding, null);
            foreach (var item in rebuilt) AnimationUtility.SetEditorCurve(clone, item.binding, item.curve);

            return clone;
        }

        public static bool MatchesTargetClipName(string motionName, AnimationClip expectedClip)
        {
            if (string.IsNullOrEmpty(motionName) || expectedClip == null) return false;
            return string.Equals(motionName, expectedClip.name, StringComparison.Ordinal)
                || string.Equals(motionName, expectedClip.name.Replace(".anim", string.Empty), StringComparison.Ordinal);
        }

        private static EditorCurveBinding? RemapBinding(EditorCurveBinding binding, GameObject avatarRoot, AfkMotionPatch patch)
        {
            var result = binding;

            var pathRemap = patch.rendererPathRemaps.FirstOrDefault(r => r != null && binding.path == (r.fromPath ?? string.Empty));
            if (pathRemap != null) result.path = pathRemap.toPath ?? string.Empty;

            if (binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
            {
                var shape = binding.propertyName.Substring("blendShape.".Length);
                var shapeRemap = patch.blendShapeNameRemaps.FirstOrDefault(r => r != null && shape == (r.fromName ?? string.Empty));
                if (shapeRemap != null) result.propertyName = "blendShape." + (shapeRemap.toName ?? string.Empty);

                if (patch.dropMissingBlendShapes && !BlendshapeExists(avatarRoot, result.path, result.propertyName.Substring("blendShape.".Length)))
                {
                    return null;
                }
            }

            return result;
        }

        private static bool BlendshapeExists(GameObject root, string rendererPath, string shapeName)
        {
            var t = string.IsNullOrEmpty(rendererPath) ? root.transform : root.transform.Find(rendererPath);
            var smr = t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
            var mesh = smr != null ? smr.sharedMesh : null;
            if (mesh == null) return false;
            for (var i = 0; i < mesh.blendShapeCount; i++) if (mesh.GetBlendShapeName(i) == shapeName) return true;
            return false;
        }

        public static string GetPath(Transform transform)
        {
            var parts = new List<string>();
            while (transform != null)
            {
                parts.Add(transform.name);
                transform = transform.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
#endif
