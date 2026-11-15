using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

[DefaultExecutionOrder(1000)]
internal sealed class FlightHairVisual : MonoBehaviour
{
    private const int BoneCount = 8;
    private readonly MeshRenderer[] sources = new MeshRenderer[2];
    private readonly SkinnedMeshRenderer[] renderers = new SkinnedMeshRenderer[2];
    private readonly bool[] hiddenBefore = new bool[2];
    private readonly bool[] hidden = new bool[2];
    private readonly Mesh[] meshes = new Mesh[2];
    private Transform[] bones;
    private GameObject rig;
    private RegisteredSkin skin;
    private Player owner;
    private float phase;
    private float flightBlend;

    internal static void Attach(Transform mount, RegisteredSkin skin, Player owner, float phase)
    {
        if (mount == null) return;
        var visual = mount.GetComponent<FlightHairVisual>();
        if (skin == null)
        {
            if (visual != null)
            {
                visual.Clear();
                Destroy(visual);
            }
            return;
        }
        if (visual == null)
            visual = mount.gameObject.AddComponent<FlightHairVisual>();
        visual.owner = owner;
        visual.phase = phase;
        if (visual.skin != skin || visual.rig == null)
            visual.Build(skin);
    }

    private void Build(RegisteredSkin variant)
    {
        Clear();
        if (transform.childCount == 0) return;
        sources[0] = GetComponent<MeshRenderer>();
        sources[1] = transform.GetChild(0).GetComponent<MeshRenderer>();
        if (sources[0] == null || sources[1] == null) return;
        skin = variant;
        rig = new GameObject("Miku flight hair");
        rig.transform.SetParent(transform, false);
        bones = new Transform[BoneCount];
        Mesh[] originals = { skin.Assets.GetMesh("wing-base"), skin.Assets.GetMesh("wing-blades") };
        var vertices = new Vector3[2][];
        float start = float.PositiveInfinity;
        float end = float.NegativeInfinity;
        for (int part = 0; part < 2; part++)
        {
            vertices[part] = originals[part].vertices;
            Matrix4x4 toMount = transform.worldToLocalMatrix * sources[part].transform.localToWorldMatrix;
            for (int index = 0; index < vertices[part].Length; index++)
            {
                Vector3 point = toMount.MultiplyPoint3x4(vertices[part][index]);
                vertices[part][index] = point;
                start = Mathf.Min(start, point.z);
                end = Mathf.Max(end, point.z);
            }
        }
        float spacing = Mathf.Max(end - start, .001f) / (BoneCount - 1);
        for (int index = 0; index < bones.Length; index++)
        {
            Transform bone = new GameObject("Hair " + index).transform;
            bone.SetParent(index == 0 ? rig.transform : bones[index - 1], false);
            bone.localPosition = new Vector3(0, 0, index == 0 ? start : spacing);
            bones[index] = bone;
        }
        for (int part = 0; part < 2; part++)
        {
            Mesh mesh = Instantiate(originals[part]);
            mesh.name = originals[part].name + " animated";
            meshes[part] = mesh;
            var weights = new BoneWeight[vertices[part].Length];
            for (int index = 0; index < weights.Length; index++)
            {
                float position = Mathf.Clamp((vertices[part][index].z - start) / spacing, 0, BoneCount - 1);
                int first = Mathf.Min((int)position, BoneCount - 2);
                weights[index] = new BoneWeight { boneIndex0 = first, boneIndex1 = first + 1,
                    weight0 = 1f - (position - first), weight1 = position - first };
            }
            var binds = new Matrix4x4[BoneCount];
            for (int index = 0; index < binds.Length; index++)
                binds[index] = bones[index].worldToLocalMatrix * sources[part].transform.localToWorldMatrix;
            mesh.boneWeights = weights;
            mesh.bindposes = binds;
            Transform holder = new GameObject("Animated " + originals[part].name).transform;
            holder.SetParent(sources[part].transform, false);
            var renderer = holder.gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = bones[0];
            renderer.quality = SkinQuality.Bone2;
            Bounds bounds = mesh.bounds;
            bounds.Expand(bounds.size.magnitude);
            renderer.localBounds = bounds;
            renderers[part] = renderer;
        }
    }

    private void LateUpdate()
    {
        if (rig == null) return;
        flightBlend = Mathf.MoveTowards(flightBlend,
            GliderEquipment.FindWingsuit(owner)?.IsFlying == true ? 1f : 0f, Time.deltaTime * 5f);
        for (int index = 1; index < bones.Length; index++)
        {
            float depth = index / (float)(BoneCount - 1);
            float wave = Time.time * 6.4f - depth * 5f + phase;
            bones[index].localRotation = Quaternion.Euler(
                Mathf.Sin(wave) * 5f * depth * flightBlend,
                Mathf.Sin(wave + 1.3f) * 2f * depth * flightBlend, 0f);
        }
        for (int part = 0; part < renderers.Length; part++)
        {
            MeshRenderer source = sources[part];
            SkinnedMeshRenderer renderer = renderers[part];
            if (!hidden[part])
            {
                hiddenBefore[part] = source.forceRenderingOff;
                hidden[part] = true;
            }
            renderer.sharedMaterial = source.sharedMaterial;
            renderer.gameObject.layer = source.gameObject.layer;
            renderer.renderingLayerMask = source.renderingLayerMask;
            renderer.shadowCastingMode = source.shadowCastingMode;
            renderer.enabled = source.enabled;
            renderer.forceRenderingOff = hiddenBefore[part];
            source.forceRenderingOff = true;
        }
    }

    private void OnDisable()
    {
        flightBlend = 0f;
        if (bones != null)
            foreach (Transform bone in bones)
                if (bone != null) bone.localRotation = Quaternion.identity;
        for (int part = 0; part < sources.Length; part++)
        {
            if (hidden[part] && sources[part] != null)
                sources[part].forceRenderingOff = hiddenBefore[part];
            hidden[part] = false;
            if (renderers[part] != null) renderers[part].enabled = false;
        }
    }

    private void Clear()
    {
        OnDisable();
        for (int part = 0; part < renderers.Length; part++)
        {
            if (renderers[part] != null) Destroy(renderers[part].gameObject);
            if (meshes[part] != null) Destroy(meshes[part]);
            renderers[part] = null;
            meshes[part] = null;
        }
        if (rig != null) Destroy(rig);
        rig = null;
        bones = null;
        skin = null;
    }

    private void OnDestroy() => Clear();
}
