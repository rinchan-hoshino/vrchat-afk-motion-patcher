using System;
using System.Collections.Generic;
using UnityEngine;

namespace RinChan.AfkMotionPatcher
{
    [DisallowMultipleComponent]
    [AddComponentMenu("RinChan/AFK Motion Patch")]
    public sealed class AfkMotionPatch : MonoBehaviour
    {
        [Serializable]
        public sealed class RendererPathRemap
        {
            public string fromPath = "Body";
            public string toPath = "Body";
        }

        [Serializable]
        public sealed class BlendShapeNameRemap
        {
            public string fromName;
            public string toName;
        }

        [Header("Target Action Motions")]
        public AnimationClip targetIntroMotion;
        public AnimationClip targetLoopMotion;
        public AnimationClip targetOutroMotion;

        [Header("Replacement Source Motions")]
        public AnimationClip replacementIntroSource;
        public AnimationClip replacementLoopSource;
        public AnimationClip replacementOutroSource;

        [Header("Retargeting")]
        public List<RendererPathRemap> rendererPathRemaps = new List<RendererPathRemap>();
        public List<BlendShapeNameRemap> blendShapeNameRemaps = new List<BlendShapeNameRemap>();
        public bool dropMissingBlendShapes = true;

        [Header("Build")]
        public bool patchActionLayer = true;
        public bool failOnMissingSource = true;
    }
}
