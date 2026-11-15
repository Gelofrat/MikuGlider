using System;
using System.Linq;
using MikuGlider;
using Pigeon.Movement;
using UnityEngine;

namespace Horse.MikuPhysicsLab
{
    internal sealed class LabActor : IDisposable
    {
        internal readonly GameObject Root;
        internal readonly LabRig Rig;
        internal readonly SkinnedMeshRenderer Body;
        internal readonly Player Owner = new Player();
        internal readonly RegisteredSkin Skin;
        internal readonly SkirtGarmentVisual Garment;
        internal readonly bool Arms;
        internal LabMetrics Metrics;
        private readonly Material[] materials;
        private readonly System.Collections.Generic.List<Material> inspectionMaterials = new System.Collections.Generic.List<Material>();
        private bool hat;
        private readonly LabMotionPlayer motion;
        private readonly LabWeaponVisual weapon;
        internal string WeaponKey => weapon?.WeaponKey;
        internal int WeaponMeshCount => weapon?.MeshCount ?? 0;
        private LabCombinationPlayer combination;
        private bool disposed;

        internal LabActor(LabVariant variant, bool arms, bool applyWristVisuals = true)
        {
            Arms = arms;
            Root = new GameObject(variant.label);
            if (arms)
                Root.transform.position = Vector3.up;
            var assets = LabAssetLibrary.Get(variant.key);
            Skin = new RegisteredSkin
            {
                Assets = assets,
                Definition = new SkinVariantDefinition { Key = variant.key, DisplayName = variant.label,
                    CorrectFirstPersonLeftWrist = true,
                    CorrectFirstPersonRightWrist = variant.key == "v2" }
            };
            materials = new[] { MakeMaterial(variant.key, "face-albedo"), MakeMaterial(variant.key, "body-albedo") };
            Rig = new LabRig(arms ? "arms-rig" : "body-rig", Root.transform);
            if (!arms && LabController.Argument("-labLateBind") != null)
            {
                Root.transform.SetPositionAndRotation(new Vector3(25, 27, -115), Quaternion.Euler(0, 123, 0));
                Root.transform.localScale = Vector3.one * 1.15f;
            }
            motion = new LabMotionPlayer(Rig);
            if (arms)
            {
                weapon = new LabWeaponVisual(Rig);
                for (int index = 0; index < Rig.Data.renderers.Length; index++)
                {
                    RigRenderer r = Rig.Data.renderers[index];
                    Mesh arm = assets.GetMesh(r.name.ToLowerInvariant().Contains("left") ? "arm-left" : "arm-right");
                    var renderer = Render(r, arm, new[] { materials[1] });
                    bool correctWrist = r.name.ToLowerInvariant().Contains("left")
                        ? Skin.Definition.CorrectFirstPersonLeftWrist : Skin.Definition.CorrectFirstPersonRightWrist;
                    FirstPersonWristVisual.Attach(renderer, applyWristVisuals && correctWrist && LabController.Argument("-labNativeWrists") == null ? arm : null, assets.ModelDefinition);
                    ModelPoseCorrections.Attach(renderer, assets.Corrections, Rig.Nodes.First(node => node.name == "cc_WeaponRoot"), "Native weapon ");
                    string closeupSide = LabController.Argument("-labWristCloseup");
                    if (closeupSide != null)
                        renderer.forceRenderingOff = r.name.ToLowerInvariant().Contains("left") !=
                            closeupSide.Equals("L", StringComparison.OrdinalIgnoreCase);
                }
                Body = Rig.Nodes[Rig.Data.renderers[0].node].GetComponent<SkinnedMeshRenderer>();
            }
            else
            {
                Body = Render(Rig.Data.renderers[0], assets.GetMesh("body"), materials);
                SkirtGarmentVisual.Attach(Root, Body, 1, Skin, Owner);
                Garment = Root.GetComponent<SkirtGarmentVisual>();
                Transform head = Rig.Bones.First(b => b.name == "joint_Head");
                AnimatedHairVisual.Attach(Root, Body, head, 1, Skin, Owner);
                ModelPoseCorrections.Attach(Body, assets.Corrections);
                if (LabController.Argument("-labNativeGarment") != null)
                    foreach (SkinnedMeshRenderer renderer in Root.GetComponentsInChildren<SkinnedMeshRenderer>())
                        if (renderer.gameObject.name.EndsWith(" coat flaps", StringComparison.Ordinal))
                        {
                            renderer.sharedMesh = assets.GetMesh("coat-flaps");
                            renderer.bones = Body.bones;
                        }
                if (LabController.Argument("-labGarmentDebug") != null)
                    ColorGarments();
                if (LabController.Argument("-labCullGarment") != null)
                    foreach (SkinnedMeshRenderer renderer in Root.GetComponentsInChildren<SkinnedMeshRenderer>())
                        if (renderer.gameObject.name.EndsWith(" coat flaps", StringComparison.Ordinal))
                        {
                            var material = new Material(renderer.sharedMaterial);
                            material.SetFloat("_Cull", 1);
                            renderer.sharedMaterial = material;
                            inspectionMaterials.Add(material);
                        }
                Metrics = Root.AddComponent<LabMetrics>();
                Metrics.Bind(this);
            }
        }

        private void ColorGarments()
        {
            foreach (SkinnedMeshRenderer renderer in Root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                bool coat = renderer.gameObject.name.EndsWith(" coat flaps", StringComparison.Ordinal);
                bool skirt = renderer == Garment.SurfaceRenderer;
                if (!coat && !skirt)
                    continue;
                var material = new Material(renderer.sharedMaterial);
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
                material.SetColor("_Color", coat ? Color.red : Color.yellow);
                renderer.sharedMaterial = material;
                inspectionMaterials.Add(material);
            }
            foreach (Material material in materials)
            {
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
                material.SetColor("_Color", Color.cyan);
            }
        }

