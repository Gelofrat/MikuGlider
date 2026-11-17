using System;
using System.IO;
using System.Reflection;

internal static class Program
{
    private static int Main(string[] args)
    {
        string managed = args[1];
        string bepinex = Path.GetFullPath(Path.Combine(managed, "../../BepInEx/core"));
        AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
        {
            string name = new AssemblyName(eventArgs.Name).Name + ".dll";
            foreach (string directory in new[] { managed, bepinex })
            {
                string path = Path.Combine(directory, name);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        try
        {
            MetadataChecks.Run(args[0]);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
