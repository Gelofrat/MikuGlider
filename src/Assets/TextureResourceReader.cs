using System;
using System.IO;
using UnityEngine;

namespace MikuGlider;

internal static class TextureResourceReader
{
    private const int MaximumBytes = 32 * 1024 * 1024;
    private const int MaximumDimension = 4096;
    private const long MaximumTotalPixels = 32L * 1024 * 1024;
    private static readonly Func<Texture2D, byte[], bool, bool> LoadImage = ResolveImageLoader();

    internal static Texture2D Read(EmbeddedResources resources, string name, string resourceSuffix,
        bool linear, ref long totalPixels)
    {
        byte[] png;
        using (Stream stream = resources.Open("Texture." + resourceSuffix, MaximumBytes))
        using (var reader = new AssetBinaryReader(stream))
            png = reader.ReadExactly(checked((int)stream.Length));

        ValidateHeader(png, name);
        uint width = ReadBigEndianUInt32(png, 16);
        uint height = ReadBigEndianUInt32(png, 20);
        if (width == 0 || height == 0 || width > MaximumDimension || height > MaximumDimension)
            throw new InvalidDataException("Embedded texture dimensions exceed the limit: " + name);

        totalPixels = checked(totalPixels + (long)width * height);
        if (totalPixels > MaximumTotalPixels)
            throw new InvalidDataException("Embedded textures exceed the total pixel limit.");

        bool mipmaps = name != "icon";
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipmaps, linear)
        {
            name = name,
            hideFlags = HideFlags.DontUnloadUnusedAsset,
            filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear,
            anisoLevel = mipmaps ? 8 : 1,
            wrapMode = TextureWrapMode.Clamp
        };

        try
        {
            if (!LoadImage(texture, png, false) || texture.width != width || texture.height != height)
                throw new InvalidDataException("Failed to decode embedded texture: " + name);

            return texture;
        }
        catch
        {
            UnityEngine.Object.Destroy(texture);
            throw;
        }
    }

    private static void ValidateHeader(byte[] png, string name)
    {
        if (png.Length < 33 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71 ||
            png[4] != 13 || png[5] != 10 || png[6] != 26 || png[7] != 10 ||
            ReadBigEndianUInt32(png, 8) != 13 || png[12] != 73 || png[13] != 72 || png[14] != 68 || png[15] != 82)
            throw new InvalidDataException("Embedded texture is not a PNG with an IHDR header: " + name);
    }

    private static uint ReadBigEndianUInt32(byte[] bytes, int offset)
    {
        return ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) |
            ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    private static Func<Texture2D, byte[], bool, bool> ResolveImageLoader()
    {
        Type type = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", true);
        var method = type.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
        if (method == null)
            throw new MissingMethodException(type.FullName, "LoadImage(Texture2D, byte[], bool)");

        return (Func<Texture2D, byte[], bool, bool>)Delegate.CreateDelegate(
            typeof(Func<Texture2D, byte[], bool, bool>), method);
    }
}
