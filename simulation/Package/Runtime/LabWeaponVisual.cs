using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Horse.MikuPhysicsLab
{
    [Serializable] internal sealed class LabWeaponCatalog
    {
        public LabWeaponDefinition[] weapons;
    }

    [Serializable] internal sealed class LabWeaponDefinition
    {
        public string key;
        public string label;
        public LabWeaponNode[] nodes;
        public string[] clipGuids;
        public string[] animationSets;
    }

    [Serializable] internal sealed class LabWeaponNode
    {
        public string name;
        public int parent;
        public string socket;
        public float[] position;
        public float[] rotation;
        public float[] scale;
        public string mesh;
        public string[] textures;
    }

    internal sealed class LabWeaponVisual : IDisposable
    {
        private static LabWeaponCatalog catalog;
        private readonly LabRig rig;
        private readonly List<GameObject> roots = new List<GameObject>();
        private readonly List<Material> materials = new List<Material>();
        private string selectedClip;
        private string activeKey;
        internal string WeaponKey => activeKey;
        internal int MeshCount { get; private set; }
        internal int VertexCount { get; private set; }

        internal LabWeaponVisual(LabRig rig)
        {
            this.rig = rig;
            if (catalog == null)
            {
                TextAsset source = Resources.Load<TextAsset>("MikuPhysicsLab/Weapons/catalog");
                catalog = source != null ? JsonUtility.FromJson<LabWeaponCatalog>(source.text) : new LabWeaponCatalog();
            }
        }

        internal void Select(ClipEntry entry)
        {
            if (selectedClip == entry.key)
                return;
            selectedClip = entry.key;
            string requested = LabController.Argument("-labWeapon") ?? entry.weaponKey;
            LabWeaponDefinition[] candidates = catalog.weapons?.Where(item => item.clipGuids.Contains(entry.sourceGuid)).ToArray() ?? Array.Empty<LabWeaponDefinition>();
            LabWeaponDefinition definition = !string.IsNullOrEmpty(requested) ?
                catalog.weapons?.FirstOrDefault(item => item.key == requested) :
                candidates.FirstOrDefault(item => item.animationSets.Any(set => entry.source.StartsWith(set + ":", StringComparison.Ordinal))) ??
                (candidates.Length == 1 ? candidates[0] : null);
            if (definition == null)
            {
                Clear();
                Debug.Log("LAB_WEAPON_UNSUPPORTED " + entry.key + " " + entry.label + " candidates=" + candidates.Length);
                return;
            }
            if (activeKey == definition.key)
                return;
            Clear();
            activeKey = definition.key;
            Transform[] nodes = definition.nodes.Select(item => new GameObject("Native weapon " + item.name).transform).ToArray();
            for (int i = 0; i < nodes.Length; i++)
            {
                LabWeaponNode item = definition.nodes[i];
                Transform parent = string.IsNullOrEmpty(item.socket) ? nodes[item.parent] :
                    rig.Nodes.FirstOrDefault(node => node.name == item.socket);
                if (parent == null)
                    throw new InvalidOperationException("Native weapon socket is missing: " + item.socket);
                Transform node = nodes[i];
                node.SetParent(parent, false);
                node.localPosition = LabRig.V(item.position);
                node.localRotation = new Quaternion(item.rotation[0], item.rotation[1], item.rotation[2], item.rotation[3]);
                node.localScale = LabRig.V(item.scale);
                if (!string.IsNullOrEmpty(item.socket))
                    roots.Add(node.gameObject);
                if (string.IsNullOrEmpty(item.mesh))
                    continue;
                Mesh mesh = Resources.Load<Mesh>("MikuPhysicsLab/" + item.mesh);
                if (mesh == null)
                    throw new InvalidOperationException("Native weapon mesh is missing: " + item.mesh);
                node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = node.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = item.textures.Select(TextureMaterial).ToArray();
                MeshCount++;
                VertexCount += mesh.vertexCount;
            }
            Debug.Log("LAB_WEAPON_BOUND " + activeKey + " meshes=" + MeshCount + " vertices=" + VertexCount + " clip=" + entry.key);
        }

        private Material TextureMaterial(string resource)
        {
            var material = new Material(Resources.Load<Shader>("MikuLabAlbedo"));
            material.name = "Native weapon albedo";
            material.SetFloat("_Cutoff", 0f);
            material.mainTexture = string.IsNullOrEmpty(resource) ? Texture2D.whiteTexture :
                Resources.Load<Texture2D>("MikuPhysicsLab/" + resource);
            if (material.mainTexture == null)
                throw new InvalidOperationException("Native weapon texture is missing: " + resource);
            materials.Add(material);
            return material;
        }

        private void Clear()
        {
            foreach (GameObject root in roots)
            {
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
            }
            foreach (Material material in materials)
                UnityEngine.Object.Destroy(material);
            roots.Clear();
            materials.Clear();
            activeKey = null;
            MeshCount = 0;
            VertexCount = 0;
        }

        public void Dispose() => Clear();
    }
}
