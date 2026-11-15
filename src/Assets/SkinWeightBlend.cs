using UnityEngine;

namespace MikuGlider;

internal struct SkinWeightBlend
{
    private BoneWeight weights;
    private int count;

    internal static BoneWeight BlendDistinct(BoneWeight source, BoneWeight target, float amount)
    {
        var blend = new SkinWeightBlend();
        blend.AddScaled(source.boneIndex0, source.weight0, 1 - amount);
        blend.AddScaled(source.boneIndex1, source.weight1, 1 - amount);
        blend.AddScaled(source.boneIndex2, source.weight2, 1 - amount);
        blend.AddScaled(source.boneIndex3, source.weight3, 1 - amount);
        blend.AddScaled(target.boneIndex0, target.weight0, amount);
        blend.AddScaled(target.boneIndex1, target.weight1, amount);
        blend.AddScaled(target.boneIndex2, target.weight2, amount);
        blend.AddScaled(target.boneIndex3, target.weight3, amount);
        return blend.Normalize();
    }

    private void AddScaled(int bone, float weight, float scale)
    {
        float scaled = weight * scale;
        if (scaled > 0) Add(bone, scaled);
    }

    internal static BoneWeight Replace(BoneWeight source, int first, int second, int third, int joint, float fraction)
    {
        var blend = new SkinWeightBlend();
        float amount = 0;
        blend.Keep(source.boneIndex0, source.weight0, first, second, third, ref amount);
        blend.Keep(source.boneIndex1, source.weight1, first, second, third, ref amount);
        blend.Keep(source.boneIndex2, source.weight2, first, second, third, ref amount);
        blend.Keep(source.boneIndex3, source.weight3, first, second, third, ref amount);
        if (amount <= 0) return source;
        blend.Add(joint, amount * (1 - fraction));
        blend.Add(joint + 1, amount * fraction);
        return blend.Normalize();
    }

    private void Keep(int bone, float weight, int first, int second, int third, ref float amount)
    {
        if (bone == first || bone == second || bone == third) amount += weight;
        else if (weight > 0) Add(bone, weight);
    }

    private void Add(int bone, float weight)
    {
        if (count == 0) Set(0, bone, weight);
        else if (count == 1) Set(1, bone, weight);
        else if (count == 2) Set(2, bone, weight);
        else if (count == 3)
        {
            Set(3, bone, weight);
            SwapIfLess(0, 1);
            SwapIfLess(1, 2);
            SwapIfLess(0, 1);
            SwapIfLess(2, 3);
            SwapIfLess(1, 2);
            SwapIfLess(0, 1);
        }
        else if (weight > weights.weight3)
        {
            Set(3, bone, weight);
            SwapIfLess(2, 3);
            SwapIfLess(1, 2);
            SwapIfLess(0, 1);
        }
        count++;
    }

    private BoneWeight Normalize()
    {
        if (count == 2) SwapIfLess(0, 1);
        else if (count == 3)
        {
            SwapIfLess(0, 1);
            SwapIfLess(0, 2);
            SwapIfLess(1, 2);
        }
        float total = weights.weight0 + weights.weight1 + weights.weight2 + weights.weight3;
        weights.weight0 /= total;
        weights.weight1 /= total;
        weights.weight2 /= total;
        weights.weight3 /= total;
        return weights;
    }

    private void SwapIfLess(int first, int second)
    {
        Get(first, out int firstBone, out float firstWeight);
        Get(second, out int secondBone, out float secondWeight);
        if (firstWeight >= secondWeight) return;
        Set(first, secondBone, secondWeight);
        Set(second, firstBone, firstWeight);
    }

    private void Get(int slot, out int bone, out float weight)
    {
        switch (slot)
        {
            case 0: bone = weights.boneIndex0; weight = weights.weight0; break;
            case 1: bone = weights.boneIndex1; weight = weights.weight1; break;
            case 2: bone = weights.boneIndex2; weight = weights.weight2; break;
            default: bone = weights.boneIndex3; weight = weights.weight3; break;
        }
    }

    private void Set(int slot, int bone, float weight)
    {
        switch (slot)
        {
            case 0: weights.boneIndex0 = bone; weights.weight0 = weight; break;
            case 1: weights.boneIndex1 = bone; weights.weight1 = weight; break;
            case 2: weights.boneIndex2 = bone; weights.weight2 = weight; break;
            default: weights.boneIndex3 = bone; weights.weight3 = weight; break;
        }
    }
}
