using System;
using System.IO;
using System.Text;

namespace MikuGlider;

internal sealed class EmbeddedResources
{
    private readonly string prefix;

    internal EmbeddedResources(string prefix)
    {
        this.prefix = prefix;
    }

    internal Stream Open(string suffix, int maximumBytes)
    {
        string name = prefix + "." + suffix;
        Stream stream = typeof(EmbeddedResources).Assembly.GetManifestResourceStream(name);
        if (stream == null)
            throw new InvalidDataException("Missing embedded resource: " + name);

        if (stream.CanSeek && stream.Length > 0 && stream.Length <= maximumBytes)
            return stream;

        stream.Dispose();
        throw new InvalidDataException("Invalid embedded resource size: " + name);
    }

    internal string ReadText(string suffix, int maximumBytes)
    {
        using (Stream stream = Open(suffix, maximumBytes))
        using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
        {
            string text = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException("The embedded resource is empty: " + suffix);

            return text;
        }
    }
}
