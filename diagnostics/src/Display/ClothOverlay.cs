using System;
using System.Collections.Generic;
using UnityEngine;

namespace MikuGlider.Diagnostics;

internal sealed class ClothOverlay
{
    private GUIStyle text;
    private readonly HashSet<int> penetrations = new HashSet<int>();
    private readonly HashSet<int> jitter = new HashSet<int>();

    internal void Draw(ClothSampler sampler, bool enabled, bool recording, string message)
    {
        if ((!enabled && !recording) || Event.current.type != EventType.Repaint)
            return;
        text ??= new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
        ClothFrame frame = sampler?.Last;
        GUI.Box(new Rect(12, 12, 650, 165), string.Empty);
        GUI.Label(new Rect(22, 20, 630, 155),
            (recording ? "● RECORDING" : "CLOTH DIAGNOSTICS") + "   F6 record · F7 overlay · F8 snapshot · F9 target\n" +
            (frame == null ? recording ? "No visible skirt. Character / first-person arms capture remains active." : "No visible Miku skirt. Use J or open a character preview." :
                frame.skin + " · " + frame.context + " · " + frame.motion + " · " + frame.state + " · probe #" + frame.sampledFrame + "\n" +
                (frame.state == "pose_driven" ? "Garment pose " + frame.garmentPoseMilliseconds.ToString("F3") + " ms" :
                "Particles " + frame.particleCount + " · pinned " + frame.pinnedCount) +
                " · space " + frame.particleSpace + " · fit " + (frame.coordinateError * 1000f).ToString("F1") + " mm\n" +
                "Penetration " + (frame.maximumPenetration * 1000f).ToString("F1") + " mm · speed " +
                frame.maximumRelativeSpeed.ToString("F2") + " m/s · " + frame.issues) + "\n" + message, text);
        if (!enabled || frame == null || !frame.coordinatesReliable)
            return;
        Camera camera = sampler.Target.FindCamera();
        if (camera == null || camera.targetTexture != null)
            return;
        penetrations.Clear();
        jitter.Clear();
        if (frame.penetratingIndices != null)
            foreach (int index in frame.penetratingIndices)
                penetrations.Add(index);
        if (frame.jitterIndices != null)
            foreach (int index in frame.jitterIndices)
                jitter.Add(index);
        int stride = Math.Max(1, frame.particleCount / 400);
        for (int index = 0; index < (frame.particlesWorld?.Length ?? 0); index++)
        {
            bool warning = penetrations.Contains(index) || jitter.Contains(index);
            if (!warning && index % stride != 0)
                continue;
            Color color = penetrations.Contains(index) ? new Color(1f, 0.2f, 0.2f) :
                jitter.Contains(index) ? Color.yellow : sampler.Distances[index] == 0f ? Color.cyan : Color.green;
            Point(camera, frame.particlesWorld[index], color, warning ? 5f : 3f);
        }
        if (frame.colliders != null)
            foreach (ColliderData collider in frame.colliders)
                if (collider.valid)
                    Capsule(camera, collider);
        if (frame.geometry?.crossingPoints != null)
            foreach (Vector3 point in frame.geometry.crossingPoints)
                Point(camera, point, Color.magenta, 7f);
    }

    private static void Capsule(Camera camera, ColliderData collider)
    {
        Color color = collider.enabled ? new Color(1f, 0.6f, 0.15f) : Color.gray;
        Vector3 axis = collider.end - collider.start;
        Vector3 direction = axis.sqrMagnitude > 0.00001f ? axis.normalized : Vector3.up;
        Vector3 right = Vector3.Cross(direction, Math.Abs(direction.y) < 0.8f ? Vector3.up : Vector3.right).normalized;
        Vector3 forward = Vector3.Cross(direction, right).normalized;
        for (int ring = 0; ring < 2; ring++)
        {
            Vector3 center = ring == 0 ? collider.start : collider.end;
            float radius = ring == 0 ? collider.startRadius : collider.endRadius;
            for (int index = 0; index < 16; index++)
            {
                float a = index * Mathf.PI / 8f;
                float b = (index + 1) * Mathf.PI / 8f;
                Line(camera, center + (right * Mathf.Cos(a) + forward * Mathf.Sin(a)) * radius,
                    center + (right * Mathf.Cos(b) + forward * Mathf.Sin(b)) * radius, color);
            }
        }
        for (int index = 0; index < 4; index++)
        {
            Vector3 offset = index == 0 ? right : index == 1 ? -right : index == 2 ? forward : -forward;
            Line(camera, collider.start + offset * collider.startRadius, collider.end + offset * collider.endRadius, color);
            for (int step = 0; step < 8; step++)
            {
                float a = step * Mathf.PI / 16f;
                float b = (step + 1) * Mathf.PI / 16f;
                Line(camera, collider.start + (offset * Mathf.Cos(a) - direction * Mathf.Sin(a)) * collider.startRadius,
                    collider.start + (offset * Mathf.Cos(b) - direction * Mathf.Sin(b)) * collider.startRadius, color);
                Line(camera, collider.end + (offset * Mathf.Cos(a) + direction * Mathf.Sin(a)) * collider.endRadius,
                    collider.end + (offset * Mathf.Cos(b) + direction * Mathf.Sin(b)) * collider.endRadius, color);
            }
        }
    }

    private static void Point(Camera camera, Vector3 point, Color color, float size)
    {
        Vector3 screen = camera.WorldToScreenPoint(point);
        if (screen.z <= 0f)
            return;
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(screen.x - size * 0.5f, Screen.height - screen.y - size * 0.5f, size, size), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static void Line(Camera camera, Vector3 start, Vector3 end, Color color)
    {
        Vector3 a = camera.WorldToScreenPoint(start);
        Vector3 b = camera.WorldToScreenPoint(end);
        if (a.z <= 0f || b.z <= 0f)
            return;
        Vector2 from = new Vector2(a.x, Screen.height - a.y);
        Vector2 to = new Vector2(b.x, Screen.height - b.y);
        Vector2 delta = to - from;
        Matrix4x4 matrix = GUI.matrix;
        Color previous = GUI.color;
        try
        {
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            GUI.DrawTexture(new Rect(from.x, from.y, delta.magnitude, 1f), Texture2D.whiteTexture);
        }
        finally
        {
            GUI.matrix = matrix;
            GUI.color = previous;
        }
    }
}
