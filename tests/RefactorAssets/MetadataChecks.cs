using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;

internal static class MetadataChecks
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    internal static void Run(string workspace)
    {
        Assembly plugin = Assembly.LoadFrom(Path.Combine(workspace, "bin/Release/MikuGlider.dll"));
        ValidatePlugin(plugin);
        if (plugin.GetReferencedAssemblies().Any(a => a.Name == "UnityEngine.ClothModule"))
            throw new Exception("Native Cloth must not be referenced by the mod.");
        int patchCount = 0;
        int injectedFields = 0;
        var targets = new HashSet<string>();
        foreach (Type type in plugin.GetTypes())
        {
            foreach (CustomAttributeData attribute in type.GetCustomAttributesData())
            {
                if (attribute.AttributeType.FullName != "HarmonyLib.HarmonyPatch") continue;
                var args = attribute.ConstructorArguments;
                Type target = (Type)args[0].Value;
                string name = (string)args[1].Value;
                bool getter = args.Count > 2 && Enum.GetName(args[2].ArgumentType, args[2].Value) == "Getter";
                MethodInfo method = getter
                    ? target.GetProperty(name, Flags)?.GetGetMethod(true)
                    : target.GetMethods(Flags).Single(m => m.Name == name);
                if (method == null) throw new Exception("Missing patch target " + target.Name + "." + name);
                targets.Add(target.FullName + "." + method.Name);
                foreach (MethodInfo patch in type.GetMethods(Flags).Where(m => m.Name == "Prefix" || m.Name == "Postfix" || m.Name == "Finalizer"))
                {
                    foreach (ParameterInfo parameter in patch.GetParameters())
                    {
                        if (!parameter.Name.StartsWith("___", StringComparison.Ordinal))
                        {
                            Type argumentType = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType;
                            if (parameter.Name == "__instance" && !argumentType.IsAssignableFrom(target))
                                throw new Exception("Invalid Harmony instance: " + type.Name);
                            if (!parameter.Name.StartsWith("__", StringComparison.Ordinal) &&
                                !method.GetParameters().Any(p => p.Name == parameter.Name &&
                                    (p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType) == argumentType))
                                throw new Exception("Invalid Harmony argument: " + type.Name + "." + parameter.Name);
                            continue;
                        }
                        FieldInfo field = FindField(target, parameter.Name.Substring(3));
                        if (field == null || field.FieldType != parameter.ParameterType)
                            throw new Exception("Invalid Harmony field injection " + type.Name + "." + parameter.Name);
                        injectedFields++;
                    }
                }
                patchCount++;
            }
        }
        // Check required integration contracts, not an arbitrary total patch count.
        foreach (string required in new[] {
            "Pigeon.Movement.Player.ApplySkins", "PlayerData.OnAwake", "SkinInstance.get_Upgrade",
            "ThirdPersonAnimator.UpdateMesh", "ThirdPersonAnimator.HandleInterrupts",
            "MenuRig.LateUpdate", "MenuRig.OnDisable", "CharacterSelectWindow.ApplySkin",
            "PlayerAnimation.OverrideSkinnedMeshes", "GearSelectionWindow.SetupSkinMaterials",
            "Wingsuit.OnSkinsApplied", "PlayerAnimation.ImportAnimationSet",
            "Pigeon.Movement.PlayerLook.DisableThirdPerson", "Pigeon.Movement.PlayerLook.EnableThirdPerson",
            "Pigeon.Movement.PlayerLook.HandleCameraControl", "MissionHUD.ShowNewObjective" })
            if (!targets.Contains(required)) throw new Exception("Missing integration: " + required);
        Type previewPatch = plugin.GetType("MikuGlider.CharacterPreviewMaterialsPatch", true);
        Type selectionWindow = Assembly.Load("Assembly-CSharp").GetType("CharacterSelectWindow", true);
        MethodInfo applyPreview = selectionWindow.GetMethod("ApplySkin", Flags);
        foreach (ParameterInfo argument in previewPatch.GetMethod("Prefix", Flags).GetParameters())
            if (!applyPreview.GetParameters().Any(p => p.Name == argument.Name && p.ParameterType == argument.ParameterType))
                throw new Exception("Invalid preview patch argument: " + argument.Name);
        Type access = plugin.GetType("MikuGlider.NativeSkinAccess", true);
        access.GetMethod("ValidateLayout", Flags).Invoke(null, null);
        Type cameraAccess = plugin.GetType("MikuGlider.NativeCameraAccess", true);
        foreach (FieldInfo cached in cameraAccess.GetFields(Flags).Where(f => typeof(MemberInfo).IsAssignableFrom(f.FieldType)))
            if (cached.GetValue(null) == null) Console.WriteLine("MISSING_CAMERA_MEMBER " + cached.Name);
        cameraAccess.GetMethod("ValidateLayout", Flags).Invoke(null, null);
        Type weaponAccess = plugin.GetType("MikuGlider.NativeWeaponAccess", true);
        foreach (FieldInfo cached in weaponAccess.GetFields(Flags).Where(f => typeof(MemberInfo).IsAssignableFrom(f.FieldType)))
            if (cached.GetValue(null) == null) throw new Exception("Missing weapon member " + cached.Name);
        Type cameraPatch = plugin.GetType("MikuGlider.CameraControlPatch", true);
        MethodInfo finalizer = cameraPatch.GetMethod("Finalizer", Flags);
        if (finalizer == null || finalizer.ReturnType != typeof(Exception))
            throw new Exception("Camera frame cleanup is missing.");
        Type skirtType = plugin.GetType("MikuGlider.SkirtGarmentVisual", true);
        Type rigType = plugin.GetType("MikuGlider.PoseGarmentRig", true);
        if (skirtType.GetField("poseRig", Flags)?.FieldType != rigType ||
            skirtType.GetProperty("UsesPoseRig", Flags)?.PropertyType != typeof(bool))
            throw new Exception("Missing pose garment integration.");
        Type playerType = Assembly.Load("Assembly-CSharp").GetType("Pigeon.Movement.Player", true);
        if (playerType.GetEvent("ModifyRawMoveInput") == null)
            throw new Exception("Missing native movement input modifier event.");
        // The project is the resource manifest. Do not duplicate its file paths here.
        var project = XDocument.Load(Path.Combine(workspace, "MikuGlider.csproj"));
        var files = project.Descendants("EmbeddedResource").ToDictionary(
            item => (string)item.Attribute("LogicalName"), item => ((string)item.Attribute("Include")).Replace('\\', '/'));
        if (!plugin.GetManifestResourceNames().OrderBy(x => x).SequenceEqual(files.Keys.OrderBy(x => x)))
            throw new Exception("Packaged resources differ from the project manifest.");
        if (!files.ContainsKey("MikuGlider.Audio.DalekExterminate")) throw new Exception("Missing Dalek audio resource.");
        Type catalog = plugin.GetType("MikuGlider.SkinCatalog", true);
        object[] variants = ((System.Collections.IEnumerable)catalog.GetProperty("Variants", Flags).GetValue(null)).Cast<object>().ToArray();
        if (variants.Length != 3) throw new Exception("Exactly three skin variants must be registered.");
        Type definition = variants[0].GetType();
        if (variants.Select(v => definition.GetProperty("NumericId", Flags).GetValue(v)).Distinct().Count() != variants.Length)
            throw new Exception("Skin variants must have unique upgrade IDs.");
        foreach (object variant in variants)
        {
            string prefix = (string)definition.GetProperty("ResourcePrefix", Flags).GetValue(variant);
            foreach (string suffix in new[] { "Model", "Hair", "Definition", "Corrections", "Texture.BodyAlbedo", "Texture.FaceAlbedo", "Texture.Icon" })
                if (!files.ContainsKey(prefix + "." + suffix)) throw new Exception("Missing skin resource: " + prefix + "." + suffix);
        }
        using (var hash = SHA256.Create())
        {
            foreach (var pair in files)
            {
                using (Stream embedded = plugin.GetManifestResourceStream(pair.Key))
                using (Stream local = File.OpenRead(Path.Combine(workspace, pair.Value)))
                    if (embedded == null || !hash.ComputeHash(embedded).SequenceEqual(hash.ComputeHash(local)))
                        throw new Exception("Embedded resource mismatch: " + pair.Key);
            }
        }
        Console.WriteLine("METADATA_CHECKS_PASS patches=" + patchCount + " fields=" + injectedFields + " resources=" + files.Count);
    }

    private static void ValidatePlugin(Assembly plugin)
    {
        Type entry = plugin.GetType("MikuGlider.MikuGliderPlugin", true);
        CustomAttributeData registration = entry.GetCustomAttributesData()
            .Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        if ((string)registration.ConstructorArguments[0].Value != "horse.mycopunk.mikuglider")
            throw new Exception("The persistent plugin ID changed.");
        string version = (string)registration.ConstructorArguments[2].Value;
        if (Version.Parse(version) != new Version(plugin.GetName().Version.ToString(3)))
            throw new Exception("Plugin and assembly versions differ.");
        CustomAttributeData mod = entry.GetCustomAttributesData()
            .Single(a => a.AttributeType.Name == "MycoModAttribute");
        if (Convert.ToInt32(mod.ConstructorArguments[1].Value) != 0)
            throw new Exception("Shared skin registration must not be client-side only.");
        CustomAttributeData framework = plugin.GetCustomAttributesData()
            .Single(a => a.AttributeType.FullName == "System.Runtime.Versioning.TargetFrameworkAttribute");
        if ((string)framework.ConstructorArguments[0].Value != ".NETFramework,Version=v4.8")
            throw new Exception("Unexpected runtime target.");
        if (plugin.GetTypes().Any(t => t.Namespace != "MikuGlider" &&
            !t.FullName.StartsWith("<", StringComparison.Ordinal) &&
            !t.FullName.StartsWith("Microsoft.CodeAnalysis.", StringComparison.Ordinal) &&
            !t.FullName.StartsWith("System.Runtime.CompilerServices.", StringComparison.Ordinal)))
            throw new Exception("Unexpected namespace in the plugin assembly.");
    }

    private static FieldInfo FindField(Type type, string name)
    {
        while (type != null)
        {
            FieldInfo field = type.GetField(name, Flags | BindingFlags.DeclaredOnly);
            if (field != null) return field;
            type = type.BaseType;
        }
        return null;
    }
}
