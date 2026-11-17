using Animancer;
using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

internal sealed class ThirdPersonWeaponVisual
{
    private WeaponModelCopy model;
    private Gun source;
    private Transform sourceRoot;
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
        if (source != gun || sourceRoot != gun.GunModel || animator != target || model == null || model.Root == null || model.Root.parent != mount)
        {
            Clear();
            if (!NativeWeaponAccess.TryGet(gun, player.Character, out Gun.ThirdPersonData data, out Vector3 scale))
                return;
            source = gun;
            sourceRoot = gun.GunModel;
            animator = target;
            animations = data.animationSet;
            model = new WeaponModelCopy(sourceRoot, mount);
            model.Root.name = "MikuThirdPersonWeapon";
            model.Root.SetLocalPositionAndRotation(data.position,
                data.rotation == default(Quaternion) ? Quaternion.identity : data.rotation);
            model.Root.localScale = scale;
            Gun prefab = gun.Prefab as Gun;
            if (prefab != null)
            {
                model.AddDetached(gun.MagModel, prefab.MagModel, prefab.GunModel);
                model.AddDetached(NativeWeaponAccess.Mag1Model(gun), NativeWeaponAccess.Mag1Model(prefab), prefab.GunModel);
            }
            model.Finish();
        }
        model.Sync();
        UpdatePose(player);
    }

    internal void Clear()
    {
        if (overlay != null && animator != null)
            overlay.Layer.StartFade(0f, 0.12f);
        model?.Dispose();
        model = null;
        source = null;
        sourceRoot = null;
        animator = null;
        animations = null;
        overlay = null;
        overlayKey = null;
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
