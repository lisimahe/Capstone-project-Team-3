using System.Collections.Generic;
using UnityEngine;

namespace MissionBit
{
    /// <summary>
    /// Applies the performant <c>MissionBit/DancingCrowd</c> GPU vertex-dance shader to
    /// crowd renderers and optionally disables skeletal <see cref="Animator"/>s
    /// (Animators are expensive on Quest when many characters are on screen).
    ///
    /// Usage:
    ///   1. Put this on a crowd parent (or each person).
    ///   2. Assign <see cref="danceMaterial"/> (create one using MissionBit/DancingCrowd),
    ///      or leave null to auto-create a runtime material from the shader.
    ///   3. Press Play — textures/colors are copied from the original materials.
    /// </summary>
    [DisallowMultipleComponent]
    public class DancingCrowdSetup : MonoBehaviour
    {
        [Header("Material")]
        [SerializeField] private Material danceMaterial;
        [Tooltip("Shared template material (Assets/[Art]/DancingCrowd). Instances still get unique phase seeds.")]
        [SerializeField] private bool uniquePhasePerRenderer = true;

        [Header("Dance Defaults (applied when creating / overriding)")]
        [SerializeField] private float danceSpeed = 2.2f;
        [SerializeField] private float swayAmount = 0.08f;
        [SerializeField] private float bobAmount = 0.05f;
        [SerializeField] private float twistAmount = 0.12f;

        [Header("Performance")]
        [Tooltip("Disable Animator components under this object (recommended on headset).")]
        [SerializeField] private bool disableAnimators = true;
        [Tooltip("Disable Animation (legacy) components too.")]
        [SerializeField] private bool disableLegacyAnimation = true;
        [SerializeField] private bool includeInactive = true;
        [SerializeField] private bool applyOnAwake = true;

        [Header("Filter")]
        [Tooltip("Only affect renderers whose material name contains this (empty = all).")]
        [SerializeField] private string materialNameContains = "";

        private static readonly int DanceSpeedId = Shader.PropertyToID("_DanceSpeed");
        private static readonly int SwayId = Shader.PropertyToID("_SwayAmount");
        private static readonly int BobId = Shader.PropertyToID("_BobAmount");
        private static readonly int TwistId = Shader.PropertyToID("_TwistAmount");
        private static readonly int SeedId = Shader.PropertyToID("_RandomSeed");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly List<Material> _spawned = new List<Material>(64);
        private bool _applied;

        private void Awake()
        {
            if (applyOnAwake)
                Apply();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                    Destroy(_spawned[i]);
            }
            _spawned.Clear();
        }

        [ContextMenu("Apply Dancing Crowd")]
        public void Apply()
        {
            if (_applied) return;

            Shader shader = Shader.Find("MissionBit/DancingCrowd");
            if (shader == null)
            {
                Debug.LogError("[DancingCrowdSetup] Shader 'MissionBit/DancingCrowd' not found. Is the .shader imported?", this);
                return;
            }

            if (danceMaterial == null)
            {
                danceMaterial = new Material(shader) { name = "DancingCrowd (Runtime)" };
                _spawned.Add(danceMaterial);
            }
            else if (danceMaterial.shader != shader)
            {
                Debug.LogWarning("[DancingCrowdSetup] Assigned material is not using MissionBit/DancingCrowd — creating instances from that shader anyway.", this);
            }

            ApplyDanceParams(danceMaterial);

            if (disableAnimators)
            {
                var animators = GetComponentsInChildren<Animator>(includeInactive);
                for (int i = 0; i < animators.Length; i++)
                    animators[i].enabled = false;
            }

            if (disableLegacyAnimation)
            {
                var anims = GetComponentsInChildren<Animation>(includeInactive);
                for (int i = 0; i < anims.Length; i++)
                    anims[i].enabled = false;
            }

            var renderers = GetComponentsInChildren<Renderer>(includeInactive);
            int converted = 0;

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;

                var shared = r.sharedMaterials;
                if (shared == null || shared.Length == 0) continue;

                var next = new Material[shared.Length];
                bool changed = false;

                for (int m = 0; m < shared.Length; m++)
                {
                    Material src = shared[m];
                    if (src == null)
                    {
                        next[m] = null;
                        continue;
                    }

                    if (!string.IsNullOrEmpty(materialNameContains)
                        && src.name.IndexOf(materialNameContains, System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        next[m] = src;
                        continue;
                    }

                    Material inst = new Material(shader);
                    inst.name = src.name + " (Dance)";
                    CopyMapsAndColor(src, inst);
                    ApplyDanceParams(inst);

                    if (uniquePhasePerRenderer)
                    {
                        float seed = Hash(r.GetInstanceID() * 73856093 ^ m * 19349663)
                                     + r.transform.position.x * 0.17f
                                     + r.transform.position.z * 0.31f;
                        inst.SetFloat(SeedId, seed * 6.2831853f);
                    }

                    // Prefer GPU instancing when possible.
                    inst.enableInstancing = true;

                    _spawned.Add(inst);
                    next[m] = inst;
                    changed = true;
                }

                if (changed)
                {
                    r.sharedMaterials = next;
                    converted++;
                }
            }

            _applied = true;
            Debug.Log($"[DancingCrowdSetup] Converted {converted} renderer(s) under '{name}'. Animators disabled={disableAnimators}.", this);
        }

        private void ApplyDanceParams(Material mat)
        {
            if (mat == null) return;
            if (mat.HasProperty(DanceSpeedId)) mat.SetFloat(DanceSpeedId, danceSpeed);
            if (mat.HasProperty(SwayId)) mat.SetFloat(SwayId, swayAmount);
            if (mat.HasProperty(BobId)) mat.SetFloat(BobId, bobAmount);
            if (mat.HasProperty(TwistId)) mat.SetFloat(TwistId, twistAmount);
        }

        private static void CopyMapsAndColor(Material src, Material dst)
        {
            Texture tex = null;
            if (src.HasProperty(BaseMapId)) tex = src.GetTexture(BaseMapId);
            if (tex == null && src.HasProperty(MainTexId)) tex = src.GetTexture(MainTexId);
            if (tex == null) tex = src.mainTexture;

            if (tex != null)
            {
                if (dst.HasProperty(BaseMapId)) dst.SetTexture(BaseMapId, tex);
                if (dst.HasProperty(MainTexId)) dst.SetTexture(MainTexId, tex);
                dst.mainTexture = tex;
            }

            Color color = Color.white;
            if (src.HasProperty(BaseColorId)) color = src.GetColor(BaseColorId);
            else if (src.HasProperty(ColorId)) color = src.GetColor(ColorId);
            else color = src.color;

            if (dst.HasProperty(BaseColorId)) dst.SetColor(BaseColorId, color);
            if (dst.HasProperty(ColorId)) dst.SetColor(ColorId, color);
        }

        private static float Hash(int x)
        {
            unchecked
            {
                uint u = (uint)x;
                u ^= 2747636419u;
                u *= 2654435769u;
                u ^= u >> 16;
                u *= 2654435769u;
                u ^= u >> 16;
                u *= 2654435769u;
                return (u & 0x00FFFFFFu) / 16777215f;
            }
        }
    }
}
