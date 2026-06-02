#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace RinChan.AfkMotionPatcher
{
    [InitializeOnLoad]
    public static class AfkMotionPatchAuthoring
    {
        private const string GeneratedRoot = "Assets/RinChan/AfkMotionPatcher/Baked";
        private const string GeneratorVersion = "2026-06-02_generic_afk_motion_patch_v1";

        private static readonly HashSet<int> PendingAvatarIds = new HashSet<int>();
        private static bool processQueued;
        private static bool isProcessing;

        static AfkMotionPatchAuthoring()
        {
            EditorApplication.hierarchyChanged += ScheduleLoadedAvatars;
            EditorApplication.projectChanged += ScheduleLoadedAvatars;
            EditorApplication.delayCall += ScheduleLoadedAvatars;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    EditorApplication.delayCall += ScheduleLoadedAvatars;
                }
            };
            EditorSceneManager.sceneOpened += (_, __) => ScheduleLoadedAvatars();
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            EditorApplication.delayCall += ScheduleLoadedAvatars;
        }

        public static void Schedule(AfkMotionPatch patch)
        {
            if (patch == null) return;
            var descriptor = patch.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null) return;
            Schedule(descriptor.gameObject);
        }

        public static void Schedule(GameObject avatarRoot)
        {
            if (avatarRoot == null) return;
            PendingAvatarIds.Add(avatarRoot.GetInstanceID());
            if (processQueued) return;
            processQueued = true;
            EditorApplication.delayCall += ProcessPending;
        }

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

        public static bool GeneratedClipsReady(AfkMotionPatch patch)
        {
            return patch != null
                && patch.generatedIntroClip != null
                && patch.generatedLoopClip != null
                && patch.generatedOutroClip != null
                && AssetDatabase.Contains(patch.generatedIntroClip)
                && AssetDatabase.Contains(patch.generatedLoopClip)
                && AssetDatabase.Contains(patch.generatedOutroClip);
        }

        public static void SyncAvatar(GameObject avatarRoot)
        {
            if (EditorApplication.isPlaying) return;
            if (avatarRoot == null) return;
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null || !avatarRoot.scene.IsValid() || !avatarRoot.scene.isLoaded) return;
            if (!TryGetActivePatch(avatarRoot, out var patch)) return;

            if (!RequiredClipsPresent(patch))
            {
                Debug.LogWarning("[AfkMotionPatch] Sync skipped because required target/replacement clips are missing.", patch);
                return;
            }

            var signature = ComputeSignature(avatarRoot, patch);
            if (patch.generatedSignature == signature && GeneratedClipsReady(patch)) return;

            var introAdapted = CreateAdaptedClipClone(patch.replacementIntroSource, avatarRoot, patch, "Intro");
            var loopAdapted = CreateAdaptedClipClone(patch.replacementLoopSource, avatarRoot, patch, "Loop");
            var outroAdapted = CreateAdaptedClipClone(patch.replacementOutroSource, avatarRoot, patch, "Outro");

            EnsureFolder(GeneratedRoot);
            var folder = GeneratedRoot + "/" + BuildAssetKey(avatarRoot, patch);
            EnsureFolder(folder);

            var introPath = folder + "/" + SanitizeFileName(introAdapted.name) + ".anim";
            var loopPath = folder + "/" + SanitizeFileName(loopAdapted.name) + ".anim";
            var outroPath = folder + "/" + SanitizeFileName(outroAdapted.name) + ".anim";

            patch.generatedIntroClip = WriteClipAsset(introPath, introAdapted);
            patch.generatedLoopClip = WriteClipAsset(loopPath, loopAdapted);
            patch.generatedOutroClip = WriteClipAsset(outroPath, outroAdapted);
            patch.generatedSignature = signature;

            Debug.Log($"[AfkMotionPatch] Materialized adapted AFK clips avatar={avatarRoot.name}, patch={GetPath(patch.transform)}", patch);

            EditorUtility.SetDirty(patch);
            EditorSceneManager.MarkSceneDirty(avatarRoot.scene);
            AssetDatabase.SaveAssets();
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

        private static bool RequiredClipsPresent(AfkMotionPatch patch)
        {
            return patch != null
                && patch.targetIntroMotion != null
                && patch.targetLoopMotion != null
                && patch.targetOutroMotion != null
                && patch.replacementIntroSource != null
                && patch.replacementLoopSource != null
                && patch.replacementOutroSource != null;
        }

        private static void ScheduleLoadedAvatars()
        {
            if (EditorApplication.isPlaying) return;
            foreach (var descriptor in Resources.FindObjectsOfTypeAll<VRCAvatarDescriptor>())
            {
                if (descriptor == null) continue;
                var avatarRoot = descriptor.gameObject;
                if (!avatarRoot.scene.IsValid() || !avatarRoot.scene.isLoaded) continue;
                if (avatarRoot.GetComponentInChildren<AfkMotionPatch>(true) == null) continue;
                Schedule(avatarRoot);
            }
        }

        private static void ProcessPending()
        {
            processQueued = false;
            if (EditorApplication.isPlaying)
            {
                PendingAvatarIds.Clear();
                return;
            }
            if (isProcessing) return;

            var pending = PendingAvatarIds.ToArray();
            PendingAvatarIds.Clear();
            isProcessing = true;
            try
            {
                foreach (var instanceId in pending)
                {
                    var avatarRoot = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                    if (avatarRoot != null) SyncAvatar(avatarRoot);
                }
            }
            finally
            {
                isProcessing = false;
                if (PendingAvatarIds.Count > 0 && !processQueued)
                {
                    processQueued = true;
                    EditorApplication.delayCall += ProcessPending;
                }
            }
        }

        private static AnimationClip WriteClipAsset(string assetPath, AnimationClip clip)
        {
            if (clip == null) return null;
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath) != null) AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(clip, assetPath);
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
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

        private static string ComputeSignature(GameObject avatarRoot, AfkMotionPatch patch)
        {
            var payload = string.Join("\n", new[]
            {
                GeneratorVersion,
                GetAssetFingerprint(patch.targetIntroMotion),
                GetAssetFingerprint(patch.targetLoopMotion),
                GetAssetFingerprint(patch.targetOutroMotion),
                GetAssetFingerprint(patch.replacementIntroSource),
                GetAssetFingerprint(patch.replacementLoopSource),
                GetAssetFingerprint(patch.replacementOutroSource),
                string.Join("|", patch.rendererPathRemaps.Select(r => (r?.fromPath ?? string.Empty) + "=>" + (r?.toPath ?? string.Empty))),
                string.Join("|", patch.blendShapeNameRemaps.Select(r => (r?.fromName ?? string.Empty) + "=>" + (r?.toName ?? string.Empty))),
                GetPath(patch.transform),
                ComputeHierarchyFingerprint(avatarRoot)
            });
            return Hash128.Compute(payload).ToString();
        }

        private static string GetAssetFingerprint(UnityEngine.Object asset)
        {
            if (asset == null) return "NULL";
            var path = AssetDatabase.GetAssetPath(asset);
            var dependencyHash = string.IsNullOrEmpty(path) ? "no-path" : AssetDatabase.GetAssetDependencyHash(path).ToString();
            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(asset).ToString();
            return string.Join("|", asset.name, path, dependencyHash, globalId);
        }

        private static string ComputeHierarchyFingerprint(GameObject avatarRoot)
        {
            var builder = new StringBuilder();
            foreach (var transform in avatarRoot.GetComponentsInChildren<Transform>(true))
            {
                builder.AppendLine(GetPath(transform));
            }

            foreach (var renderer in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderBy(r => GetPath(r.transform)))
            {
                builder.Append(GetPath(renderer.transform)).Append(':');
                var mesh = renderer.sharedMesh;
                if (mesh != null)
                {
                    for (var i = 0; i < mesh.blendShapeCount; i++)
                    {
                        builder.Append(mesh.GetBlendShapeName(i)).Append('|');
                    }
                }
                builder.AppendLine();
            }

            return Hash128.Compute(builder.ToString()).ToString();
        }

        private static string BuildAssetKey(GameObject avatarRoot, AfkMotionPatch patch)
        {
            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(patch).ToString();
            var hash = Hash128.Compute(globalId).ToString().Substring(0, 12);
            return SanitizeFileName(avatarRoot.name) + "_" + hash;
        }

        private static string SanitizeFileName(string name)
        {
            var chars = name.Select(ch => Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), ch) >= 0 ? '_' : ch).ToArray();
            return new string(chars);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
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
