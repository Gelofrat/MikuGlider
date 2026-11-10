using System.Collections.Generic;
using Pigeon.Movement;
using UnityEngine;

namespace MikuGlider;

internal sealed class WeaponVisibility
{
    private readonly Dictionary<Renderer, bool> previous = new Dictionary<Renderer, bool>();
    private readonly HashSet<Renderer> current = new HashSet<Renderer>();
    private readonly List<Renderer> children = new List<Renderer>();
    private readonly List<Renderer> released = new List<Renderer>();
    internal bool Visible { get; private set; } = true;

    internal void Toggle()
    {
        Visible = !Visible;
        if (Visible)
            Restore();
    }

    internal void Update(Player player)
    {
        if (Visible || player == null)
        {
            Restore();
            return;
        }
        current.Clear();
        Gun gun = player.SelectedGear as Gun;
        Collect(gun?.GunModel);
        if (gun?.SkinRenderers != null)
            foreach (Renderer renderer in gun.SkinRenderers)
                if (renderer != null)
                    current.Add(renderer);
        if (player.Animator != null)
        {
            Collect(player.Animator.WeaponRoot);
            Collect(player.Animator.MagRoot);
            Collect(player.Animator.Mag1Root);
        }
        released.Clear();
        foreach (var pair in previous)
            if (pair.Key == null || !current.Contains(pair.Key))
            {
                if (pair.Key != null)
                    pair.Key.forceRenderingOff = pair.Value;
                released.Add(pair.Key);
            }
        foreach (Renderer renderer in released)
            previous.Remove(renderer);
        foreach (Renderer renderer in current)
        {
            if (!previous.ContainsKey(renderer))
                previous.Add(renderer, renderer.forceRenderingOff);
            renderer.forceRenderingOff = true;
        }
    }

    private void Collect(Transform root)
    {
        if (root == null) return;
        children.Clear();
        root.GetComponentsInChildren(true, children);
        foreach (Renderer renderer in children)
            if (renderer != null)
                current.Add(renderer);
    }

    internal void Restore()
    {
        foreach (var pair in previous)
            if (pair.Key != null)
                pair.Key.forceRenderingOff = pair.Value;
        previous.Clear();
        current.Clear();
        children.Clear();
        released.Clear();
    }
}
