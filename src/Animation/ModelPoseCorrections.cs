using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace MikuGlider;

[DefaultExecutionOrder(1400)]
internal sealed class ModelPoseCorrections : MonoBehaviour
{
    private static readonly ConditionalWeakTable<Mesh, AppliedMesh> Applied = new ConditionalWeakTable<Mesh, AppliedMesh>();
    private static readonly ConditionalWeakTable<Mesh, object> Rejected = new ConditionalWeakTable<Mesh, object>();
    private static readonly Action<Mesh, string, float, Vector3[], Vector3[], Vector3[]> AddShape =
        (Action<Mesh, string, float, Vector3[], Vector3[], Vector3[]>)Delegate.CreateDelegate(
            typeof(Action<Mesh, string, float, Vector3[], Vector3[], Vector3[]>),
            typeof(Mesh).GetMethod("AddBlendShapeFrame", new[] { typeof(string), typeof(float), typeof(Vector3[]), typeof(Vector3[]), typeof(Vector3[]) }));
    private SkinnedMeshRenderer surface;
    private AppliedMesh state;
    private Transform[] bones;
    private float[] activations;
    private float[] appliedWeights;
    private Transform weaponRoot;
    private string weaponNamePrefix = "";
    private Transform[] weaponChildren = Array.Empty<Transform>();
    private string[] weaponNames = Array.Empty<string>();

    internal sealed class AppliedMesh
    {
        internal MeshCorrection Correction;
        internal BoneWeight[] OriginalWeights;
        internal int[] Shapes;
    }

    internal static AppliedMesh GetApplied(Mesh mesh) => mesh != null && Applied.TryGetValue(mesh, out var state) ? state : null;

    internal static void Attach(SkinnedMeshRenderer renderer, ModelCorrections profile, Transform weaponRoot = null, string weaponNamePrefix = "")
    {
        if (renderer == null) return;
        if (renderer.sharedMesh == null || profile == null || profile.meshes.Length == 0)
        {
            renderer.GetComponent<ModelPoseCorrections>()?.ResetShapes();
            return;
        }
        Mesh candidate = renderer.sharedMesh;
        if (Rejected.TryGetValue(candidate, out _)) return;
        try
        {
            AttachValidated(renderer, profile);
            var component = renderer.GetComponent<ModelPoseCorrections>();
            if (component != null && (component.weaponRoot != weaponRoot || component.weaponNamePrefix != weaponNamePrefix))
            {
                component.weaponRoot = weaponRoot;
                component.weaponNamePrefix = weaponNamePrefix;
                component.weaponChildren = Array.Empty<Transform>();
                component.weaponNames = Array.Empty<string>();
            }
        }
        catch (Exception error) when (error is InvalidDataException || error is InvalidOperationException)
        {
            Rejected.Add(candidate, new object());
            Debug.LogWarning("Miku Glider skipped incompatible manual corrections for " + candidate.name + ": " + error.Message);
        }
    }

