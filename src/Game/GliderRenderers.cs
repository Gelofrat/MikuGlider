using System;
using UnityEngine;

namespace MikuGlider;

internal sealed class GliderRenderers
{
    internal SkinnedMeshRenderer Body { get; }
    internal SkinnedMeshRenderer LeftArm { get; }
    internal SkinnedMeshRenderer RightArm { get; }

    private GliderRenderers(SkinnedMeshRenderer body, SkinnedMeshRenderer leftArm, SkinnedMeshRenderer rightArm)
    {
        Body = body;
        LeftArm = leftArm;
        RightArm = rightArm;
    }

    internal static GliderRenderers Read(Character character)
    {
        var rig = character.ThirdPersonModel.GetComponent<ThirdPersonRig>();
        Transform firstPerson = character.FirstPersonModel.transform;
        if (rig == null || rig.SkinRenderers == null || rig.SkinRenderers.Length == 0 || firstPerson.childCount < 3)
            throw new InvalidOperationException("Glider renderer layout changed.");

        var body = rig.SkinRenderers[0] as SkinnedMeshRenderer;
        var left = firstPerson.GetChild(1).GetComponent<SkinnedMeshRenderer>();
        var right = firstPerson.GetChild(2).GetComponent<SkinnedMeshRenderer>();
        if (body == null || left == null || right == null)
            throw new InvalidOperationException("Glider body or arm renderers are missing.");
        if (rig.MainRendererIndex != 1 || rig.LightRendererIndex != 0 || body.sharedMaterials.Length != 2)
            throw new InvalidOperationException("Glider material layout changed; expected face slot 0 and body slot 1.");

        return new GliderRenderers(body, left, right);
    }
}
