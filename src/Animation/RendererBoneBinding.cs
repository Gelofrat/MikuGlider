using System.Runtime.CompilerServices;
using UnityEngine;

namespace MikuGlider;

// The only writer of renderer.bones for wrist and grip corrections.
// Layers are ordered: native rig -> wrist stations -> grip copies.
internal sealed class RendererBoneBinding
{
    private static readonly ConditionalWeakTable<SkinnedMeshRenderer, RendererBoneBinding> Owners =
        new ConditionalWeakTable<SkinnedMeshRenderer, RendererBoneBinding>();
    private readonly SkinnedMeshRenderer renderer;
    private readonly Transform[] native;
    private Transform[] wrist, grip, lastApplied;

    private RendererBoneBinding(SkinnedMeshRenderer renderer)
    {
        this.renderer = renderer;
        native = renderer.bones;
    }

    internal static RendererBoneBinding For(SkinnedMeshRenderer renderer) =>
        Owners.GetValue(renderer, value => new RendererBoneBinding(value));

    // The game can reuse a renderer with a different rig after changing scenes.
    // Never reinstall an old visual layer over that new source skeleton.
    internal bool Matches(Transform[] bones) => SameBones(bones, lastApplied) || SameBones(bones, native);

    internal void SetWrist(Transform[] bones) { wrist = bones; Apply(); }
    internal void SetGrip(Transform[] bones) { grip = bones; Apply(); }
    internal void ReleaseGrip() { grip = null; Restore(); }
    internal void ReleaseWrist() { wrist = null; Restore(); }

    private void Apply()
    {
        lastApplied = grip ?? wrist ?? native;
        renderer.bones = lastApplied;
    }

    private void Restore()
    {
        // A game-side rebind takes precedence over cleanup of our old layers.
        if (renderer != null && SameBones(renderer.bones, lastApplied)) Apply();
        if (wrist == null && grip == null) Owners.Remove(renderer);
    }

    internal static bool SameBones(Transform[] a, Transform[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
