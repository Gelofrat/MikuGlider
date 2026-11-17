using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Pigeon.Movement
{
    internal sealed class Player
    {
        internal bool IsAlive = true;
        internal readonly Wingsuit Wingsuit = new Wingsuit();
    }

    internal sealed class Wingsuit
    {
        internal bool IsFlying;
    }
}

namespace MikuGlider
{
    internal static class GliderEquipment
    {
        internal static Pigeon.Movement.Wingsuit FindWingsuit(Pigeon.Movement.Player owner) => owner?.Wingsuit;
    }

    internal sealed class SkinVariantDefinition
    {
        internal string Key;
        internal string DisplayName;
        internal bool CorrectFirstPersonLeftWrist;
        internal bool CorrectFirstPersonRightWrist;
    }

    internal sealed class RegisteredSkin
    {
        internal SkinVariantDefinition Definition;
        internal SkinAssets Assets;
        internal bool OwnsBody(Mesh mesh) => mesh == Assets.GetMesh("body") || mesh == Assets.GetMesh("body-hat");
    }

    internal sealed class SkinAssets : IDisposable
    {
        private readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        internal HairAnimationData Hair { get; private set; }
        internal SkinModelDefinition ModelDefinition { get; private set; }
        internal ModelCorrections Corrections { get; private set; }

        internal SkinAssets(string folder)
        {
            var definition = Resources.Load<TextAsset>("MikuPhysicsLab/" + folder + "/definition");
            ModelDefinition = SkinModelDefinition.Parse(definition.text);
            Corrections = ModelCorrections.Parse(Resources.Load<TextAsset>("MikuPhysicsLab/" + folder + "/corrections").text);
            var model = Resources.Load<TextAsset>("MikuPhysicsLab/" + folder + "/model");
            using (var stream = new MemoryStream(model.bytes))
            {
                foreach (MeshData data in MeshResourceReader.Read(stream))
                {
                    var mesh = new Mesh { name = folder + "." + data.Name };
                    mesh.indexFormat = data.Vertices.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                    mesh.vertices = data.Vertices;
                    mesh.normals = data.Normals;
                    mesh.uv = data.UV;
                    if (data.Weights != null)
                    {
                        mesh.bindposes = data.BindPoses;
                        mesh.boneWeights = data.Weights;
                    }
                    MeshIndices.Assign(mesh, data.Submeshes);
                    mesh.RecalculateBounds();
                    meshes.Add(data.Name, mesh);
                }
            }
            var hair = Resources.Load<TextAsset>("MikuPhysicsLab/" + folder + "/hair");
            using (var stream = new MemoryStream(hair.bytes))
                Hair = HairResourceReader.Read(stream);
        }

        internal Mesh GetMesh(string name) => meshes[name];
        internal bool TryGetMesh(string name, out Mesh mesh) => meshes.TryGetValue(name, out mesh);
        public void Dispose()
        {
            foreach (Mesh mesh in meshes.Values)
                UnityEngine.Object.Destroy(mesh);
            meshes.Clear();
        }
    }

    internal static class MikuGliderPlugin
    {
        internal static readonly LabRuntime Runtime = new LabRuntime();
    }

    internal sealed class LabRuntime
    {
        internal readonly LabLogger Logger = new LabLogger();
    }

    internal sealed class LabLogger
    {
        internal void LogError(string text) => Debug.LogError(text);
        internal void LogDebug(string text) => Debug.Log(text);
        internal void LogWarning(string text) => Debug.LogWarning(text);
    }
}

namespace Horse.MikuPhysicsLab
{
    internal static class LabAssetLibrary
    {
        private static readonly Dictionary<string, MikuGlider.SkinAssets> Assets = new Dictionary<string, MikuGlider.SkinAssets>();

        internal static MikuGlider.SkinAssets Get(string variant)
        {
            if (!Assets.TryGetValue(variant, out MikuGlider.SkinAssets assets))
            {
                assets = new MikuGlider.SkinAssets(variant);
                Assets.Add(variant, assets);
            }
            return assets;
        }

        internal static void Clear()
        {
            foreach (MikuGlider.SkinAssets assets in Assets.Values)
                assets.Dispose();
            Assets.Clear();
        }
    }
}
