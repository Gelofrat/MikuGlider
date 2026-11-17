using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MikuGlider;
using UnityEngine;

internal static class Program
{
    private static int Main(string[] args)
    {
        string workspace = args[0];
        string managed = args[1];
        string bepinex = Path.GetFullPath(Path.Combine(managed, "../../BepInEx/core"));
        AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
        {
            string path = Path.Combine(managed, new AssemblyName(eventArgs.Name).Name + ".dll");
            if (File.Exists(path)) return Assembly.LoadFrom(path);
            path = Path.Combine(bepinex, new AssemblyName(eventArgs.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try
        {
            Checks.Run(workspace);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}

internal static class Checks
{
    private static int assertions;

    internal static void Run(string workspace)
    {
        byte[] model = File.ReadAllBytes(Path.Combine(workspace, "assets/V1/MikuGlider.mikumesh"));
        IReadOnlyList<MeshData> meshes = MeshResourceReader.Read(new MemoryStream(model));
        Assert(meshes.Count == 14, "mesh count");
        Assert(meshes.First(m => m.Name == "body").BindPoses.Length == 72, "body skeleton");
        Assert(meshes.First(m => m.Name == "arm-left").BindPoses.Length == 28, "FP skeleton");
        Assert(SerializeMeshes(meshes).SequenceEqual(model), "every mesh value round-trips exactly");

        RejectMesh(ChangeInt(model, 8, 0));
        RejectMesh(model.Take(model.Length - 1).ToArray());
        RejectMesh(model.Concat(new byte[] { 0 }).ToArray());
        RejectMesh(ChangeInt(model, 0, 0));
        int nameLength = BitConverter.ToInt32(model, 12);
        int counts = 16 + nameLength;
        int bones = BitConverter.ToInt32(model, counts + 8);
        int vertices = BitConverter.ToInt32(model, counts);
        int firstVertex = counts + 12 + bones * 64;
        RejectMesh(ChangeInt(model, counts, 250001));
        RejectMesh(ChangeFloat(model, firstVertex, float.NaN));
        RejectMesh(ChangeFloat(model, firstVertex + 32, 2f));
        RejectMesh(ChangeInt(model, firstVertex + 48, bones));
        RejectMesh(ChangeInt(model, firstVertex + vertices * 64 + 4, vertices));
        RejectMesh(SerializeMeshes(new[] { meshes[0], meshes[0] }));

        byte[] hair = File.ReadAllBytes(Path.Combine(workspace, "assets/V1/MikuGlider.mikuhair"));
        HairAnimationData animation = HairResourceReader.Read(new MemoryStream(hair));
        VerifyHair(hair, animation, 115);
        PhysicsChecks.Run(animation, meshes.First(m => m.Name == "skirt"), false);
        RejectHair(ChangeInt(hair, 12, 0));
        RejectHair(hair.Take(hair.Length - 1).ToArray());
        RejectHair(hair.Concat(new byte[] { 0 }).ToArray());
        RejectHair(ChangeInt(hair, 8, 257));
        int firstBoneName = BitConverter.ToInt32(hair, 16);
        int firstRotation = 20 + firstBoneName + 12;
        RejectHair(ChangeFloat(hair, firstRotation, 9f));
        RejectHair(ChangeFloat(hair, firstRotation + 16, -1f));
        byte[] v2Model = File.ReadAllBytes(Path.Combine(workspace, "assets/v2/MikuGliderV2.mikumesh"));
        IReadOnlyList<MeshData> v2Meshes = MeshResourceReader.Read(new MemoryStream(v2Model));
        Assert(v2Meshes.Count == 15, "v2 mesh count");
        Assert(v2Meshes.First(m => m.Name == "sleeves").BindPoses.Length == 72, "v2 sleeve skeleton");
        Assert(v2Meshes.First(m => m.Name == "body").BindPoses.Length == 72, "v2 body skeleton");
        Assert(v2Meshes.First(m => m.Name == "arm-left").BindPoses.Length == 28, "v2 FP skeleton");
        Assert(SerializeMeshes(v2Meshes).SequenceEqual(v2Model), "v2 mesh values round-trip");
        byte[] v2Hair = File.ReadAllBytes(Path.Combine(workspace, "assets/v2/MikuGliderV2.mikuhair"));
        HairAnimationData v2Animation = HairResourceReader.Read(new MemoryStream(v2Hair));
        VerifyHair(v2Hair, v2Animation, 27);
        PhysicsChecks.Run(v2Animation, v2Meshes.First(m => m.Name == "skirt"), false);
        byte[] v3Model = File.ReadAllBytes(Path.Combine(workspace, "assets/v3/MikuGliderV3.mikumesh"));
        IReadOnlyList<MeshData> v3Meshes = MeshResourceReader.Read(new MemoryStream(v3Model));
        Assert(v3Meshes.Count == 15, "v3 mesh count");
        Assert(v3Meshes.First(m => m.Name == "sleeves").BindPoses.Length == 72, "v3 sleeve skeleton");
        Assert(v3Meshes.First(m => m.Name == "body").BindPoses.Length == 72, "v3 body skeleton");
        Assert(v3Meshes.First(m => m.Name == "arm-right").BindPoses.Length == 28, "v3 FP skeleton");
        Assert(SerializeMeshes(v3Meshes).SequenceEqual(v3Model), "v3 mesh values round-trip");
        byte[] v3Hair = File.ReadAllBytes(Path.Combine(workspace, "assets/v3/MikuGliderV3.mikuhair"));
        HairAnimationData v3Animation = HairResourceReader.Read(new MemoryStream(v3Hair));
        VerifyHair(v3Hair, v3Animation, 27);
        PhysicsChecks.Run(v3Animation, v3Meshes.First(m => m.Name == "skirt"), false);
        Console.WriteLine("ASSET_CHECKS_PASS assertions=" + assertions + " meshes=" + meshes.Count + " frames=210");
    }

    private static void VerifyHair(byte[] bytes, HairAnimationData animation, int expectedBones)
    {
        using (var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
        {
            reader.ReadBytes(8);
            int count = reader.ReadInt32();
            Assert(count == expectedBones && count == animation.Parents.Length, "hair skeleton");
            for (int index = 0; index < count; index++)
            {
                Assert(reader.ReadInt32() == animation.Parents[index], "hair parent");
                Assert(ReadName(reader) == animation.Names[index], "hair bone name");
                MatchVector(reader, animation.Positions[index]);
                MatchRotation(reader, animation.RestRotations[index]);
                MatchVector(reader, animation.Scales[index]);
            }

            Assert(reader.ReadInt32() == 4, "hair clip count");
            int totalFrames = 0;
            foreach (HairAnimationState state in Enum.GetValues(typeof(HairAnimationState)))
            {
                ReadName(reader);
                int frameCount = reader.ReadInt32();
                float fps = reader.ReadSingle();
                HairAnimationClip clip = animation.GetClip(state);
                var frames = (Quaternion[][])typeof(HairAnimationClip).GetField("frames", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(clip);
                Assert(frames.Length == frameCount, "hair frame count");
                Assert(Math.Abs(clip.Duration - (frameCount - 1) / fps) < 0.00001, "hair duration");
                foreach (Quaternion[] frame in frames)
                {
                    foreach (Quaternion rotation in frame)
                        MatchRotation(reader, rotation);
                }

                totalFrames += frameCount;
            }

            Assert(totalFrames == 210, "all hair frames");
            Assert(reader.BaseStream.Position == bytes.Length, "hair end");
        }
    }

    private static byte[] SerializeMeshes(IReadOnlyList<MeshData> meshes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("MIKUMSH1"));
            writer.Write(meshes.Count);
            foreach (MeshData mesh in meshes)
            {
                byte[] name = Encoding.UTF8.GetBytes(mesh.Name);
                writer.Write(name.Length);
                writer.Write(name);
                writer.Write(mesh.Vertices.Length);
                writer.Write(mesh.Submeshes.Length);
                writer.Write(mesh.BindPoses.Length);
                foreach (Matrix4x4 bind in mesh.BindPoses)
                {
                    for (int row = 0; row < 4; row++)
                        for (int column = 0; column < 4; column++)
                            writer.Write(bind[row, column]);
                }

                for (int index = 0; index < mesh.Vertices.Length; index++)
                {
                    WriteVector(writer, mesh.Vertices[index]);
                    WriteVector(writer, mesh.Normals[index]);
                    writer.Write(mesh.UV[index].x);
                    writer.Write(mesh.UV[index].y);
                    BoneWeight weight = mesh.Weights == null ? default : mesh.Weights[index];
                    writer.Write(weight.weight0);
                    writer.Write(weight.weight1);
                    writer.Write(weight.weight2);
                    writer.Write(weight.weight3);
                    writer.Write(weight.boneIndex0);
                    writer.Write(weight.boneIndex1);
                    writer.Write(weight.boneIndex2);
                    writer.Write(weight.boneIndex3);
                }

                foreach (int[] submesh in mesh.Submeshes)
                {
                    writer.Write(submesh.Length);
                    foreach (int index in submesh)
                        writer.Write(index);
                }
            }

            return stream.ToArray();
        }
    }

    private static void WriteVector(BinaryWriter writer, Vector3 vector)
    {
        writer.Write(vector.x);
        writer.Write(vector.y);
        writer.Write(vector.z);
    }

    private static void MatchVector(BinaryReader reader, Vector3 vector)
    {
        Match(reader.ReadSingle(), vector.x);
        Match(reader.ReadSingle(), vector.y);
        Match(reader.ReadSingle(), vector.z);
    }

    private static void MatchRotation(BinaryReader reader, Quaternion rotation)
    {
        Match(reader.ReadSingle(), rotation.x);
        Match(reader.ReadSingle(), rotation.y);
        Match(reader.ReadSingle(), rotation.z);
        Match(reader.ReadSingle(), rotation.w);
    }

    private static string ReadName(BinaryReader reader)
    {
        return Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
    }

    private static byte[] ChangeInt(byte[] source, int offset, int value)
    {
        byte[] bytes = (byte[])source.Clone();
        Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
        return bytes;
    }

    private static byte[] ChangeFloat(byte[] source, int offset, float value)
    {
        byte[] bytes = (byte[])source.Clone();
        Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
        return bytes;
    }

    private static void RejectMesh(byte[] bytes)
    {
        Reject(() => MeshResourceReader.Read(new MemoryStream(bytes)));
    }

    private static void RejectHair(byte[] bytes)
    {
        Reject(() => HairResourceReader.Read(new MemoryStream(bytes)));
    }

    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is IOException || error is InvalidDataException)
        {
            assertions++;
            return;
        }

        throw new Exception("Malformed input was accepted.");
    }

    private static void Match(float expected, float actual)
    {
        Assert(Math.Abs(expected - actual) < 0.000001, "hair sample changed");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);

        assertions++;
    }
}
