using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace MikuGlider;

[DefaultExecutionOrder(1400)]
internal sealed class ModelPoseCorrections : MonoBehaviour
{
    private static readonly ConditionalWeakTable<SkinnedMeshRenderer, RejectedBinding> Rejected =
        new ConditionalWeakTable<SkinnedMeshRenderer, RejectedBinding>();
    private SkinnedMeshRenderer surface;
    private ModelCorrectionMeshes.AppliedMesh state;
    private ModelCorrections profile;
    private Transform[] bones;
    private float[] activations;
    private float[] handleContacts;
    private float[] appliedWeights;
    private int[] poseGroups;
    private float[] groupTotals;
    private Transform weaponRoot;
    private string weaponNamePrefix = "";
    private Transform[] weaponChildren = Array.Empty<Transform>();
    private string[] weaponNames = Array.Empty<string>();
    private Transform[] gripBones;
    private Transform[] renderedBones;
    private Quaternion[] gripOffsets;
    private bool gripBound;
    private Transform[] anchorTargets;
    private bool anchorsDirty = true;
    private FirstPersonWristVisual wrist;
    private RendererBoneBinding binding;

    internal bool UpdatesWrist => enabled && state != null && surface != null && GetApplied(surface.sharedMesh) == state &&
        gripBones != null && gripBones.Length > 3 && gripBones[3] != null;

    // Animation and capture code must read these, not feed the already-corrected
    // renderer copies back into the pose drivers on the next frame.
    internal Transform[] SourceBones => bones;

    internal static ModelCorrectionMeshes.AppliedMesh GetApplied(Mesh mesh) => ModelCorrectionMeshes.GetApplied(mesh);

    internal static void PrepareMeshChange(SkinnedMeshRenderer renderer)
    {
        if (renderer == null) return;
        RendererBoneBinding.ReleaseSeated(renderer);
        renderer.GetComponent<ModelPoseCorrections>()?.ResetShapes();
        FirstPersonWristVisual.Attach(renderer, null);
    }

    internal static void Attach(SkinnedMeshRenderer renderer, ModelCorrections profile, Transform weaponRoot = null, string weaponNamePrefix = "")
    {
        if (renderer == null) return;
        RendererBoneBinding.ReleaseSeated(renderer);
        if (renderer.sharedMesh == null || profile == null || profile.meshes.Length == 0)
        {
            renderer.GetComponent<ModelPoseCorrections>()?.ResetShapes();
            return;
        }
        Mesh candidate = renderer.sharedMesh;
        if (Rejected.TryGetValue(renderer, out var failure) && failure.Matches(candidate, profile, renderer.bones)) return;
        try
        {
            AttachValidated(renderer, profile);
            var component = renderer.GetComponent<ModelPoseCorrections>();
            if (failure != null && component != null && component.state != null) Rejected.Remove(renderer);
            if (component != null && (component.weaponRoot != weaponRoot || component.weaponNamePrefix != weaponNamePrefix))
            {
                component.weaponRoot = weaponRoot;
                component.weaponNamePrefix = weaponNamePrefix;
                component.weaponChildren = Array.Empty<Transform>();
                component.weaponNames = Array.Empty<string>();
                component.anchorsDirty = true;
            }
        }
        catch (Exception error) when (error is InvalidDataException || error is InvalidOperationException)
        {
            renderer.GetComponent<ModelPoseCorrections>()?.ResetShapes();
            Rejected.Remove(renderer);
            Rejected.Add(renderer, new RejectedBinding(candidate, profile, renderer.bones));
            Debug.LogWarning("Miku Glider skipped incompatible manual corrections for " + candidate.name + ": " + error.Message);
        }
    }

