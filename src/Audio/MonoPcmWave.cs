using System;
using System.IO;
using System.Text;

namespace MikuGlider;

internal sealed class MonoPcmWave
{
    internal const uint SampleRate = 48000;
    internal readonly float[] Samples;

    private MonoPcmWave(float[] samples) { Samples = samples; }

    internal static MonoPcmWave Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, true);
        if (!stream.CanSeek || stream.Length < 44 || stream.Length > 4 * 1024 * 1024 ||
            reader.ReadUInt32() != 0x46464952 || reader.ReadUInt32() != stream.Length - 8 ||
            reader.ReadUInt32() != 0x45564157)
            throw new InvalidDataException("Invalid Dalek RIFF/WAVE resource.");

        bool formatValid = false;
        byte[] data = null;
        while (stream.Position < stream.Length)
        {
            if (stream.Length - stream.Position < 8)
                throw new InvalidDataException("Truncated WAV chunk header.");
            uint id = reader.ReadUInt32();
            uint length = reader.ReadUInt32();
            long next = stream.Position + length + (length & 1);
            if (next > stream.Length)
                throw new InvalidDataException("Truncated WAV chunk.");
            if (id == 0x20746d66)
            {
                if (length < 16 || reader.ReadUInt16() != 1 || reader.ReadUInt16() != 1 ||
                    reader.ReadUInt32() != SampleRate || reader.ReadUInt32() != SampleRate * 2 ||
                    reader.ReadUInt16() != 2 || reader.ReadUInt16() != 16)
                    throw new InvalidDataException("Dalek audio must be mono 48 kHz PCM16 WAV.");
                formatValid = true;
            }
            else if (id == 0x61746164)
            {
                if (data != null || length == 0 || (length & 1) != 0)
                    throw new InvalidDataException("Invalid WAV sample data.");
                data = reader.ReadBytes((int)length);
            }
            stream.Position = next;
        }
        if (!formatValid || data == null)
            throw new InvalidDataException("Missing WAV format or samples.");
        var samples = new float[data.Length / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (short)(data[i * 2] | data[i * 2 + 1] << 8) / 32768f;
        return new MonoPcmWave(samples);
    }
}

internal sealed class MonoPcmPlayback
{
    private readonly float[] samples;
    private int position;

    internal MonoPcmPlayback(MonoPcmWave wave) { samples = wave.Samples; }

    // Wwise calls this on its audio thread. No Unity API, allocation or disk IO here.
    internal bool Fill(uint playingId, uint channel, float[] buffer)
    {
        Array.Clear(buffer, 0, buffer.Length);
        if (channel != 0 || position >= samples.Length)
            return false;
        int count = Math.Min(buffer.Length, samples.Length - position);
        Array.Copy(samples, position, buffer, 0, count);
        position += count;
        // Return true for the final partial buffer so its tail is actually played.
        return true;
    }
}
