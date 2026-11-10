using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MikuGlider;

internal sealed class AssetBinaryReader : BinaryReader
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    internal AssetBinaryReader(Stream stream) : base(stream, Utf8, true)
    {
    }

    internal void ExpectHeader(string expected)
    {
        if (Encoding.ASCII.GetString(ReadExactly(expected.Length)) != expected)
            throw new InvalidDataException("Unsupported embedded asset header; expected " + expected + ".");
    }

    internal byte[] ReadExactly(int length)
    {
        RequireRemaining(length);
        byte[] bytes = ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException("Truncated embedded asset.");

        return bytes;
    }

    internal int ReadCount(int minimum, int maximum, string field)
    {
        int value = ReadInt32();
        if (value < minimum || value > maximum)
            throw new InvalidDataException("Embedded asset " + field + " is outside the supported range.");

        return value;
    }

    internal string ReadName(int maximumBytes = 128)
    {
        int length = ReadCount(1, maximumBytes, "name length");
        string name = Utf8.GetString(ReadExactly(length));
        if (string.IsNullOrWhiteSpace(name) || name.IndexOf('\0') >= 0)
            throw new InvalidDataException("Invalid embedded asset name.");

        return name;
    }

    internal float ReadFinite()
    {
        float value = ReadSingle();
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidDataException("Non-finite coordinate, rotation or weight in embedded asset.");

        return value;
    }

    internal Vector3 ReadVector3()
    {
        return new Vector3(ReadFinite(), ReadFinite(), ReadFinite());
    }

    internal Vector2 ReadVector2()
    {
        return new Vector2(ReadFinite(), ReadFinite());
    }

    internal Quaternion ReadRotation()
    {
        var rotation = new Quaternion(ReadFinite(), ReadFinite(), ReadFinite(), ReadFinite());
        float squaredLength = rotation.x * rotation.x + rotation.y * rotation.y +
            rotation.z * rotation.z + rotation.w * rotation.w;
        if (Math.Abs(squaredLength - 1f) > 0.001f)
            throw new InvalidDataException("Invalid embedded animation quaternion.");

        return rotation.normalized;
    }

    internal Matrix4x4 ReadMatrix()
    {
        var matrix = new Matrix4x4();
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
                matrix[row, column] = ReadFinite();
        }

        return matrix;
    }

    internal void RequireRemaining(long length)
    {
        if (length < 0 || length > BaseStream.Length - BaseStream.Position)
            throw new EndOfStreamException("Truncated embedded asset.");
    }

    internal void RequireEnd()
    {
        if (BaseStream.Position != BaseStream.Length)
            throw new InvalidDataException("Unexpected trailing data in embedded asset.");
    }
}
