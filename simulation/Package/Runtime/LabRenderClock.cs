using UnityEngine;
using System.Collections.Generic;

namespace Horse.MikuPhysicsLab
{
    public sealed class LabRenderClock : MonoBehaviour
    {
        public int RenderedFrames { get; private set; }
        public int ForcedFrames { get; private set; }
        public int AutomaticPasses { get; private set; }
        public int ManualPasses { get; private set; }
        internal readonly List<string> Trace = new List<string>();
        private int lastFrame = -1;
        private Camera view;
        private RenderTexture target;
        private bool manual;

        private void Awake()
        {
            view = GetComponent<Camera>();
            if (LabController.Argument("-labSuite") != null)
            {
                bool firstPerson = LabController.Argument("-labFirstPerson") != null;
                target = new RenderTexture(firstPerson ? 1280 : 1200, firstPerson ? 720 : 1000, 24);
                view.targetTexture = target;
            }
        }

        internal void SetFirstPersonAspect(bool firstPerson)
        {
            if (target == null) return;
            int width = firstPerson ? 1280 : 1200;
            int height = firstPerson ? 720 : 1000;
            if (target.width == width && target.height == height) return;
            var previous = target;
            target = new RenderTexture(width, height, 24);
            view.targetTexture = target;
            previous.Release();
            Destroy(previous);
            lastFrame = -1;
        }

        private void OnPostRender()
        {
            if (manual)
                ManualPasses++;
            else
                AutomaticPasses++;
            if (Trace.Count < 18)
                Trace.Add((manual ? "manual " : "automatic ") + Time.frameCount);
            if (lastFrame != Time.frameCount)
            {
                lastFrame = Time.frameCount;
                RenderedFrames++;
            }
        }

        internal void EnsureRendered()
        {
            if (lastFrame != Time.frameCount)
            {
                RenderManual();
                ForcedFrames++;
            }
        }

        internal void RenderManual()
        {
            manual = true;
            try { view.Render(); }
            finally { manual = false; }
        }

        private void OnGUI()
        {
            if (target == null || !isActiveAndEnabled)
                return;
            int depth = GUI.depth;
            GUI.depth = 20;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), target, ScaleMode.ScaleToFit, false);
            GUI.depth = depth;
        }

        internal void Shutdown()
        {
            if (target != null)
            {
                if (view != null)
                {
                    view.enabled = false;
                    view.targetTexture = null;
                }
                target.Release();
                Destroy(target);
                target = null;
            }
        }

        private void OnDestroy() => Shutdown();
    }
}