    private static void AttachValidated(SkinnedMeshRenderer renderer, ModelCorrections profile)
    {
        var previous = renderer.GetComponent<ModelPoseCorrections>();
        Mesh mesh = renderer.sharedMesh;
        if (previous != null && previous.state != null && previous.surface.sharedMesh == mesh &&
            Applied.TryGetValue(mesh, out var known) && known == previous.state)
        {
            previous.bones = renderer.bones;
            previous.enabled = known.Shapes.Length > 0;
            return;
        }
        MeshCorrection correction = mesh != null ? profile?.Find(mesh) : null;
        if (correction == null)
        {
            if (previous != null) previous.ResetShapes();
            return;
        }
        bool cached = Applied.TryGetValue(mesh, out var applied);
        if (!cached) ModelCorrections.Validate(mesh, correction);
        Transform[] targets = renderer.bones;
        foreach (PoseShapeEdit pose in correction.poses)
            foreach (PoseShapeDriver driver in pose.drivers)
                if (driver == null || driver.bone < 0 || driver.bone >= targets.Length || targets[driver.bone] == null || targets[driver.bone].name != driver.name)
                    throw new InvalidOperationException("Pose correction skeleton differs: " + correction.key);
        if (!cached)
        {
            applied = new AppliedMesh { Correction = correction, OriginalWeights = mesh.boneWeights, Shapes = new int[correction.poses.Length] };
            var weights = (BoneWeight[])applied.OriginalWeights.Clone();
            foreach (VertexWeightEdit edit in correction.weights) weights[edit.vertex] = edit.value;
            for (int i = 0; i < correction.poses.Length; i++)
            {
                PoseShapeEdit pose = correction.poses[i];
                var deltas = new Vector3[mesh.vertexCount];
                for (int v = 0; v < pose.vertices.Length; v++) deltas[pose.vertices[v]] = pose.deltas[v];
                var normalDeltas = new Vector3[mesh.vertexCount];
                Mesh shape = Instantiate(mesh);
                Vector3[] points = mesh.vertices;
                shape.RecalculateNormals();
                Vector3[] normals = shape.normals;
                for (int v = 0; v < points.Length; v++) points[v] += deltas[v];
                shape.vertices = points;
                shape.RecalculateNormals();
                Vector3[] correctedNormals = shape.normals;
                for (int v = 0; v < normals.Length; v++) normalDeltas[v] = correctedNormals[v] - normals[v];
                if (Application.isPlaying) Destroy(shape); else DestroyImmediate(shape);
                applied.Shapes[i] = mesh.blendShapeCount;
                AddShape(mesh, "MikuManual:" + pose.name, 100, deltas, normalDeltas, null);
            }
            mesh.boneWeights = weights;
            Applied.Add(mesh, applied);
        }
        var component = previous ?? renderer.gameObject.AddComponent<ModelPoseCorrections>();
        component.surface = renderer;
        component.state = applied;
        component.bones = renderer.bones;
        component.activations = new float[applied.Shapes.Length];
        component.appliedWeights = new float[applied.Shapes.Length];
        for (int i = 0; i < component.appliedWeights.Length; i++) component.appliedWeights[i] = float.NaN;
        component.enabled = applied.Shapes.Length > 0;
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
        float total = 0;
        for (int i = 0; i < activations.Length; i++)
        {
            PoseShapeEdit pose = state.Correction.poses[i];
            string weapon = string.IsNullOrEmpty(pose.weapon) ? state.Correction.weapon : pose.weapon;
            activations[i] = MatchesWeapon(weapon) ? Activation(pose, bones) : 0;
            total += activations[i];
        }
        float scale = 100f / Mathf.Max(1, total);
        for (int i = 0; i < activations.Length; i++)
        {
            float weight = activations[i] * scale;
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
        }
        for (int i = 0; i < count; i++)
        {
            Transform child = weaponRoot.GetChild(i);
            if (weaponChildren[i] != child)
            {
                weaponChildren[i] = child;
                // Names are read only when the weapon model changes, not every frame.
                string name = child.name;
                weaponNames[i] = name.StartsWith(weaponNamePrefix, StringComparison.Ordinal)
                    ? name.Substring(weaponNamePrefix.Length) : name;
            }
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

    private void ResetShapes()
    {
        if (surface != null && state != null && GetApplied(surface.sharedMesh) == state)
            foreach (int shape in state.Shapes) surface.SetBlendShapeWeight(shape, 0);
        state = null;
        enabled = false;
    }

    private void OnDisable()
    {
        if (surface != null && state != null && GetApplied(surface.sharedMesh) == state)
            foreach (int shape in state.Shapes) surface.SetBlendShapeWeight(shape, 0);
        if (appliedWeights != null)
            for (int i = 0; i < appliedWeights.Length; i++) appliedWeights[i] = float.NaN;
    }
}