        private SkinnedMeshRenderer Render(RigRenderer source, Mesh mesh, Material[] colors)
        {
            var renderer = Rig.Nodes[source.node].gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = source.bones.Select(i => Rig.Nodes[i]).ToArray();
            renderer.rootBone = Rig.Nodes[source.root];
            renderer.sharedMaterials = colors;
            renderer.quality = (SkinQuality)source.quality;
            renderer.updateWhenOffscreen = true;
            renderer.localBounds = new Bounds(new Vector3(0, 1, 0), new Vector3(5, 5, 5));
            return renderer;
        }

        internal void SetHat(bool value)
        {
            if (Arms || hat == value)
                return;
            hat = value;
            Body.sharedMesh = Skin.Assets.GetMesh(value ? "body-hat" : "body");
        }

        internal void Sample(AnimationClip clip, float time, bool flying, float bend = 0f)
        {
            Rig.Restore();
            Owner.Wingsuit.IsFlying = flying;
            if (!Arms)
            {
                if (clip != null)
                    clip.SampleAnimation(Rig.AnimationRoot, time);
                return;
            }
            foreach (Transform bone in Rig.Nodes)
            {
                if (bone.name.EndsWith("lowerArm_01", StringComparison.Ordinal))
                {
                    Vector3 axis = bone.InverseTransformDirection(Root.transform.up);
                    float sign = bone.name.Contains("_L_") ? 1f : -1f;
                    bone.localRotation *= Quaternion.AngleAxis(bend * sign, axis);
                }
            }
        }

        internal void Animate(ClipEntry entry, AnimationClip clip, AnimationClip idle, float baseSpeed, float time, bool flying, bool advancing)
        {
            weapon?.Select(entry);
            Owner.Wingsuit.IsFlying = flying;
            float progress = time * entry.speed;
            float clipTime = entry.loop ? Mathf.Repeat(progress, clip.length) : Mathf.Clamp(progress, 0, clip.length);
            float alpha = entry.fadeDuration > 0 ? Mathf.Clamp01(time / entry.fadeDuration) : 1;
            alpha = alpha < .5f ? 2 * alpha * alpha : 1 - Mathf.Pow(-2 * alpha + 2, 2) / 2;
            if (LabController.Argument("-labDriver") == "sample")
            {
                Rig.Restore();
                clip.SampleAnimation(Rig.AnimationRoot, clipTime);
                return;
            }
            motion.Sample(clip, idle, clipTime, idle != null ? Mathf.Repeat(time * baseSpeed, idle.length) : 0, time, alpha, entry.overlay, entry.speed, baseSpeed, advancing, entry.additive);
        }

        internal void AnimateCombination(LabController lab, ClipEntry entry, float time)
        {
            if (combination == null)
            {
                motion.Dispose();
                combination = new LabCombinationPlayer(Rig, lab, entry.segments);
            }
            Owner.Wingsuit.IsFlying = combination.Sample(time);
        }

        internal float AnimationCoverage => motion.Coverage;

        internal void RecreateGarment()
        {
            if (Arms)
                return;
            var renderer = typeof(SkirtGarmentVisual).GetMethod("CreateRenderer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Mesh previousMesh = Garment.SurfaceRenderer.sharedMesh;
            renderer.Invoke(Garment, null);
            if (Garment.SurfaceRenderer.sharedMesh != previousMesh)
                throw new InvalidOperationException("Repeated garment binding did not reuse its mesh.");
            Debug.Log("LAB_GARMENT_CACHE_REUSED " + Skin.Definition.Key + " " + Garment.InitializationMilliseconds.ToString("F3") + " ms");
            UnityEngine.Object.Destroy(Metrics);
            Metrics = Root.AddComponent<LabMetrics>();
            Metrics.Bind(this);
            Metrics.Deterministic = true;
        }

        internal Vector3 Focus
        {
            get
            {
                if (!Arms)
                    return Rig.Bones.First(b => b.name == "joint_Pelvis").position - Vector3.up * .2f;
                Transform[] hands = Rig.Nodes.Where(b => b.name.EndsWith("_hand_01", StringComparison.Ordinal)).ToArray();
                return hands.Aggregate(Vector3.zero, (position, bone) => position + bone.position) / hands.Length;
            }
        }

        internal Transform[] HairBones => Arms ? Array.Empty<Transform>() : Root.GetComponentsInChildren<SkinnedMeshRenderer>()
            .Where(renderer => renderer.sharedMesh == Skin.Assets.GetMesh("hair-l") || renderer.sharedMesh == Skin.Assets.GetMesh("hair-r"))
            .SelectMany(renderer => renderer.bones).Distinct().ToArray();

        internal static Material MakeMaterial(string folder, string texture)
        {
            Shader shader = Resources.Load<Shader>("MikuLabAlbedo");
            var material = new Material(shader) { name = folder + " " + texture };
            Texture2D image = Resources.Load<Texture2D>("MikuPhysicsLab/" + folder + "/" + texture);
            material.mainTexture = image;
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", image);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            return material;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (Root != null)
                Root.SetActive(false);
            motion.Dispose();
            combination?.Dispose();
            weapon?.Dispose();
            if (Root != null)
                UnityEngine.Object.Destroy(Root);
            foreach (Material material in materials)
                UnityEngine.Object.Destroy(material);
            foreach (Material material in inspectionMaterials)
                UnityEngine.Object.Destroy(material);
        }
    }
}
