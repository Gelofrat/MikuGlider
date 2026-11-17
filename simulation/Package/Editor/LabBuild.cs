using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Horse.MikuPhysicsLab.Editor
{
    public static class LabBuild
    {
        [MenuItem("Miku Physics Lab/Create inspection scene")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Miku Physics Lab", typeof(LabController));
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/MikuPhysicsLab.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/MikuPhysicsLab.unity", true) };
        }

        [MenuItem("Miku Physics Lab/Build standalone viewer")]
        public static void Build()
        {
            CreateScene();
            PlayerSettings.companyName = "Horse";
            PlayerSettings.productName = "Miku Physics Lab";
            PlayerSettings.defaultScreenWidth = 1440;
            PlayerSettings.defaultScreenHeight = 1000;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(UnityEditor.Build.NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Build"));
            Directory.CreateDirectory(directory);
            var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/MikuPhysicsLab.unity" },
                locationPathName = Path.Combine(directory, "MikuPhysicsLab.exe"),
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            if (build.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Lab build failed: " + build.summary.result);
            Debug.Log("LAB_BUILD_READY " + directory);
        }
    }
}
