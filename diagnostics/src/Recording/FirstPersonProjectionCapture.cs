using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal static class FirstPersonProjectionCapture
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Dictionary<Camera, RenderProjectionData> Frames = new Dictionary<Camera, RenderProjectionData>();
    private static Harmony harmony;
    private static FieldInfo cameraDataField;
    private static FieldInfo cameraField;
    private static Camera activeCamera;
    private static bool sampled;
    internal static bool Enabled;
    internal static string Error { get; private set; }

    internal static void Install()
    {
        try
        {
            Assembly pipeline = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "Unity.RenderPipelines.Universal.Runtime");
            Type pass = pipeline.GetType("UnityEngine.Rendering.Universal.Internal.DrawObjectsPass", true);
            MethodInfo callback = pass.GetNestedTypes(Members).SelectMany(type => type.GetMethods(Members)).Single(method =>
                method.GetParameters().Length == 2 && method.GetParameters()[0].ParameterType.Name == "FirstPersonPassData");
            cameraDataField = callback.GetParameters()[0].ParameterType.GetField("cameraData", Members);
            cameraField = cameraDataField.FieldType.GetField("camera", Members);
            MethodInfo matrices = pipeline.GetType("UnityEngine.Rendering.Universal.RenderingUtils", true).GetMethods(Members).Single(method =>
                method.Name == "SetViewAndProjectionMatrices" && method.GetParameters().Length == 4 &&
                method.GetParameters()[0].ParameterType.Name == "RasterCommandBuffer");
            harmony = new Harmony(DiagnosticsPlugin.Guid + ".projection");
            harmony.Patch(callback, prefix: new HarmonyMethod(typeof(FirstPersonProjectionCapture), nameof(BeginPass)),
                finalizer: new HarmonyMethod(typeof(FirstPersonProjectionCapture), nameof(EndPass)));
            harmony.Patch(matrices, postfix: new HarmonyMethod(typeof(FirstPersonProjectionCapture), nameof(ReadMatrices)));
        }
        catch (Exception error)
        {
            Error = error.GetType().Name + ": " + error.Message;
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }

    private static void BeginPass(object __0)
    {
        activeCamera = null;
        sampled = false;
        if (!Enabled) return;
        try
        {
            activeCamera = cameraField.GetValue(cameraDataField.GetValue(__0)) as Camera;
        }
        catch (Exception error)
        {
            Error = error.GetType().Name + ": " + error.Message;
        }
    }

    private static void ReadMatrices(Matrix4x4 __1, Matrix4x4 __2)
    {
        if (!Enabled || activeCamera == null || sampled) return;
        sampled = true;
        try
        {
            float verticalScale = Mathf.Abs(__2.m11);
            float horizontalScale = Mathf.Abs(__2.m00);
            if (verticalScale < .00001f || horizontalScale < .00001f) return;
            float fov = 2f * Mathf.Atan(1f / verticalScale) * Mathf.Rad2Deg;
            float aspect = verticalScale / horizontalScale;
            Matrix4x4 projection = Matrix4x4.Perspective(fov, aspect, activeCamera.nearClipPlane, activeCamera.farClipPlane);
            Frames[activeCamera] = new RenderProjectionData
            {
                unityFrame = Time.frameCount,
                source = "DrawObjectsPass.FirstPersonPassData / RenderingUtils.SetViewAndProjectionMatrices",
                renderingLayerMask = 2,
                fieldOfView = fov,
                aspect = aspect,
                near = activeCamera.nearClipPlane,
                far = activeCamera.farClipPlane,
                worldToCamera = CharacterCapture.Matrix(__1),
                projection = CharacterCapture.Matrix(projection),
                gpuProjection = CharacterCapture.Matrix(__2)
            };
        }
        catch (Exception error)
        {
            Error = error.GetType().Name + ": " + error.Message;
        }
    }

    private static void EndPass() => activeCamera = null;

    internal static RenderProjectionData Read(Camera camera) => camera != null && Frames.TryGetValue(camera, out RenderProjectionData data) &&
        data.unityFrame == Time.frameCount ? data : null;

    internal static void Reset()
    {
        Frames.Clear();
        activeCamera = null;
    }

    internal static void Dispose()
    {
        Enabled = false;
        Reset();
        harmony?.UnpatchSelf();
        harmony = null;
    }
}
