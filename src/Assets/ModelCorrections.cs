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
            if (pose.boneRotations == null) throw new InvalidDataException("Missing pose bone rotations.");
            if (pose.gripAnchor != null)
            {
                var anchor = pose.gripAnchor;
                Vector3 contact = anchor.contactPosition ?? anchor.position;
                float maxReleaseDistance = correction.key == "arm-right" && bones == 28 ? .35f : .1f;
                var q = anchor.rotation;
                float norm = q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;
                if (anchor.bone != 3 || string.IsNullOrEmpty(anchor.name) || string.IsNullOrEmpty(anchor.path) ||
                    !Finite(anchor.position.x) || !Finite(anchor.position.y) || !Finite(anchor.position.z) ||
                    !Finite(contact.x) || !Finite(contact.y) || !Finite(contact.z) ||
                    !Finite(norm) || Mathf.Abs(norm-1)>.001f || !Finite(anchor.innerDistance) ||
                    !Finite(anchor.outerDistance) || anchor.innerDistance<=0 || anchor.outerDistance<=anchor.innerDistance ||
                    anchor.outerDistance>maxReleaseDistance)
                    throw new InvalidDataException("Invalid hand grip anchor.");
                bool wristAlignment = correction.key == "arm-left wrist transition" && bones == 37 &&
                    anchor.name == "jnt_L_handRoot_01" && anchor.rotateAtWrist;
                if (anchor.followArm && !wristAlignment &&
                    (correction.key != "arm-right" || bones != 28 || anchor.name != "jnt_R_handRoot_01"))
                    throw new InvalidDataException("Arm alignment requires a first-person arm rig.");
                if (anchor.rotateAtWrist && (!wristAlignment || !anchor.followArm))
                    throw new InvalidDataException("Wrist rotation requires the aligned left forearm.");
                if (anchor.followArm && anchor.weightOnly)
                    throw new InvalidDataException("A weight-only contact cannot move the arm.");
                if (anchor.releaseOnly && !anchor.weightOnly)
                    throw new InvalidDataException("A release pose must use a weight-only grip anchor.");
                if (anchor.sustainOnContact && (!anchor.weightOnly || anchor.releaseOnly))
                    throw new InvalidDataException("A sustained correction requires a holding contact.");
            }
            var editedBones = new HashSet<int>();
            foreach (PoseBoneRotation edit in pose.boneRotations)
            {
                bool bodyFinger = correction.key == "body" && bones == 72 && edit != null &&
                    (edit.bone >= 17 && edit.bone <= 35 || edit.bone >= 40 && edit.bone <= 58) &&
                    edit.name != null && edit.name.StartsWith("joint_", StringComparison.Ordinal);
                bool palm = bones == 28 && correction.key == "arm-right" && edit != null && edit.bone == 3 && edit.name == "jnt_R_handRoot_01";
                bool fingerRoot = bones == 28 && correction.key == "arm-right" && edit != null &&
                    (edit.bone == 4 && edit.name == "jnt_R_IndexA" || edit.bone == 19 && edit.name == "jnt_R_PinkyA");
                bool leftThumbRoot = bones == 37 && correction.key == "arm-left wrist transition" && edit != null &&
                    edit.bone == 24 && edit.name == "jnt_L_ThumbA";
                bool wrist = bones == 28 && correction.key == "arm-right" && edit != null && edit.bone == 2 &&
                    edit.name == "jnt_R_hand_01" && pose.gripAnchor?.followArm == true;
                if (edit == null || (!bodyFinger && !palm && !wrist && (edit.bone < 4 || edit.bone >= Math.Min(28, bones))) ||
                    string.IsNullOrEmpty(edit.name) || !editedBones.Add(edit.bone))
                    throw new InvalidDataException("Invalid finger pose bone.");
                if (!ValidRotation(edit.rotation, edit.absolute) || (edit.contactRotation.HasValue &&
                    (pose.gripAnchor == null || !ValidRotation(edit.contactRotation.Value, edit.absolute))))
                    throw new InvalidDataException("Invalid finger pose rotation.");
                if (!Finite(edit.motionFraction) || edit.motionFraction < 0 || edit.motionFraction > 1 ||
                    (edit.motionReference.HasValue && (edit.absolute || !ValidRotation(edit.motionReference.Value, true))) ||
                    (edit.motionFraction != 1 && !edit.motionReference.HasValue))
                    throw new InvalidDataException("Invalid finger animation range.");
                if (edit.position.HasValue)
                {
                    Vector3 p = edit.position.Value;
                    if ((!bodyFinger && !palm && !fingerRoot && !leftThumbRoot) || !Finite(p.x) || !Finite(p.y) || !Finite(p.z) || p.magnitude > .5f)
                        throw new InvalidDataException("Invalid hand joint position.");
                }
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

    private static bool ValidRotation(Quaternion q, bool absolute)
    {
        float norm = q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w;
        return Finite(norm) && Mathf.Abs(norm - 1) <= .001f && (absolute || Quaternion.Angle(Quaternion.identity, q) <= 90);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

[Serializable]
internal sealed class MeshCorrection
{
    public string key = "";
    public string fingerprint = "";
    public string weapon = "";
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
    // Empty inherits the mesh scope, preserving existing correction profiles.
    public string weapon = "";
    public PoseShapeDriver[] drivers = Array.Empty<PoseShapeDriver>();
    // Independent regions (e.g. thumb and four-finger grip) must not halve each
    // other's weights. Empty preserves the original shared normalization group.
    public string group = "";
    public int[] vertices = Array.Empty<int>();
    public Vector3[] deltas = Array.Empty<Vector3>();
    public PoseBoneRotation[] boneRotations = Array.Empty<PoseBoneRotation>();
    public PoseGripAnchor gripAnchor = null;
}

[Serializable]
internal sealed class PoseGripAnchor
{
    public int bone = 3;
    public string name = "";
    public string path = "";
    public Vector3 position = Vector3.zero;
    // Native palm position on the handle, used when the fitted palm sits at a different angle.
    // Native palm location on the handle, used to detect contact when a fitted grip shifts the palm.
    public Vector3? contactPosition = null;
    public Quaternion rotation = Quaternion.identity;
    // A closed viewmodel grip must carry the forearm with it, rather than
    // pulling the palm away from a robot wrist that twists independently.
    public bool followArm = false;
    // Translate the whole arm to the contact point and rotate at the existing wrist joint.
    public bool rotateAtWrist = false;
    // Use the distance to drive the pose weight without repositioning the palm.
    public bool weightOnly = false;
    // Fade release-specific finger corrections out as the hand takes the handle.
    public bool releaseOnly = false;
    // Keep the hand's calibrated soft tissue in its holding pose until release.
    public bool sustainOnContact = false;
    // In model units. Release the contact smoothly when the animation moves
    // the hand away; never pin a detached hand to the weapon.
    public float innerDistance = .02f;
    public float outerDistance = .06f;
}

[Serializable]
internal sealed class PoseBoneRotation
{
    public int bone = 0;
    public string name = "";
    public Quaternion rotation = Quaternion.identity;
    // Optional rotation at full handle contact. The base rotation follows free movement.
    public Quaternion? contactRotation = null;
    // Fraction of the source finger's excursion from its recorded holding pose.
    public Quaternion? motionReference = null;
    public float motionFraction = 1f;
    public bool absolute = false;
    // Optional authored joint centre on the renderer copy, in parent space.
    public Vector3? position = null;
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
