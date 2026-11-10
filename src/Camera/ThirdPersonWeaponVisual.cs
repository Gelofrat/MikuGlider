using System.Collections.Generic;
using Animancer;
using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

internal sealed class ThirdPersonWeaponVisual
{
    private readonly Dictionary<Transform, Transform> transforms = new Dictionary<Transform, Transform>();
    private readonly Dictionary<Renderer, Renderer> renderers = new Dictionary<Renderer, Renderer>();
    private readonly List<Material> materials = new List<Material>();
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private Gun source;
    private Transform sourceRoot;
    private GameObject visual;
    private ThirdPersonAnimator animator;
    private ThirdPersonGearAnimationSet animations;
    private AnimancerState overlay;
    private PlayerAnimation.Key overlayKey;

    internal void Update(Player player, ThirdPersonAnimator target, bool visible)
    {
        Gun gun = player.SelectedGear as Gun;
        if (!visible || target == null || gun == null || gun.GunModel == null)
        {
            Clear();
            return;
        }
        Transform mount = target.Rig.WeaponRootThirdPerson;
        if (mount == null)
        {
            Clear();
            return;
        }
        if (source != gun || sourceRoot != gun.GunModel || animator != target || visual == null || visual.transform.parent != mount)
        {
            Clear();
            if (!NativeWeaponAccess.TryGet(gun, player.Character, out Gun.ThirdPersonData data, out Vector3 scale))
                return;
            source = gun;
            sourceRoot = gun.GunModel;
            animator = target;
            animations = data.animationSet;
            visual = CopyHierarchy(sourceRoot, mount).gameObject;
            visual.name = "MikuThirdPersonWeapon";
            visual.transform.SetLocalPositionAndRotation(data.position,
                data.rotation == default(Quaternion) ? Quaternion.identity : data.rotation);
            visual.transform.localScale = scale;
            CopyRenderers();
        }
        foreach (var pair in transforms)
        {
            if (pair.Key == null || pair.Value == null || pair.Key == sourceRoot)
                continue;
            pair.Value.SetLocalPositionAndRotation(pair.Key.localPosition, pair.Key.localRotation);
            pair.Value.localScale = pair.Key.localScale;
            pair.Value.gameObject.SetActive(pair.Key.gameObject.activeSelf);
        }
        foreach (var pair in renderers)
        {
            if (pair.Key == null || pair.Value == null)
                continue;
            pair.Value.enabled = pair.Key.enabled;
            materials.Clear();
            pair.Key.GetSharedMaterials(materials);
            pair.Value.SetSharedMaterials(materials);
            pair.Key.GetPropertyBlock(properties);
            pair.Value.SetPropertyBlock(properties);
        }
        UpdatePose(player);
    }

    internal void Clear()
    {
        if (overlay != null && animator != null)
            overlay.Layer.StartFade(0f, 0.12f);
        if (visual != null)
        {
            visual.SetActive(false);
            Object.Destroy(visual);
        }
        transforms.Clear();
        renderers.Clear();
        source = null;
        sourceRoot = null;
        visual = null;
        animator = null;
        animations = null;
        overlay = null;
        overlayKey = null;
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
                    clone.rootBone = skin.rootBone != null && transforms.TryGetValue(skin.rootBone, out Transform root) ? root : visual.transform;
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

    private void UpdatePose(Player player)
    {
        PlayerAnimation.Key key = null;
        if (animations != null && GliderEquipment.FindWingsuit(player)?.IsFlying != true)
            key = player.Sliding ? animations.slide : !player.Grounded ? animations.jumpLoop :
                PlayerInput.MoveInput().sqrMagnitude > 0.01f ? animations.run : animations.idle;
        if (key?.clip == null)
        {
            if (overlay != null)
                overlay.Layer.StartFade(0f, 0.12f);
            overlay = null;
            overlayKey = null;
            return;
        }
        if (overlayKey == key)
            return;
        animator.PlayAdditive(key);
        overlay = animator.CreateState(key);
        overlay.Layer.IsAdditive = false;
        overlay.Layer.StartFade(1f, Mathf.Max(0.05f, key.fadeDuration));
        overlayKey = key;
    }
}
