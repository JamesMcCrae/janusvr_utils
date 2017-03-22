﻿#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace JanusVR
{
    public class JanusVRLink : MonoBehaviour
    {
        [NonSerialized]
        public Texture2D texture;

        [SerializeField, HideInInspector]
        public MeshRenderer meshRenderer;


        [SerializeField, HideInInspector]
        private MeshFilter filter;

        [SerializeField, HideInInspector]
        public string url = "http://www.janusvr.com";
        [SerializeField, HideInInspector]
        public string title = "JanusVR";
        [SerializeField, HideInInspector]
        public bool draw_glow = true;
        [SerializeField, HideInInspector]
        public bool draw_text = true;
        [SerializeField, HideInInspector]
        public bool auto_load = false;

        [SerializeField, HideInInspector]
        private Color color = Color.white;

        [SerializeField, HideInInspector]
        private bool circular = false;

        public Color Color
        {
            get { return color; }
            set
            {
                color = value;
                if (!meshRenderer)
                {
                    meshRenderer = GetComponent<MeshRenderer>();
                }
                meshRenderer.sharedMaterial.color = value;
            }
        }

        public bool Circular
        {
            get { return circular; }
            set
            {
                if (circular != value)
                {
                    filter.sharedMesh = JanusResources.PlaneMesh;

                    if (value)
                    {
                        filter.sharedMesh = JanusResources.CylinderBaseMesh;
                    }
                    else
                    {
                        filter.sharedMesh = JanusResources.PlaneMesh;
                    }
                }
                circular = value;
            }
        }

        public Vector3 GetJanusPosition()
        {
            // offset the position so it fits on the main app
            Vector3 position = transform.position;
            Vector3 scale = transform.localScale;
            Vector3 halfScale = scale / 2.0f;

            Vector3 bot = position - (transform.up * halfScale.y);
            return bot;
        }
        
        [MenuItem("GameObject/JanusVR/Link")]
        private static void CreateEntryPortal()
        {
            GameObject go = new GameObject("JanusVR Link");
            go.transform.localScale = new Vector3(1.8f, 2.5f, 1);

            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            MeshFilter filter = go.AddComponent<MeshFilter>();

            renderer.receiveShadows = false;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            filter.sharedMesh = JanusResources.PlaneMesh;

            Material mat = Material.Instantiate(AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat"));
            renderer.sharedMaterial = mat;

            JanusVRLink portal = go.AddComponent<JanusVRLink>();
            portal.meshRenderer = renderer;
            portal.filter = filter;
        }
    }
}
#endif