    private sealed class RejectedBinding
    {
        private readonly Mesh mesh;
        private readonly ModelCorrections profile;
        private readonly Transform[] bones;
        private readonly string[] names;
        internal RejectedBinding(Mesh mesh, ModelCorrections profile, Transform[] bones)
        {
            this.mesh = mesh; this.profile = profile; this.bones = bones;
            names = Array.ConvertAll(bones, bone => bone != null ? bone.name : null);
        }
        internal bool Matches(Mesh mesh, ModelCorrections profile, Transform[] current)
        {
            if (this.mesh != mesh || this.profile != profile || bones.Length != current.Length) return false;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != current[i] || names[i] != (current[i] != null ? current[i].name : null)) return false;
            return true;
        }
    }

    private static void AttachValidated(SkinnedMeshRenderer renderer, ModelCorrections profile)
    {
        var previous = renderer.GetComponent<ModelPoseCorrections>();
        Mesh mesh = renderer.sharedMesh;
        var known = GetApplied(mesh);
        if (previous != null && previous.state != null && previous.surface.sharedMesh == mesh &&
            known == previous.state && previous.profile == profile && previous.MatchesSource(renderer.bones))
        {
            previous.BindGripBones(true);
            previous.enabled = known.Shapes.Length > 0;
            return;
        }
        MeshCorrection correction = profile.Find(mesh);
        if (correction == null)
        {
            if (previous != null) previous.ResetShapes();
            return;
        }
        if (previous != null) previous.ReleaseGripBones();
        if (!ModelCorrectionMeshes.Validate(mesh, correction)) return;
        Transform[] targets = renderer.bones;
        if (targets.Length < mesh.bindposes.Length)
            throw new InvalidOperationException("Correction skeleton has fewer bones than its mesh: " + correction.key);
        foreach (PoseShapeEdit pose in correction.poses)
        {
            if (pose.gripAnchor != null && (targets.Length <= 3 || targets[3] == null || targets[3].name != pose.gripAnchor.name))
                throw new InvalidOperationException("Grip anchor skeleton differs: " + correction.key);
            if (pose.gripAnchor?.followArm == true && targets[0] == null)
                throw new InvalidOperationException("Grip anchor requires an arm root: " + correction.key);
            foreach (PoseShapeDriver driver in pose.drivers)
                if (driver == null || driver.bone < 0 || driver.bone >= targets.Length || targets[driver.bone] == null || targets[driver.bone].name != driver.name)
                    throw new InvalidOperationException("Pose correction skeleton differs: " + correction.key);
            foreach (PoseBoneRotation edit in pose.boneRotations)
                if (edit.bone < 0 || edit.bone >= targets.Length || targets[edit.bone] == null || targets[edit.bone].name != edit.name)
                    throw new InvalidOperationException("Finger correction skeleton differs: " + correction.key);
        }
        var applied = ModelCorrectionMeshes.Prepare(mesh, correction);
        var component = previous ?? renderer.gameObject.AddComponent<ModelPoseCorrections>();
        component.surface = renderer;
        component.state = applied;
        component.profile = profile;
        component.bones = renderer.bones;
        component.wrist = renderer.GetComponent<FirstPersonWristVisual>();
        if (component.wrist != null) component.wrist.PoseOwner = component;
        component.anchorsDirty = true;
        component.CreateGripBones();
        component.activations = new float[applied.Shapes.Length];
        component.handleContacts = new float[applied.Shapes.Length];
        component.appliedWeights = new float[applied.Shapes.Length];
        component.poseGroups = new int[applied.Shapes.Length];
        var groups = new Dictionary<string, int>();
        for (int i = 0; i < applied.Shapes.Length; i++)
        {
            string key = correction.poses[i].group ?? "";
            if (!groups.TryGetValue(key, out int group)) groups.Add(key, group = groups.Count);
            component.poseGroups[i] = group;
        }
        component.groupTotals = new float[groups.Count];
        for (int i = 0; i < component.appliedWeights.Length; i++) component.appliedWeights[i] = float.NaN;
        component.enabled = applied.Shapes.Length > 0;
    }

    private bool MatchesSource(Transform[] current)
    {
        // A shared mesh/profile does not identify a skeleton instance. Only reuse
        // copies while this renderer still refers to those copies or their source.
        return RendererBoneBinding.SameBones(current, renderedBones) || RendererBoneBinding.SameBones(current, bones);
    }

    private void CreateGripBones()
    {
        var affected = new bool[bones.Length];
        bool any = false;
        foreach (PoseShapeEdit pose in state.Correction.poses)
        {
            foreach (PoseBoneRotation edit in pose.boneRotations) { affected[edit.bone] = true; any = true; }
            if (pose.gripAnchor != null && !pose.gripAnchor.weightOnly)
            {
                affected[pose.gripAnchor.followArm ? 0 : pose.gripAnchor.bone] = true;
                any = true;
            }
        }
        if (!any) return;
        // Include descendants so changing a knuckle moves the following joints
        // while retaining their native local translations and segment lengths.
        for (int pass = 0; pass < bones.Length; pass++)
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null || affected[i]) continue;
                int parent = Array.IndexOf(bones, bones[i].parent);
                if (parent >= 0 && affected[parent]) affected[i] = true;
            }
        gripBones = new Transform[bones.Length];
        gripOffsets = new Quaternion[bones.Length];
        renderedBones = (Transform[])bones.Clone();
        for (int i = 0; i < bones.Length; i++)
            if (affected[i])
            {
                var copy = new GameObject("Miku grip " + bones[i].name).transform;
                copy.gameObject.hideFlags = HideFlags.DontSave;
                gripBones[i] = renderedBones[i] = copy;
            }
        for (int i = 0; i < bones.Length; i++)
            if (gripBones[i] != null)
            {
                int parent = Array.IndexOf(bones, bones[i].parent);
                gripBones[i].SetParent(parent >= 0 && gripBones[parent] != null ? gripBones[parent] : bones[i].parent, false);
                CopyGripBone(i, Quaternion.identity);
            }
        BindGripBones();
    }

    private void BindGripBones(bool force = false)
    {
        if (surface != null && renderedBones != null && (force || !gripBound))
        {
            binding = RendererBoneBinding.For(surface);
            binding.SetGrip(renderedBones);
            gripBound = true;
        }
    }

    private void CopyGripBone(int i, Quaternion offset)
    {
        gripBones[i].localPosition = bones[i].localPosition;
        gripBones[i].localRotation = bones[i].localRotation * offset;
        gripBones[i].localScale = bones[i].localScale;
    }

    private float HandleContact(PoseGripAnchor anchor, Transform target)
    {
        if (target == null || !target.gameObject.activeInHierarchy) return 0;
        Transform palm = bones[anchor.bone];
        float distance = Vector3.Distance(palm.position, target.TransformPoint(anchor.contactPosition ?? anchor.position))
            / Mathf.Max(.0001f, palm.lossyScale.x);
        float t = Mathf.InverseLerp(anchor.innerDistance, anchor.outerDistance, distance);
        return 1f - t * t * (3f - 2f * t);
    }

    private void UpdateGripBones()
    {
        if (gripBones == null) return;
        for (int i = 0; i < gripOffsets.Length; i++) gripOffsets[i] = Quaternion.identity;
        for (int p = 0; p < activations.Length; p++)
            if (activations[p] > 0)
                foreach (PoseBoneRotation edit in state.Correction.poses[p].boneRotations)
                {
                    Quaternion rotation = edit.contactRotation.HasValue
                        ? Quaternion.Slerp(edit.rotation, edit.contactRotation.Value, handleContacts[p]) : edit.rotation;
                    Quaternion offset = edit.absolute ? Quaternion.Inverse(bones[edit.bone].localRotation) * rotation : rotation;
                    if (edit.motionReference.HasValue)
                    {
                        Quaternion source = bones[edit.bone].localRotation;
                        Quaternion reference = edit.motionReference.Value;
                        // Small source variations come from locomotion while the fingers stay planted.
                        float t = Mathf.InverseLerp(.5f, 2f, Quaternion.Angle(reference, source));
                        float motion = edit.motionFraction * t * t * (3f - 2f * t);
                        Quaternion animated = Quaternion.Slerp(reference, source, motion);
                        offset = Quaternion.Inverse(source) * animated * rotation;
                    }
                    gripOffsets[edit.bone] *= Quaternion.Slerp(Quaternion.identity, offset,
                        activations[p] / Mathf.Max(1, groupTotals[poseGroups[p]]));
                }
        for (int i = 0; i < gripBones.Length; i++)
            if (gripBones[i] != null) CopyGripBone(i, gripOffsets[i]);
        for (int p = 0; p < activations.Length; p++)
            if (activations[p] > 0)
                foreach (PoseBoneRotation edit in state.Correction.poses[p].boneRotations)
                    if (edit.position.HasValue)
                        gripBones[edit.bone].localPosition += (edit.position.Value - bones[edit.bone].localPosition)
                            * (activations[p] / Mathf.Max(1, groupTotals[poseGroups[p]]));
        for (int p = 0; p < activations.Length; p++)
        {
            var anchor = state.Correction.poses[p].gripAnchor;
            if (anchor == null || anchor.weightOnly || anchorTargets == null || anchorTargets[p] == null || activations[p] <= 0) continue;
            Transform source = bones[anchor.bone], target = anchorTargets[p], copy = gripBones[anchor.bone];
            Vector3 position = target.TransformPoint(anchor.position);
            float weight = activations[p] / Mathf.Max(1, groupTotals[poseGroups[p]]);
            if (anchor.followArm)
            {
                Quaternion rotation = Quaternion.Slerp(copy.rotation, target.rotation * anchor.rotation, weight);
                Transform root = gripBones[0];
                if (anchor.rotateAtWrist)
                {
                    root.position += weight * (position - copy.position);
                    copy.rotation = rotation;
                    continue;
                }
                Quaternion delta = rotation * Quaternion.Inverse(copy.rotation);
                Vector3 translation = Vector3.Lerp(copy.position, position, weight) - delta * copy.position;
                root.SetPositionAndRotation(delta * root.position + translation, delta * root.rotation);
                continue;
            }
            weight *= handleContacts[p];
            copy.SetPositionAndRotation(Vector3.Lerp(source.position, position, weight),
                Quaternion.Slerp(source.rotation, target.rotation * anchor.rotation, weight));
        }
        if (wrist != null && gripBones.Length > 3 && gripBones[3] != null)
            wrist.UpdatePose(gripBones[3], gripBones[1]);
        // Reattach after OnDisable, or an ordinary renderer rebind. The native
        // animation transforms themselves are never modified.
        BindGripBones();
    }

    private void RestoreSourceBones()
    {
        gripBound = false;
        binding?.ReleaseGrip();
        binding = null;
        if (wrist != null) wrist.UpdatePose();
    }

    private void ReleaseGripBones()
    {
        RestoreSourceBones();
        if (wrist != null && wrist.PoseOwner == this) wrist.PoseOwner = null;
        if (gripBones != null)
            for (int i = 0; i < gripBones.Length; i++)
                if (gripBones[i] != null)
                {
                    if (Application.isPlaying) Destroy(gripBones[i].gameObject);
                    else DestroyImmediate(gripBones[i].gameObject);
                }
        gripBones = null; renderedBones = null; gripOffsets = null;
        anchorTargets = null; anchorsDirty = true;
    }

    // Bake once at mesh creation. UV splits must not become new shading seams
    // when a corrective bends a finger. Preserve deliberately hard normals.
    internal static Vector3[] CalculateNormalDeltas(Vector3[] basis, Vector3[] target,
        Vector3[] shading, int[] triangles)
    {
        var before = new Vector3[basis.Length];
        var after = new Vector3[basis.Length];
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            Vector3 n = Vector3.Cross(basis[b] - basis[a], basis[c] - basis[a]);
            Vector3 m = Vector3.Cross(target[b] - target[a], target[c] - target[a]);
            before[a] += n; before[b] += n; before[c] += n;
            after[a] += m; after[b] += m; after[c] += m;
        }
        var groups = new Dictionary<Vector3Int, List<int>>();
        for (int i = 0; i < basis.Length; i++)
        {
            Vector3Int key = Vector3Int.RoundToInt(basis[i] * 1000000f);
            if (!groups.TryGetValue(key, out var group)) groups.Add(key, group = new List<int>());
            group.Add(i);
        }
        var result = new Vector3[basis.Length];
        foreach (var group in groups.Values)
            foreach (int i in group)
            {
                Vector3 n = Vector3.zero, m = Vector3.zero;
                foreach (int j in group)
                    if (Vector3.Dot(shading[i], shading[j]) > .999f &&
                        (target[i] - target[j]).sqrMagnitude < 1e-10f)
                    { n += before[j]; m += after[j]; }
                // Normalize explicitly: Unity's Vector3.normalized epsilon is
                // too large for the small triangles in fingertip meshes.
                float nl = n.magnitude, ml = m.magnitude;
                // The imported meshes use clockwise front faces. Match the
                // existing shading orientation instead of assuming winding.
                if (nl > 1e-12f && ml > 1e-12f)
                    result[i] = (Vector3.Dot(n, shading[i]) < 0 ? -1f : 1f) * (m / ml - n / nl);
            }
        return result;
    }

    internal static float Activation(PoseShapeEdit pose, Transform[] bones)
    {
        float weight = 1;
        foreach (PoseShapeDriver driver in pose.drivers)
        {
            if (bones[driver.bone] == null) return 0;
            float angle = Quaternion.Angle(bones[driver.bone].localRotation, driver.rotation);
            float t = Mathf.InverseLerp(driver.innerRadius, driver.radius, angle);
            weight = Mathf.Min(weight, 1 - t * t * (3 - 2 * t));
        }
        return weight;
    }

    private void LateUpdate()
    {
        if (surface == null || state == null || GetApplied(surface.sharedMesh) != state) { enabled = false; return; }
        UpdateWeaponCache();
        Array.Clear(groupTotals, 0, groupTotals.Length);
        for (int i = 0; i < activations.Length; i++)
        {
            PoseShapeEdit pose = state.Correction.poses[i];
            string weapon = string.IsNullOrEmpty(pose.weapon) ? state.Correction.weapon : pose.weapon;
            bool equipped = MatchesWeapon(weapon);
            activations[i] = equipped ? Activation(pose, bones) : 0;
            // Sample the animated handle once for the palm and contact-specific finger rotations.
            var anchor = pose.gripAnchor;
            if (anchor != null && equipped && (activations[i] > 0 || anchor.sustainOnContact))
            {
                // An equipped weapon can recreate its handle under the same weapon root.
                if (weaponRoot != null && anchorTargets[i] == null)
                    anchorTargets[i] = weaponRoot.Find(anchor.path);
                handleContacts[i] = HandleContact(anchor, anchorTargets?[i]);
                if (anchor.sustainOnContact)
                    activations[i] = Mathf.Lerp(activations[i], 1f, handleContacts[i]);
                else if (anchor.followArm || anchor.weightOnly)
                    activations[i] *= anchor.releaseOnly ? 1f - handleContacts[i] : handleContacts[i];
            }
            groupTotals[poseGroups[i]] += activations[i];
        }
        UpdateGripBones();
        for (int i = 0; i < activations.Length; i++)
        {
            float weight = activations[i] * 100f / Mathf.Max(1, groupTotals[poseGroups[i]]);
            if (appliedWeights[i] == weight) continue;
            surface.SetBlendShapeWeight(state.Shapes[i], weight);
            appliedWeights[i] = weight;
        }
    }

    private void UpdateWeaponCache()
    {
        if (weaponRoot == null) return;
        int count = weaponRoot.childCount;
        if (weaponChildren.Length != count)
        {
            weaponChildren = new Transform[count];
            weaponNames = new string[count];
            anchorsDirty = true;
        }
        for (int i = 0; i < count; i++)
        {
            Transform child = weaponRoot.GetChild(i);
            if (weaponChildren[i] != child)
            {
                weaponChildren[i] = child;
                anchorsDirty = true;
                // Names are read only when the weapon model changes, not every frame.
                string name = child.name;
                weaponNames[i] = name.StartsWith(weaponNamePrefix, StringComparison.Ordinal)
                    ? name.Substring(weaponNamePrefix.Length) : name;
            }
        }
        if (anchorsDirty)
        {
            anchorTargets = new Transform[state.Correction.poses.Length];
            for (int i = 0; i < anchorTargets.Length; i++)
            {
                var anchor = state.Correction.poses[i].gripAnchor;
                if (anchor == null) continue;
                anchorTargets[i] = weaponRoot.Find(anchor.path);
                // The lab names extracted meshes explicitly; the game uses
                // the original MagRoot/Handle hierarchy above.
                if (anchorTargets[i] == null && !string.IsNullOrEmpty(weaponNamePrefix))
                {
                    string leaf = anchor.path.Substring(anchor.path.LastIndexOf('/')+1);
                    foreach (var child in weaponRoot.GetComponentsInChildren<Transform>(true))
                        if (child.name == weaponNamePrefix + leaf) { anchorTargets[i] = child; break; }
                }
            }
            anchorsDirty = false;
        }
    }

    private bool MatchesWeapon(string required)
    {
        if (string.IsNullOrEmpty(required)) return true;
        if (weaponRoot == null) return false;
        for (int i = 0; i < weaponChildren.Length; i++)
            if (weaponNames[i] == required && weaponChildren[i].gameObject.activeSelf) return true;
        return false;
    }

    internal void ResetShapes()
    {
        if (surface != null && state != null && GetApplied(surface.sharedMesh) == state)
            foreach (int shape in state.Shapes) surface.SetBlendShapeWeight(shape, 0);
        ReleaseGripBones();
        state = null;
        profile = null;
        enabled = false;
    }

    private void OnDisable()
    {
        RestoreSourceBones();
        if (surface != null && state != null && GetApplied(surface.sharedMesh) == state)
            foreach (int shape in state.Shapes) surface.SetBlendShapeWeight(shape, 0);
        if (appliedWeights != null)
            for (int i = 0; i < appliedWeights.Length; i++) appliedWeights[i] = float.NaN;
    }

    private void OnDestroy() => ReleaseGripBones();
}
