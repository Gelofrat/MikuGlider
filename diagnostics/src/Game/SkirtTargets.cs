using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal sealed class SkirtTargets
{
    private readonly Type visualType;
    private readonly FieldInfo skin;
    private readonly FieldInfo body;
    private readonly FieldInfo skirt;
    private readonly FieldInfo owner;
    private readonly FieldInfo ready;
    private readonly FieldInfo failed;
    private readonly Dictionary<Component, int> identities = new Dictionary<Component, int>();
    private int nextId;
    internal Assembly SourceAssembly => visualType.Assembly;

    internal SkirtTargets()
    {
        if (!Chainloader.PluginInfos.TryGetValue("horse.mycopunk.mikuglider", out var plugin))
            throw new InvalidOperationException("MikuGlider is not loaded.");
        visualType = plugin.Instance.GetType().Assembly.GetType("MikuGlider.SkirtGarmentVisual", true);
        skin = Field("skin");
        body = Field("body");
        skirt = Field("skirt");
        owner = Field("owner");
        ready = Field("ready");
        failed = Field("failed");
    }

    internal List<SkirtTarget> Find(bool includePreviews)
    {
        var targets = new List<SkirtTarget>();
        foreach (UnityEngine.Object found in Resources.FindObjectsOfTypeAll(visualType))
        {
            var component = found as Component;
            if (component == null || !component.gameObject.scene.IsValid() || !component.gameObject.activeInHierarchy)
                continue;
            var player = owner.GetValue(component) as Player;
            if (player != null && player != Player.LocalPlayer || player == null && !includePreviews)
                continue;
            var renderer = body.GetValue(component) as SkinnedMeshRenderer;
            object registered = skin.GetValue(component);
            if (renderer == null || registered == null || !renderer.enabled || renderer.forceRenderingOff)
                continue;
            if (renderer.sharedMesh != Property(registered, "Body") as Mesh &&
                renderer.sharedMesh != Property(registered, "BodyWithHat") as Mesh)
                continue;
            var garment = skirt.GetValue(component) as SkinnedMeshRenderer;
            if (garment != null && !garment.gameObject.activeInHierarchy)
                continue;
            if (player != null && PlayerLook.LocalCamera != null &&
                (PlayerLook.LocalCamera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
                continue;
            object definition = Property(registered, "Definition");
            if (!identities.TryGetValue(component, out int identity))
            {
                identity = ++nextId;
                identities.Add(component, identity);
            }
            targets.Add(new SkirtTarget(identity, component, renderer, garment,
                player, (string)Property(definition, "DisplayName"),
                (string)Property(definition, "Key"), ready, failed));
        }
        targets.Sort((a, b) =>
        {
            int context = (a.Owner == null ? 1 : 0).CompareTo(b.Owner == null ? 1 : 0);
            if (context != 0)
                return context;
            int visible = (a.Body.isVisible ? 0 : 1).CompareTo(b.Body.isVisible ? 0 : 1);
            return visible != 0 ? visible : a.Id.CompareTo(b.Id);
        });
        var destroyed = new List<Component>();
        foreach (Component component in identities.Keys)
            if (component == null)
                destroyed.Add(component);
        foreach (Component component in destroyed)
            identities.Remove(component);
        return targets;
    }

    private FieldInfo Field(string name)
    {
        return visualType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(visualType.FullName, name);
    }

    private static object Property(object instance, string name)
    {
        return instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(instance, null) ?? throw new MissingMemberException(instance.GetType().FullName, name);
    }
}

internal sealed class SkirtTarget
{
    private readonly FieldInfo ready;
    private readonly FieldInfo failed;
    internal Component Host { get; }
    internal SkinnedMeshRenderer Body { get; }
    internal SkinnedMeshRenderer Skirt { get; }
    internal SkinnedMeshRenderer Surface => ReadRenderer("surfaceRenderer");
    internal SkinnedMeshRenderer Coat => ReadRenderer("coat");
    internal SkinnedMeshRenderer Sleeves => ReadRenderer("sleeves");
    internal Player Owner { get; }
    internal string Skin { get; }
    internal string Key { get; }
    internal int Id { get; }
    internal string Context => Host == null ? "destroyed" : Owner == null ? "preview" :
        Host.GetComponentInParent<PlayerLook>() != null ? "local_self" : "local_world";
    internal double PoseMilliseconds => Host == null ? 0 : (double)(Host.GetType()
        .GetProperty("PoseUpdateMilliseconds", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(Host, null) ?? 0d);
    internal bool Ready => Host != null && (bool)ready.GetValue(Host);
    internal bool Failed => Host != null && (bool)failed.GetValue(Host);
    internal bool Alive => Host != null && Body != null && Body.enabled && !Body.forceRenderingOff &&
        Body.gameObject.activeInHierarchy && (Skirt == null || Skirt.gameObject.activeInHierarchy);

    internal SkirtTarget(int identity, Component host, SkinnedMeshRenderer body, SkinnedMeshRenderer skirt,
        Player owner, string skin, string key, FieldInfo ready, FieldInfo failed)
    {
        Id = identity;
        Host = host;
        Body = body;
        Skirt = skirt;
        Owner = owner;
        Skin = skin;
        Key = key;
        this.ready = ready;
        this.failed = failed;
    }


    private SkinnedMeshRenderer ReadRenderer(string name) => Host == null ? null :
        Host.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(Host) as SkinnedMeshRenderer;

    internal Transform ReferenceFrame
    {
        get
        {
            foreach (Transform bone in Body.bones)
                if (bone != null && bone.name == "joint_Pelvis")
                    return bone;
            return Body.transform;
        }
    }

    internal Camera FindCamera()
    {
        if (Owner != null && PlayerLook.LocalCamera != null)
            return PlayerLook.LocalCamera;
        Camera selected = null;
        foreach (Camera camera in Camera.allCameras)
        {
            if (camera.enabled && (camera.cullingMask & (1 << Body.gameObject.layer)) != 0 &&
                (selected == null || camera.depth > selected.depth))
                selected = camera;
        }
        return selected ?? Camera.main;
    }

    internal void ReadMotion(ClothFrame frame)
    {
        if (Owner == null)
        {
            frame.motion = "preview";
            return;
        }
        frame.playerVelocity = Owner.Velocity;
        frame.grounded = Owner.Grounded;
        frame.sliding = Owner.Sliding;
        frame.sprinting = Owner.IsSprinting;
        if (Owner.Gear != null)
            foreach (IGear gear in Owner.Gear)
                if (gear is Wingsuit wingsuit)
                    frame.flying = wingsuit.IsFlying;
        frame.motion = !Owner.IsAlive ? "dead" : frame.flying ? "flight" : frame.sliding ? "slide" :
            !frame.grounded ? frame.playerVelocity.y > 0f ? "jump" : "fall" :
            frame.playerVelocity.sqrMagnitude < 0.04f ? "idle" : frame.sprinting ? "run" : "walk";
        try
        {
            var state = Owner.Animator?.SelfThirdPersonAnimator?.CurrentState;
            if (state != null)
            {
                frame.clip = state.Clip != null ? state.Clip.name : state.ToString();
                frame.clipTime = state.Time;
            }
        }
        catch (Exception error)
        {
            frame.clipError = error.GetType().Name;
        }
    }

    internal static string PathOf(Transform transform)
    {
        var names = new List<string>();
        for (Transform node = transform; node != null; node = node.parent)
            names.Add(node.name);
        names.Reverse();
        return string.Join("/", names);
    }
}
