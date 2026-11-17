using System;
using System.IO;
using System.Text;

namespace MikuGlider.Diagnostics;

internal sealed class ControlCommand
{
    public string id { get; set; }
    public string action { get; set; }
    public string label { get; set; }
    public int durationSeconds { get; set; }
}

internal sealed class LocalControl
{
    private readonly string root;
    private string lastId;
    internal string LastId => lastId;
    internal string LastResult { get; private set; }

    internal LocalControl(string root)
    {
        this.root = root;
        Directory.CreateDirectory(Path.Combine(root, "control"));
        if (Read() != null)
            LastResult = "ignored_existing_command";
    }

    internal ControlCommand Read()
    {
        string path = Path.Combine(root, "control", "command.json");
        if (!File.Exists(path))
            return null;
        var file = new FileInfo(path);
        if (file.Length > 4096)
        {
            LastResult = "command_too_large";
            return null;
        }
        ControlCommand command;
        try
        {
            command = CaptureJson.Read<ControlCommand>(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception error) when (error is IOException || error is Newtonsoft.Json.JsonException)
        {
            LastResult = "incomplete_or_invalid_command";
            return null;
        }
        if (command == null || string.IsNullOrWhiteSpace(command.id) || command.id.Length > 64 || command.id == lastId)
            return null;
        lastId = command.id;
        if (command.label?.Length > 120)
        {
            LastResult = "label_too_long";
            return null;
        }
        LastResult = "received";
        return command;
    }

    internal void Result(string result)
    {
        LastResult = result;
    }

    internal void Status(object status)
    {
        string destination = Path.Combine(root, "status.json");
        string temporary = destination + ".tmp";
        File.WriteAllText(temporary, CaptureJson.Write(status), new UTF8Encoding(false));
        if (File.Exists(destination))
            File.Replace(temporary, destination, null);
        else
            File.Move(temporary, destination);
    }
}
