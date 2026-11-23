using System.Runtime.CompilerServices;
using UnityEngine;

namespace MikuGlider;

internal static class DeathPartMaterials
{
    private sealed class Binding
    {
        internal Material[] Original;
        internal Material[] Applied;
    }

    private static readonly ConditionalWeakTable<Renderer, Binding> Bindings = new ConditionalWeakTable<Renderer, Binding>();

    internal static void Apply(Renderer renderer, Material[] materials)
    {
        Material[] current = renderer.sharedMaterials;
        if (Matches(current, materials)) return;
        var binding = Bindings.GetValue(renderer, _ => new Binding { Original = current });
        binding.Applied = materials;
        renderer.sharedMaterials = materials;
    }

    internal static void Release(Renderer renderer)
    {
        if (!Bindings.TryGetValue(renderer, out var binding)) return;
        Bindings.Remove(renderer);
        Material[] current = renderer.sharedMaterials;
        if (Matches(current, binding.Applied)) renderer.sharedMaterials = binding.Original;
    }

    private static bool Matches(Material[] current, Material[] expected)
    {
        if (current.Length != expected.Length) return false;
        for (int i = 0; i < current.Length; i++)
            if (current[i] != expected[i]) return false;
        return true;
    }
}
