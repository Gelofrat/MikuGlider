using System;
using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider;

// Mesh-only copy. Detached magazines/handles use the prefab attachment pose:
// their first-person sockets do not exist on the third-person skeleton.
internal sealed class WeaponModelCopy : IDisposable
{
    private readonly Dictionary<Transform, Transform> transforms = new Dictionary<Transform, Transform>();
    private readonly Dictionary<Renderer, Renderer> renderers = new Dictionary<Renderer, Renderer>();
    private readonly List<Material> materials = new List<Material>();
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private readonly HashSet<Transform> attachmentRoots = new HashSet<Transform>();
    private readonly Transform sourceRoot;
    internal Transform Root { get; }

    internal WeaponModelCopy(Transform source, Transform parent)
    {
        sourceRoot = source;
        Root = CopyHierarchy(source, parent);
    }

    internal void AddDetached(Transform source, Transform prefabPart, Transform prefabRoot)
    {
        if (source == null || prefabPart == null || prefabRoot == null || transforms.ContainsKey(source)) return;
        var names = new Stack<string>();
        Transform ancestor = prefabPart.parent;
        while (ancestor != null && ancestor != prefabRoot) { names.Push(ancestor.name); ancestor = ancestor.parent; }
        if (ancestor != prefabRoot) return;
        Transform parent = names.Count == 0 ? Root : Root.Find(string.Join("/", names));
        if (parent == null) return;
        Transform copy = CopyHierarchy(source, parent);
        copy.SetLocalPositionAndRotation(prefabPart.localPosition, prefabPart.localRotation);
        copy.localScale = prefabPart.localScale;
        copy.gameObject.SetActive(prefabPart.gameObject.activeSelf);
        attachmentRoots.Add(source);
    }

    internal void Finish() => CopyRenderers();

    internal void Sync()
    {
        foreach (var pair in transforms)
        {
            if (pair.Key == null || pair.Value == null || pair.Key == sourceRoot || attachmentRoots.Contains(pair.Key)) continue;
            pair.Value.SetLocalPositionAndRotation(pair.Key.localPosition, pair.Key.localRotation);
            pair.Value.localScale = pair.Key.localScale;
            pair.Value.gameObject.SetActive(pair.Key.gameObject.activeSelf);
        }
        foreach (var pair in renderers)
        {
            if (pair.Key == null || pair.Value == null) continue;
            pair.Value.enabled = pair.Key.enabled;
            materials.Clear();
            pair.Key.GetSharedMaterials(materials);
            pair.Value.SetSharedMaterials(materials);
            pair.Key.GetPropertyBlock(properties);
            pair.Value.SetPropertyBlock(properties);
        }
    }

    public void Dispose()
    {
        if (Root != null)
        {
            Root.gameObject.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(Root.gameObject);
            else UnityEngine.Object.DestroyImmediate(Root.gameObject);
        }
        transforms.Clear(); renderers.Clear(); attachmentRoots.Clear();
    }

    private Transform CopyHierarchy(Transform original, Transform parent)
    {
        Transform copy = new GameObject(original.name).transform;
        copy.SetParent(parent, false);
        copy.gameObject.layer = parent.gameObject.layer;
        copy.SetLocalPositionAndRotation(original.localPosition, original.localRotation);
        copy.localScale = original.localScale;
        copy.gameObject.SetActive(original == sourceRoot || original.gameObject.activeSelf);
        transforms.Add(original, copy);
        for (int i = 0; i < original.childCount; i++)
            CopyHierarchy(original.GetChild(i), copy);
        return copy;
    }

    private void CopyRenderers()
    {
        foreach (var pair in transforms)
        {
            foreach (Renderer original in pair.Key.GetComponents<Renderer>())
            {
                Renderer copy;
                if (original is SkinnedMeshRenderer skin)
                {
                    var clone = pair.Value.gameObject.AddComponent<SkinnedMeshRenderer>();
                    clone.sharedMesh = skin.sharedMesh;
                    Transform[] bones = skin.bones;
                    for (int i = 0; i < bones.Length; i++)
                        bones[i] = bones[i] != null && transforms.TryGetValue(bones[i], out Transform bone) ? bone : pair.Value;
                    clone.bones = bones;
                    clone.rootBone = skin.rootBone != null && transforms.TryGetValue(skin.rootBone, out Transform root) ? root : Root;
                    clone.localBounds = skin.localBounds;
                    clone.quality = skin.quality;
                    copy = clone;
                }
                else if (original is MeshRenderer && pair.Key.TryGetComponent(out MeshFilter filter))
                {
                    pair.Value.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    copy = pair.Value.gameObject.AddComponent<MeshRenderer>();
                }
                else
                    continue;
                copy.sharedMaterials = original.sharedMaterials;
                copy.renderingLayerMask = 1u;
                copy.shadowCastingMode = original.shadowCastingMode;
                copy.receiveShadows = original.receiveShadows;
                renderers.Add(original, copy);
            }
        }
    }

}
