using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace MikuGlider;

[Serializable]
internal sealed class ModelCorrections
{
    private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        MissingMemberHandling = MissingMemberHandling.Error,
        TypeNameHandling = TypeNameHandling.None,
        MaxDepth = 16,
        ContractResolver = new CorrectionContractResolver()
    };

    [JsonProperty(Required = Required.Always)]
    public int schemaVersion = 1;
    [JsonProperty(Required = Required.Always)]
    public MeshCorrection[] meshes = Array.Empty<MeshCorrection>();

    internal static ModelCorrections Parse(string text)
    {
        var value = JsonConvert.DeserializeObject<ModelCorrections>(text, JsonSettings);
        if (value == null || value.schemaVersion != 1 || value.meshes == null || value.meshes.Length > 32)
            throw new InvalidDataException("Unsupported model corrections.");
        var keys = new HashSet<string>();
        foreach (MeshCorrection mesh in value.meshes)
        {
            if (mesh == null || string.IsNullOrEmpty(mesh.key) || !keys.Add(mesh.key) ||
                string.IsNullOrEmpty(mesh.fingerprint) || mesh.weights == null || mesh.poses == null || mesh.poses.Length > 24)
                throw new InvalidDataException("Invalid mesh correction.");
        }
        return value;
    }

    private sealed class CorrectionContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization serialization)
        {
            JsonProperty property = base.CreateProperty(member, serialization);
            if (member.DeclaringType == typeof(BoneWeight))
                property.PropertyName = "m_" + char.ToUpperInvariant(member.Name[0]) + member.Name.Substring(1);
            return property;
        }
    }

    internal MeshCorrection Find(Mesh mesh)
    {
        string key = Key(mesh);
        return Array.Find(meshes, item => item.key == key);
    }

    internal static string Key(Mesh mesh)
    {
        int separator = mesh.name.IndexOf('.');
        return separator < 0 ? mesh.name : mesh.name.Substring(separator + 1);
    }

    internal static string Fingerprint(Mesh mesh)
    {
        using (var data = new MemoryStream())
        using (var writer = new BinaryWriter(data))
        using (var sha = SHA256.Create())
        {
            writer.Write(mesh.vertexCount);
            foreach (Vector3 v in mesh.vertices) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
            foreach (Matrix4x4 m in mesh.bindposes)
                for (int i = 0; i < 16; i++) writer.Write(m[i]);
            foreach (BoneWeight w in mesh.boneWeights)
            {
                writer.Write(w.boneIndex0); writer.Write(w.boneIndex1); writer.Write(w.boneIndex2); writer.Write(w.boneIndex3);
                writer.Write(w.weight0); writer.Write(w.weight1); writer.Write(w.weight2); writer.Write(w.weight3);
            }
            writer.Write(mesh.subMeshCount);
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                int[] indices = mesh.GetIndices(i);
                writer.Write(indices.Length);
                foreach (int index in indices) writer.Write(index);
            }
            writer.Flush();
            return BitConverter.ToString(sha.ComputeHash(data.ToArray())).Replace("-", "").ToLowerInvariant();
        }
    }

    internal static void Validate(Mesh mesh, MeshCorrection correction)
    {
        if (correction.fingerprint != Fingerprint(mesh))
            throw new InvalidDataException("Model correction was made for a different mesh: " + correction.key);
        var vertices = new HashSet<int>();
        int bones = mesh.bindposes.Length;
        foreach (VertexWeightEdit edit in correction.weights)
        {
            if (edit == null || edit.vertex < 0 || edit.vertex >= mesh.vertexCount || !vertices.Add(edit.vertex))
                throw new InvalidDataException("Invalid correction vertex.");
            int[] indices = { edit.value.boneIndex0, edit.value.boneIndex1, edit.value.boneIndex2, edit.value.boneIndex3 };
            float[] weights = { edit.value.weight0, edit.value.weight1, edit.value.weight2, edit.value.weight3 };
            float sum = 0;
            var influences = new HashSet<int>();
            for (int i = 0; i < 4; i++)
            {
                if (!Finite(weights[i]) || weights[i] < 0 || indices[i] < 0 || indices[i] >= bones ||
                    weights[i] > 0 && !influences.Add(indices[i])) throw new InvalidDataException("Invalid skin weight.");
                sum += weights[i];
            }
            if (Mathf.Abs(sum - 1) > .0001f) throw new InvalidDataException("Skin weights must sum to one.");
        }
        var names = new HashSet<string>();
        foreach (PoseShapeEdit pose in correction.poses)
        {
            if (pose == null || string.IsNullOrWhiteSpace(pose.name) || !names.Add(pose.name) || pose.drivers == null ||
                pose.drivers.Length < 1 || pose.drivers.Length > 16 || pose.vertices == null || pose.deltas == null ||
                pose.vertices.Length != pose.deltas.Length || pose.vertices.Length > mesh.vertexCount)
                throw new InvalidDataException("Invalid pose correction.");
            foreach (PoseShapeDriver driver in pose.drivers)
            {
                if (driver == null) throw new InvalidDataException("Missing pose driver.");
                Quaternion q = driver.rotation;
                float norm = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
                if (driver.bone < 0 || driver.bone >= bones || string.IsNullOrEmpty(driver.name) ||
                    !Finite(driver.radius) || driver.radius < 1 || driver.radius > 90 ||
                    !Finite(driver.innerRadius) || driver.innerRadius < 0 || driver.innerRadius >= driver.radius ||
                    !Finite(norm) || Mathf.Abs(norm - 1) > .001f)
                    throw new InvalidDataException("Invalid pose driver.");
            }
            vertices.Clear();
            for (int i = 0; i < pose.vertices.Length; i++)
            {
                Vector3 delta = pose.deltas[i];
                if (pose.vertices[i] < 0 || pose.vertices[i] >= mesh.vertexCount || !vertices.Add(pose.vertices[i]) ||
                    !Finite(delta.x) || !Finite(delta.y) || !Finite(delta.z) || delta.magnitude > 2)
                    throw new InvalidDataException("Invalid pose displacement.");
            }
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

[Serializable]
internal sealed class MeshCorrection
{
    public string key = "";
    public string fingerprint = "";
    public VertexWeightEdit[] weights = Array.Empty<VertexWeightEdit>();
    public PoseShapeEdit[] poses = Array.Empty<PoseShapeEdit>();
}

[Serializable]
internal sealed class VertexWeightEdit
{
    public int vertex = 0;
    public BoneWeight value;
}

[Serializable]
internal sealed class PoseShapeEdit
{
    public string name = "";
    public string sourcePose = "";
    public PoseShapeDriver[] drivers = Array.Empty<PoseShapeDriver>();
    public int[] vertices = Array.Empty<int>();
    public Vector3[] deltas = Array.Empty<Vector3>();
}

[Serializable]
internal sealed class PoseShapeDriver
{
    public int bone = 0;
    public string name = "";
    public Quaternion rotation = Quaternion.identity;
    public float radius = 30;
    public float innerRadius = 0;
}
