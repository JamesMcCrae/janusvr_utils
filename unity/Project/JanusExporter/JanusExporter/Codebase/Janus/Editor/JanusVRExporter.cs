﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.FBX;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace JanusVR
{
    /// <summary>
    /// Main class for the Janus VR Exporter
    /// </summary>
    public class JanusVRExporter : EditorWindow
    {
        public static bool UpdateOnlyHTML { get; private set; }
        public const int Version = 203;

        /// <summary>
        /// The folder were exporting the scene to
        /// </summary>
        [SerializeField]
        private string exportPath = @"";

        /// <summary>
        /// The format to export all textures to
        /// </summary>
        [SerializeField]
        private ImageFormatEnum defaultTexFormat = ImageFormatEnum.JPG;

        /// <summary>
        /// The quality to export all textures
        /// </summary>
        [SerializeField]
        private int defaultQuality = 70;

        /// <summary>
        /// The filtering mode to use when downsampling textures
        /// </summary>
        [SerializeField]
        private TextureFilterMode filterMode = TextureFilterMode.Nearest;

        /// <summary>
        /// The format to export all meshes in
        /// </summary>
        [SerializeField]
        private ExportMeshFormat meshFormat = ExportMeshFormat.FBX;

        /// <summary>
        /// An uniform scale to apply to the whole scene (useful for matching VR scale inside janus)
        /// </summary>
        [SerializeField]
        private float uniformScale = 1;

        /// <summary>
        /// The type of lightmaps you want to export
        /// </summary>
        [SerializeField]
        private LightmapExportType lightmapExportType = LightmapExportType.Packed;

        /// <summary>
        /// The maximum resolution a lightmap atlas can have
        /// </summary>
        [SerializeField]
        private int maxLightMapResolution = 2048;

        /// <summary>
        /// If the exporter should output the materials (if disabled, lightmaps are still exported, so you can take a look at only lightmap
        /// data with a gray tone)
        /// </summary>
        [SerializeField]
        private bool exportMaterials = true;

        /// <summary>
        /// Exports the scene's skybox
        /// </summary>
        [SerializeField]
        private bool exportSkybox = true;

        /// <summary>
        /// The resolution to render the skybox to, if it's a procedural one
        /// </summary>
        [SerializeField]
        private int exportSkyboxResolution = 1024;

        /// <summary>
        /// Compress the scene models using GZip (WIP and extremely slow)
        /// </summary>
        [SerializeField]
        private bool compressFiles = false;

        /// <summary>
        /// Lower case values that the exporter will consider for being the Main Texture on a shader
        /// </summary>
        private string[] mainTexSemantics = new string[]
        {
            "_maintex"
        };

        /// <summary>
        /// Lower case values that the exporter will consider for being the Tiling
        /// </summary>
        private string[] tilingSemantics = new string[]
        {
            "_maintex_st"
        };

        /// <summary>
        /// Lower case values that the exporter will consider for being the Color off a shader
        /// </summary>
        private string[] colorSemantics = new string[]
        {
            "_color"
        };

        /// <summary>
        /// Lower case values that the exporter will consider for the shader using transparent textures
        /// </summary>
        private string[] transparentSemantics = new string[]
        {
            "transparent"
        };

        /// <summary>
        /// The semantic names for all the skybox 6-sided faces
        /// </summary>
        private string[] skyboxTexNames = new string[]
        {
            "_FrontTex", "_BackTex", "_LeftTex", "_RightTex", "_UpTex", "_DownTex"
        };

        private Dictionary<int, List<GameObject>> lightmapped;

        private List<Texture2D> texturesExported;
        private List<TextureExportData> texturesExportedData;

        private List<Mesh> meshesExported;
        private List<MeshExportData> meshesExportedData;
        private Dictionary<Mesh, string> meshesNames;
        private int meshesCount;

        private ExportedData exported;

        private bool updateOnlyHtml = false;

        /// <summary>
        /// The farplane distance of the camera
        /// </summary>
        private float farPlaneDistance = 1000;

        private Bounds sceneSize;

        public const int PreviewSize = 64;

        internal class ExportedData
        {
            internal List<ExportedObject> exportedObjs;
            internal List<ExportedObject> exportedProbes;

            internal ExportedData()
            {
                exportedObjs = new List<ExportedObject>();
                exportedProbes = new List<ExportedObject>();
            }
        }

        public JanusVRExporter()
        {
            this.titleContent = new GUIContent("Janus Exporter");
        }

        [MenuItem("Edit/JanusVR Exporter v2.0")]
        private static void Init()
        {
            // Get existing open window or if none, make a new one:
            JanusVRExporter window = EditorWindow.GetWindow<JanusVRExporter>();
            window.Show();

            if (string.IsNullOrEmpty(window.exportPath))
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string workspace = Path.Combine(documents, @"JanusVR\workspaces");
                string proj = Path.Combine(workspace, Application.productName);
                window.exportPath = proj;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Export Path");
            exportPath = EditorGUILayout.TextField(exportPath);
            if (GUILayout.Button("..."))
            {
                // search for a folder
                exportPath = EditorUtility.OpenFolderPanel("JanusVR Export Folder", exportPath, @"C:\");
            }
            EditorGUILayout.EndHorizontal();

            meshFormat = (ExportMeshFormat)EditorGUILayout.EnumPopup("Mesh Format", meshFormat);
            defaultTexFormat = (ImageFormatEnum)EditorGUILayout.EnumPopup("Textures Format", defaultTexFormat);
            if (SupportsQuality(defaultTexFormat))
            {
                defaultQuality = EditorGUILayout.IntSlider("Textures Quality", defaultQuality, 0, 100);
            }
            //compressFiles = EditorGUILayout.Toggle("Compress Models", compressFiles);

            uniformScale = EditorGUILayout.FloatField("Uniform Scale", uniformScale);
            exportMaterials = EditorGUILayout.Toggle("Export Materials", exportMaterials);
            exportSkybox = EditorGUILayout.Toggle("Export Skybox", exportSkybox);
            if (exportSkybox && IsProceduralSkybox())
            {
                exportSkyboxResolution = Math.Max(4, EditorGUILayout.IntField("Skybox Render Resolution", exportSkyboxResolution));
            }

            lightmapExportType = (LightmapExportType)EditorGUILayout.EnumPopup("Lightmap Type", lightmapExportType);
            if (lightmapExportType != LightmapExportType.None)
            {
                maxLightMapResolution = Math.Max(4, EditorGUILayout.IntField("Max Lightmap Resolution", maxLightMapResolution));
            }

            if (GUILayout.Button("Export HTML only"))
            {
                updateOnlyHtml = true;
                PreExport();
                DoExport();
                updateOnlyHtml = false;
            }

            if (!string.IsNullOrEmpty(exportPath))
            {
                if (GUILayout.Button("Full Export"))
                {
                    PreExport();
                    DoExport();
                }
            }

            if (exported != null)
            {
                GUILayout.Label("Scene size " + sceneSize.size);
                GUILayout.Label("Far plane " + farPlaneDistance);
            }
        }

        private static string GetMeshFormat(ExportMeshFormat format)
        {
            switch (format)
            {
                case ExportMeshFormat.FBX:
                    return ".fbx";
                case ExportMeshFormat.OBJ_NotWorking:
                    return ".obj";
                default:
                    return "";
            }
        }
        private static string GetImageFormatName(ImageFormatEnum format)
        {
            switch (format)
            {
                case ImageFormatEnum.JPG:
                    return ".jpg";
                case ImageFormatEnum.PNG:
                default:
                    return ".png";
            }
        }
        private static bool SupportsQuality(ImageFormatEnum format)
        {
            switch (format)
            {
                case ImageFormatEnum.JPG:
                    return true;
                case ImageFormatEnum.PNG:// PNG is lossless
                default:
                    return false;
            }
        }

        private bool IsPowerOf2(int value)
        {
            return (value & (value - 1)) == 0;
        }

        private int NextPowerOf2(int value)
        {
            return (int)Math.Pow(2, Math.Ceiling(Math.Log(value) / Math.Log(2)));
        }

        private void AssertShader(Shader shader)
        {
            if (shader == null)
            {
                Debug.LogError("Shaders not found! Please reimport the Janus Exporter package");
            }
        }

        private Texture2D RenderSkyBoxSide(Vector3 direction, string name, RenderTexture tmpTex, Camera cam)
        {
            cam.gameObject.transform.LookAt(direction);
            cam.Render();

            Graphics.SetRenderTarget(tmpTex);
            Texture2D left = new Texture2D(tmpTex.width, tmpTex.height, TextureFormat.ARGB32, false, false);
            left.ReadPixels(new Rect(0, 0, tmpTex.width, tmpTex.height), 0, 0);
            left.name = "SkyBox" + name;

            texturesExported.Add(left);

            return left;
        }

        private bool IsProceduralSkybox()
        {
            Material skybox = RenderSettings.skybox;
            if (skybox != null)
            {
                for (int i = 0; i < skyboxTexNames.Length; i++)
                {
                    if (!skybox.HasProperty(skyboxTexNames[i]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private Texture2D skyBoxLeft;
        private Texture2D skyBoxRight;
        private Texture2D skyBoxUp;
        private Texture2D skyBoxDown;
        private Texture2D skyBoxForward;
        private Texture2D skyBoxBack;

        private void PreExport()
        {
            Clean();

            texturesExported = new List<Texture2D>();
            texturesExportedData = new List<TextureExportData>();

            meshesExported = new List<Mesh>();
            meshesExportedData = new List<MeshExportData>();
            meshesNames = new Dictionary<Mesh, string>();
            meshesCount = 0;

            Scene scene = SceneManager.GetActiveScene();
            GameObject[] roots = scene.GetRootGameObjects();

            lightmapped = new Dictionary<int, List<GameObject>>();
            exported = new ExportedData();

            for (int i = 0; i < roots.Length; i++)
            {
                RecursiveSearch(roots[i], exported);
            }

            if (exportSkybox)
            {
                // look for skybox and grab all 6 textures if it's 6-sided
                Material skybox = RenderSettings.skybox;
                if (skybox != null)
                {
                    bool proceed = true;
                    for (int i = 0; i < skyboxTexNames.Length; i++)
                    {
                        if (!skybox.HasProperty(skyboxTexNames[i]))
                        {
                            proceed = false;
                        }
                    }

                    if (proceed)
                    {
                        for (int i = 0; i < skyboxTexNames.Length; i++)
                        {
                            Texture2D tex = (Texture2D)skybox.GetTexture(skyboxTexNames[i]);
                            if (!texturesExported.Contains(tex))
                            {
                                texturesExported.Add(tex);
                            }
                        }

                        skyBoxForward = (Texture2D)skybox.GetTexture("_FrontTex");
                        skyBoxBack = (Texture2D)skybox.GetTexture("_BackTex");
                        skyBoxLeft = (Texture2D)skybox.GetTexture("_LeftTex");
                        skyBoxRight = (Texture2D)skybox.GetTexture("_RightTex");
                        skyBoxUp = (Texture2D)skybox.GetTexture("_UpTex");
                        skyBoxDown = (Texture2D)skybox.GetTexture("_DownTex");
                    }
                    else
                    {
                        if (!updateOnlyHtml)
                        {
                            // the skybox is not a 6-texture skybox
                            // lets render it to one then
                            GameObject temp = new GameObject("__TempSkyRender");
                            Camera cam = temp.AddComponent<Camera>();

                            cam.enabled = false;

                            RenderTexture tex = new RenderTexture(exportSkyboxResolution, exportSkyboxResolution, 0);
                            cam.targetTexture = tex;
                            cam.clearFlags = CameraClearFlags.Skybox;
                            cam.cullingMask = 0;
                            cam.orthographic = true;

                            skyBoxLeft = RenderSkyBoxSide(Vector3.left, "Left", tex, cam);
                            skyBoxRight = RenderSkyBoxSide(Vector3.right, "Right", tex, cam);
                            skyBoxForward = RenderSkyBoxSide(Vector3.forward, "Forward", tex, cam);
                            skyBoxBack = RenderSkyBoxSide(Vector3.back, "Back", tex, cam);
                            skyBoxUp = RenderSkyBoxSide(Vector3.up, "Up", tex, cam);
                            skyBoxDown = RenderSkyBoxSide(Vector3.down, "Down", tex, cam);

                            cam.targetTexture = null;
                            Graphics.SetRenderTarget(null);

                            GameObject.DestroyImmediate(tex);
                            GameObject.DestroyImmediate(temp);
                        }
                    }
                }
            }

            if (lightmapExportType != LightmapExportType.None &&
                lightmapped.Count > 0)
            {
                string scenePath = scene.path;
                scenePath = Path.GetDirectoryName(scenePath);
                string lightMapsFolder = Path.Combine(scenePath, scene.name);

                switch (lightmapExportType)
                {
                    case LightmapExportType.BakedMaterial:
                        #region Baked
                        {
                            // only load shader now, so if the user is not exporting lightmaps
                            // he doesn't need to have it on his project folder
                            Shader lightMapShader = Shader.Find("Hidden/LMapBaked");
                            AssertShader(lightMapShader);

                            Material lightMap = new Material(lightMapShader);
                            lightMap.SetPass(0);

                            // export lightmaps
                            int lmap = 0;
                            foreach (var lightPair in lightmapped)
                            {
                                int id = lightPair.Key;
                                List<GameObject> toRender = lightPair.Value;

                                // get the path to the lightmap file
                                string lightMapFile = Path.Combine(lightMapsFolder, "Lightmap-" + id + "_comp_light.exr");
                                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(lightMapFile);
                                if (texture == null)
                                {
                                    continue;
                                }

                                lightMap.SetTexture("_LightMapTex", texture);

                                for (int i = 0; i < toRender.Count; i++)
                                {
                                    GameObject obj = toRender[i];
                                    MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
                                    MeshFilter filter = obj.GetComponent<MeshFilter>();

                                    Mesh mesh = filter.sharedMesh;
                                    Transform trans = obj.transform;
                                    Matrix4x4 world = Matrix4x4.TRS(trans.position, trans.rotation, trans.lossyScale);

                                    Vector4 scaleOffset = renderer.lightmapScaleOffset;
                                    float width = (1 - scaleOffset.z) * scaleOffset.x;
                                    float height = (1 - scaleOffset.w) * scaleOffset.y;
                                    float size = Math.Max(width, height);

                                    int lightMapSize = (int)(maxLightMapResolution * size);
                                    lightMapSize = (int)Math.Pow(2, Math.Ceiling(Math.Log(lightMapSize) / Math.Log(2)));
                                    lightMapSize = Math.Min(maxLightMapResolution, Math.Max(lightMapSize, 16));

                                    RenderTexture renderTexture = RenderTexture.GetTemporary(lightMapSize, lightMapSize, 0, RenderTextureFormat.ARGB32);
                                    Graphics.SetRenderTarget(renderTexture);
                                    GL.Clear(true, true, new Color(0, 0, 0, 0)); // clear to transparent

                                    Material[] mats = renderer.sharedMaterials;
                                    for (int j = 0; j < mats.Length; j++)
                                    {
                                        Material mat = mats[j];

                                        lightMap.SetTexture("_MainTex", null);

                                        Shader shader = mat.shader;
                                        int props = ShaderUtil.GetPropertyCount(shader);
                                        for (int k = 0; k < props; k++)
                                        {
                                            string name = ShaderUtil.GetPropertyName(shader, k);

                                            ShaderUtil.ShaderPropertyType propType = ShaderUtil.GetPropertyType(shader, k);
                                            if (propType == ShaderUtil.ShaderPropertyType.TexEnv)
                                            {
                                                if (mainTexSemantics.Contains(name.ToLower()))
                                                {
                                                    // main texture texture
                                                    lightMap.SetTexture("_MainTex", mat.GetTexture(name));
                                                }
                                            }
                                        }

                                        lightMap.SetVector("_LightMapUV", renderer.lightmapScaleOffset);
                                        lightMap.SetPass(0);
                                        Graphics.DrawMeshNow(mesh, world, j);
                                    }

                                    // This is the only way to access data from a RenderTexture
                                    Texture2D tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false, false);
                                    tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                                    tex.name = "Lightmap" + lmap;
                                    tex.Apply(); // send the data back to the GPU so we can draw it on the preview area

                                    if (!texturesExported.Contains(tex))
                                    {
                                        texturesExported.Add(tex);
                                    }

                                    ExportedObject eobj = exported.exportedObjs.First(c => c.GameObject == obj);
                                    eobj.DiffuseMapTex = tex;

                                    Graphics.SetRenderTarget(null);
                                    RenderTexture.ReleaseTemporary(renderTexture);

                                    lmap++;
                                }
                            }
                            UnityEngine.Object.DestroyImmediate(lightMap);
                        }
                        #endregion
                        break;
                    case LightmapExportType.Packed:
                        #region Packed
                        {
                            Shader lightMapShader = Shader.Find("Hidden/LMapPacked");
                            AssertShader(lightMapShader);

                            Material lightMap = new Material(lightMapShader);
                            lightMap.SetPass(0);

                            // just pass the textures forward
                            // export lightmaps
                            foreach (var lightPair in lightmapped)
                            {
                                int id = lightPair.Key;
                                List<GameObject> toRender = lightPair.Value;

                                // get the path to the lightmap file
                                string lightMapFile = Path.Combine(lightMapsFolder, "Lightmap-" + id + "_comp_light.exr");
                                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(lightMapFile);
                                if (texture == null)
                                {
                                    continue;
                                }

                                lightMap.SetTexture("_LightMapTex", texture);

                                // We need to access unity_Lightmap_HDR to decode the lightmap,
                                // but we can't, so we have to render everything to a custom RenderTexture!
                                Texture2D decTex = new Texture2D(texture.width, texture.height);
                                decTex.name = "Lightmap" + id;
                                texturesExported.Add(decTex);

                                RenderTexture renderTexture = RenderTexture.GetTemporary(texture.width, texture.height);
                                Graphics.SetRenderTarget(renderTexture);
                                GL.Clear(true, true, new Color(0, 0, 0, 0)); // clear to transparent

                                for (int i = 0; i < toRender.Count; i++)
                                {
                                    GameObject obj = toRender[i];
                                    MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
                                    MeshFilter filter = obj.GetComponent<MeshFilter>();

                                    Mesh mesh = filter.sharedMesh;
                                    Transform trans = obj.transform;
                                    Matrix4x4 world = Matrix4x4.TRS(trans.position, trans.rotation, trans.lossyScale);

                                    lightMap.SetVector("_LightMapUV", renderer.lightmapScaleOffset);
                                    lightMap.SetPass(0);
                                    Graphics.DrawMeshNow(mesh, Matrix4x4.identity);

                                    ExportedObject eobj = exported.exportedObjs.First(c => c.GameObject == obj);
                                    eobj.LightMapTex = decTex;
                                }

                                decTex.ReadPixels(new Rect(0, 0, decTex.width, decTex.height), 0, 0);
                                decTex.Apply(); // send the data back to the GPU so we can draw it on the preview area

                                Graphics.SetRenderTarget(null);
                                RenderTexture.ReleaseTemporary(renderTexture);
                            }
                            UObject.DestroyImmediate(lightMap);
                        }
                        #endregion
                        break;
                    case LightmapExportType.PackedSourceEXR:
                        #region Packed Source EXR
                        {
                            foreach (var lightPair in lightmapped)
                            {
                                int id = lightPair.Key;
                                List<GameObject> toRender = lightPair.Value;

                                // get the path to the lightmap file
                                string lightMapFile = Path.Combine(lightMapsFolder, "Lightmap-" + id + "_comp_light.exr");
                                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(lightMapFile);
                                if (texture == null)
                                {
                                    continue;
                                }

                                for (int i = 0; i < toRender.Count; i++)
                                {
                                    GameObject obj = toRender[i];
                                    ExportedObject eobj = exported.exportedObjs.First(c => c.GameObject == obj);
                                    eobj.LightMapTex = texture;
                                }

                                texturesExported.Add(texture);
                            }
                        }
                        #endregion
                        break;
                    case LightmapExportType.Unpacked:
                        #region Unpacked
                        {
                            Shader lightMapShader = Shader.Find("Hidden/LMapUnpacked");
                            AssertShader(lightMapShader);

                            Material lightMap = new Material(lightMapShader);
                            lightMap.SetPass(0);

                            // export lightmaps
                            int lmap = 0;
                            foreach (var lightPair in lightmapped)
                            {
                                int id = lightPair.Key;
                                List<GameObject> toRender = lightPair.Value;

                                // get the path to the lightmap file
                                string lightMapFile = Path.Combine(lightMapsFolder, "Lightmap-" + id + "_comp_light.exr");
                                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(lightMapFile);
                                if (texture == null)
                                {
                                    continue;
                                }

                                lightMap.SetTexture("_LightMapTex", texture);

                                for (int i = 0; i < toRender.Count; i++)
                                {
                                    GameObject obj = toRender[i];
                                    MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
                                    MeshFilter filter = obj.GetComponent<MeshFilter>();

                                    Mesh mesh = filter.sharedMesh;
                                    Transform trans = obj.transform;
                                    Matrix4x4 world = Matrix4x4.TRS(trans.position, trans.rotation, trans.lossyScale);

                                    Vector4 scaleOffset = renderer.lightmapScaleOffset;
                                    float width = (1 - scaleOffset.z) * scaleOffset.x;
                                    float height = (1 - scaleOffset.w) * scaleOffset.y;
                                    float size = Math.Max(width, height);

                                    int lightMapSize = (int)(maxLightMapResolution * size);
                                    lightMapSize = (int)Math.Pow(2, Math.Ceiling(Math.Log(lightMapSize) / Math.Log(2)));
                                    lightMapSize = Math.Min(maxLightMapResolution, Math.Max(lightMapSize, 16));

                                    RenderTexture renderTexture = RenderTexture.GetTemporary(lightMapSize, lightMapSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                                    Graphics.SetRenderTarget(renderTexture);
                                    GL.Clear(true, true, new Color(0, 0, 0, 0)); // clear to transparent

                                    Material[] mats = renderer.sharedMaterials;
                                    for (int j = 0; j < mats.Length; j++)
                                    {
                                        Material mat = mats[j];

                                        Shader shader = mat.shader;

                                        lightMap.SetVector("_LightMapUV", renderer.lightmapScaleOffset);
                                        lightMap.SetPass(0);
                                        Graphics.DrawMeshNow(mesh, world, j);
                                    }

                                    // This is the only way to access data from a RenderTexture
                                    Texture2D tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false, false);
                                    tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                                    tex.name = "Lightmap" + lmap;
                                    tex.Apply(); // send the data back to the GPU so we can draw it on the preview area

                                    if (!texturesExported.Contains(tex))
                                    {
                                        texturesExported.Add(tex);
                                    }

                                    ExportedObject eobj = exported.exportedObjs.First(c => c.GameObject == obj);
                                    eobj.LightMapTex = tex;

                                    Graphics.SetRenderTarget(null);
                                    RenderTexture.ReleaseTemporary(renderTexture);

                                    lmap++;
                                }
                            }
                            UObject.DestroyImmediate(lightMap);
                        }
                        #endregion
                        break;
                }
            }

            for (int i = 0; i < texturesExported.Count; i++)
            {
                Texture2D tex = texturesExported[i];
                TextureExportData data = new TextureExportData();
                string path = AssetDatabase.GetAssetPath(tex);

                // look for at least 1 object exported that uses us as a transparent texture
                for (int j = 0; j < exported.exportedObjs.Count; j++)
                {
                    ExportedObject obj = exported.exportedObjs[j];
                    if (obj.IsTransparent)
                    {
                        if (obj.DiffuseMapTex == tex ||
                            obj.LightMapTex == tex ||
                            obj.Texture == tex)
                        {
                            data.ExportAlpha = true;
                        }
                    }
                }

                data.Created = string.IsNullOrEmpty(path);
                data.Format = this.defaultTexFormat; // start up with a default format
                data.Texture = tex;
                data.Resolution = tex.width;
                data.Quality = defaultQuality;

                texturesExportedData.Add(data);
            }

            for (int i = 0; i < meshesExported.Count; i++)
            {
                Mesh mesh = meshesExported[i];
                MeshExportData data = new MeshExportData();
                data.Format = this.meshFormat;
                data.Mesh = mesh;

                string name = mesh.name;
                if (string.IsNullOrEmpty(name))
                {
                    meshesCount++;
                    name = "ExportedMesh" + meshesCount;
                }
                meshesNames.Add(mesh, name);

                meshesExportedData.Add(data);
            }

            farPlaneDistance = sceneSize.size.magnitude * 1.3f;
        }

        private void RecursiveSearch(GameObject root, ExportedData data)
        {
            if (!root.activeInHierarchy)
            {
                return;
            }

            Component[] comps = root.GetComponents<Component>();

            for (int i = 0; i < comps.Length; i++)
            {
                Component comp = comps[i];
                if (comp == null)
                {
                    continue;
                }

                if (comp is MeshRenderer)
                {
                    MeshRenderer meshRen = (MeshRenderer)comp;
                    MeshFilter filter = comps.FirstOrDefault(c => c is MeshFilter) as MeshFilter;
                    if (filter == null)
                    {
                        continue;
                    }

                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    sceneSize.Encapsulate(meshRen.bounds);

                    // Only export the mesh if we never exported this one mesh
                    if (!meshesExported.Contains(mesh))
                    {
                        meshesExported.Add(mesh);
                    }

                    ExportedObject exp = data.exportedObjs.FirstOrDefault(c => c.GameObject == root);
                    if (exp == null)
                    {
                        exp = new ExportedObject();
                        exp.GameObject = root;
                        data.exportedObjs.Add(exp);
                    }
                    exp.Mesh = mesh;

                    // export textures
                    if (lightmapExportType != LightmapExportType.BakedMaterial && exportMaterials) // if were baking we dont need the original textures
                    {
                        Material[] mats = meshRen.sharedMaterials;
                        for (int j = 0; j < mats.Length; j++)
                        {
                            Material mat = mats[j];
                            if (mat == null)
                            {
                                continue;
                            }

                            Vector2 sca = mat.mainTextureScale;
                            Vector2 off = mat.mainTextureOffset;
                            if (sca != Vector2.one || off != Vector2.zero)
                            {
                                exp.Tiling = new Vector4(sca.x, sca.y, off.x, off.y);
                            }

                            Shader shader = mat.shader;
                            if (!string.IsNullOrEmpty(shader.name))
                            {
                                string shaderLowercase = shader.name.ToLower();
                                for (int k = 0; k < transparentSemantics.Length; k++)
                                {
                                    if (shaderLowercase.Contains(transparentSemantics[k]))
                                    {
                                        exp.IsTransparent = true;
                                        break;
                                    }
                                }
                            }

                            int props = ShaderUtil.GetPropertyCount(shader);
                            for (int k = 0; k < props; k++)
                            {
                                string name = ShaderUtil.GetPropertyName(shader, k);

                                ShaderUtil.ShaderPropertyType propType = ShaderUtil.GetPropertyType(shader, k);
                                if (propType == ShaderUtil.ShaderPropertyType.TexEnv)
                                {
                                    if (mainTexSemantics.Contains(name.ToLower()))
                                    {
                                        Texture2D tex = mat.GetTexture(name) as Texture2D;
                                        if (tex == null)
                                        {
                                            continue;
                                        }

                                        exp.DiffuseMapTex = tex;
                                        if (!texturesExported.Contains(tex))
                                        {
                                            texturesExported.Add(tex);
                                        }
                                    }
                                }
                                else if (propType == ShaderUtil.ShaderPropertyType.Color)
                                {
                                    string nameLower = name.ToLower();
                                    if (colorSemantics.Contains(nameLower))
                                    {
                                        Color c = mat.GetColor(name);
                                        exp.Color = c;
                                    }
                                }
                            }
                        }
                    }

                    int lightMap = meshRen.lightmapIndex;
                    if (lightMap != -1)
                    {
                        // Register mesh for lightmap render
                        List<GameObject> toRender;
                        if (!lightmapped.TryGetValue(lightMap, out toRender))
                        {
                            toRender = new List<GameObject>();
                            lightmapped.Add(lightMap, toRender);
                        }

                        toRender.Add(root);
                    }
                }
                else if (comp is Collider)
                {
                    Collider col = (Collider)comp;

                    ExportedObject exp = data.exportedObjs.FirstOrDefault(c => c.GameObject == root);
                    if (exp == null)
                    {
                        exp = new ExportedObject();
                        exp.GameObject = root;
                        data.exportedObjs.Add(exp);
                    }
                    exp.Col = col;
                }
                else if (comp is ReflectionProbe)
                {
                    ReflectionProbe probe = (ReflectionProbe)comp;

                    ExportedObject exp = new ExportedObject();
                    exp.GameObject = root;
                    exp.Texture = probe.texture;

                    data.exportedProbes.Add(exp);
                }
            }

            foreach (Transform child in root.transform)
            {
                RecursiveSearch(child.gameObject, data);
            }
        }

        private void Clean()
        {
            skyBoxLeft = null;
            skyBoxRight = null;
            skyBoxForward = null;
            skyBoxBack = null;
            skyBoxUp = null;
            skyBoxDown = null;

            sceneSize = new Bounds();

            if (texturesExportedData != null)
            {
                for (int i = 0; i < texturesExportedData.Count; i++)
                {
                    TextureExportData tex = texturesExportedData[i];

                    if (tex.Created)
                    {
                        // we made this, we delete this
                        UObject.DestroyImmediate(tex.Texture);
                    }

                    if (tex.Preview)
                    {
                        string path = AssetDatabase.GetAssetPath(tex.Preview);
                        if (string.IsNullOrEmpty(path))
                        {
                            // make sure we didnt just copy the Preview because it was the same resolution
                            // as the requested preview
                            UObject.DestroyImmediate(tex.Preview);
                        }
                    }
                }
            }

            if (exported != null)
            {
                exported = null;
            }

            UpdateOnlyHTML = updateOnlyHtml;
        }

        private int GetExponentOf2(int value)
        {
            int times = 1;
            for (int result = value; result > 2; result /= 2)
            {
                times++;
            }
            return times;
        }

        private void DoExport()
        {
            try
            {
                if (!Directory.Exists(exportPath))
                {
                    Directory.CreateDirectory(exportPath);
                }
            }
            catch
            {
                Debug.LogError("Error while creating the export folder!");
                return;
            }

            FBXExporter.LightmappingEnabled = lightmapExportType != LightmapExportType.None;

            int lmapCounter = 0;
            for (int i = 0; i < texturesExportedData.Count; i++)
            {
                TextureExportData tex = texturesExportedData[i];
                string path = AssetDatabase.GetAssetPath(tex.Texture);
                string expPath = Path.Combine(exportPath, tex.Texture.name);

                Texture2D texture = tex.Texture;

                // force power of 2 resolution
                if (!IsPowerOf2(tex.Resolution))
                {
                    // not a power of 2
                    tex.Resolution = Math.Min(NextPowerOf2(tex.Resolution), maxLightMapResolution);
                }

                if (string.IsNullOrEmpty(path))
                {
                    // we created this texture, just export it (were free to read it)
                    tex.ExportedPath = ExportTexture(texture, expPath, defaultTexFormat, tex.Quality, true);
                }
                else
                {
                    string lowerPath = path.ToLower();
                    if (lightmapExportType == LightmapExportType.PackedSourceEXR &&
                        lowerPath.EndsWith("exr"))
                    {
                        lmapCounter++;

                        string expName = "Lightmap" + lmapCounter + ".exr";
                        string destination = Path.Combine(exportPath, expName);

                        tex.ExportedPath = expName;
                        if (!updateOnlyHtml)
                        {
                            if (File.Exists(destination))
                            {
                                File.Delete(destination);
                            }
                            File.Copy(path, destination);
                        }
                    }
                    else
                    {
                        TextureUtil.TempTextureData data = TextureUtil.LockTexture(texture, path);
                        tex.ExportedPath = ExportTexture(texture, expPath, tex.Format, tex.Quality, !data.alphaIsTransparency && !tex.ExportAlpha);
                        TextureUtil.UnlockTexture(data);
                    }
                }
            }

            bool switchUv = lightmapExportType == LightmapExportType.BakedMaterial;

            for (int i = 0; i < meshesExportedData.Count; i++)
            {
                MeshExportData model = meshesExportedData[i];
                string name = meshesNames[model.Mesh];
                string expPath = Path.Combine(exportPath, name);
                ExportMesh(model.Mesh, expPath, model.Format, null, switchUv);
                model.ExportedPath = name + GetMeshFormat(model.Format);
            }

            // Make the index.html file
            StringBuilder index = new StringBuilder("<html>\n\t<head>\n\t\t<title>Janus Unity Exporter v" + Version + "</title>\n\t</head>\n\t<body>\n\t\t<FireBoxRoom>\n\t\t\t<Assets>");

            List<Mesh> meshWritten = new List<Mesh>();

            for (int i = 0; i < exported.exportedObjs.Count; i++)
            {
                ExportedObject obj = exported.exportedObjs[i];
                if (!meshWritten.Contains(obj.Mesh))
                {
                    MeshExportData data = meshesExportedData.FirstOrDefault(c => c.Mesh == obj.Mesh);
                    if (data == null || string.IsNullOrEmpty(data.ExportedPath))
                    {
                        continue;
                    }

                    string path = data.ExportedPath;
                    string name = meshesNames[obj.Mesh];
                    index.Append("\n\t\t\t\t<AssetObject id=\"" + name + "\" src=\"" + path + "\" />");
                    meshWritten.Add(obj.Mesh);
                }
            }

            // textures appear only once, while exported objects can appear multiple times
            // that's why we have a meshwritten list, and not a texturewritten (not anymore at least)
            for (int i = 0; i < texturesExportedData.Count; i++)
            {
                TextureExportData data = texturesExportedData[i];
                string path = data.ExportedPath;
                index.Append("\n\t\t\t\t<AssetImage id=\"" + Path.GetFileNameWithoutExtension(path) + "\" src=\"" + path + "\" />");
            }

            TextureExportData fronttex, backtex, lefttex, righttex, uptex, downtex = null;
            string skyboxdata = "";

            if (exportSkybox)
            {
                fronttex = texturesExportedData.FirstOrDefault(c => c.Texture == skyBoxForward);
                backtex = texturesExportedData.FirstOrDefault(c => c.Texture == skyBoxBack);
                lefttex = texturesExportedData.FirstOrDefault(c => c.Texture == skyBoxLeft);
                righttex = texturesExportedData.FirstOrDefault(c => c.Texture == skyBoxRight);
                uptex = texturesExportedData.FirstOrDefault(c => c.Texture == skyBoxUp);
                downtex = texturesExportedData.FirstOrDefault(c => c.Texture == skyBoxDown);

                if (fronttex != null)
                {
                    skyboxdata += "skybox_front_id=\"" + Path.GetFileNameWithoutExtension(fronttex.ExportedPath) + "\" ";
                }
                if (backtex != null)
                {
                    skyboxdata += "skybox_back_id=\"" + Path.GetFileNameWithoutExtension(backtex.ExportedPath) + "\" ";
                }
                if (lefttex != null)
                {
                    skyboxdata += "skybox_left_id=\"" + Path.GetFileNameWithoutExtension(lefttex.ExportedPath) + "\" ";
                }
                if (righttex != null)
                {
                    skyboxdata += "skybox_right_id=\"" + Path.GetFileNameWithoutExtension(righttex.ExportedPath) + "\" ";
                }
                if (uptex != null)
                {
                    skyboxdata += "skybox_up_id=\"" + Path.GetFileNameWithoutExtension(uptex.ExportedPath) + "\" ";
                }
                if (downtex != null)
                {
                    skyboxdata += "skybox_down_id=\"" + Path.GetFileNameWithoutExtension(downtex.ExportedPath) + "\"";
                }

                if (skyboxdata.EndsWith(" "))
                {
                    skyboxdata = skyboxdata.Remove(skyboxdata.Length - 1, 1);
                }
                skyboxdata = " " + skyboxdata;
            }

            index.Append("\n\t\t\t</Assets>\n\t\t\t<Room far_dist=\"" + farPlaneDistance + "\"" + skyboxdata + ">");

            for (int i = 0; i < exported.exportedObjs.Count; i++)
            {
                ExportedObject obj = exported.exportedObjs[i];
                GameObject go = obj.GameObject;

                string diffuseID = "";
                string lmapID = "";
                if (obj.DiffuseMapTex != null)
                {
                    diffuseID = Path.GetFileNameWithoutExtension(texturesExportedData.First(k => k.Texture == obj.DiffuseMapTex).ExportedPath);
                }
                if (obj.LightMapTex != null)
                {
                    lmapID = Path.GetFileNameWithoutExtension(texturesExportedData.First(k => k.Texture == obj.LightMapTex).ExportedPath);
                }

                CultureInfo c = CultureInfo.InvariantCulture;
                Mesh mesh = obj.Mesh;
                if (mesh == null)
                {
                    continue;
                }

                string meshName = meshesNames[mesh];

                index.Append("\n\t\t\t\t<Object id=\"" + meshName + "\" lighting=\"true\" ");

                if (!string.IsNullOrEmpty(diffuseID))
                {
                    index.Append("image_id=\"" + diffuseID + "\" ");
                }

                if (!string.IsNullOrEmpty(lmapID))
                {
                    index.Append("lmap_id=\"" + lmapID + "\" ");
                    if (lightmapExportType == LightmapExportType.Packed ||
                        lightmapExportType == LightmapExportType.PackedSourceEXR)
                    {
                        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
                        Vector4 lmap = renderer.lightmapScaleOffset;
                        lmap.x = Mathf.Clamp(lmap.x, 0, 1);
                        lmap.y = Mathf.Clamp(lmap.y, 0, 1);
                        lmap.z = Mathf.Clamp(lmap.z, 0, 1);
                        lmap.w = Mathf.Clamp(lmap.w, 0, 1);
                        index.Append("lmap_sca=\"" + lmap.x.ToString(c) + " " + lmap.y.ToString(c) + " " + lmap.z.ToString(c) + " " + lmap.w.ToString(c) + "\" ");
                    }
                }

                if (obj.Tiling != null)
                {
                    Vector4 tiling = obj.Tiling.Value;
                    index.Append("tile=\"" + tiling.x.ToString(c) + " " + tiling.y.ToString(c) + " " + tiling.z.ToString(c) + " " + tiling.w.ToString(c) + "\" ");
                }

                if (obj.Col != null)
                {
                    index.Append("collision_id=\"" + meshName + "\" ");
                }

                if (obj.Color != null)
                {
                    Color objColor = obj.Color.Value;
                    int r = (int)(objColor.r * 255);
                    int g = (int)(objColor.g * 255);
                    int b = (int)(objColor.b * 255);
                    string hex = r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
                    index.Append("col=\"#f" + hex + "\" ");
                }

                Transform trans = go.transform;
                Vector3 pos = trans.position;
                pos *= uniformScale;
                pos.x *= -1;

                Quaternion rot = trans.rotation;
                Vector3 xDir = rot * Vector3.right;
                Vector3 yDir = rot * Vector3.up;
                Vector3 zDir = rot * Vector3.forward;
                xDir.x *= -1;
                yDir.x *= -1;
                zDir.x *= -1;

                Vector3 sca = trans.lossyScale;
                sca *= uniformScale;

                index.Append("pos=\"" + pos.x.ToString(c) + " " + pos.y.ToString(c) + " " + pos.z.ToString(c) + "\" ");
                if (sca.x < 0 || sca.y < 0 || sca.z < 0)
                {
                    index.Append("cull_face=\"front\"" + "\" ");
                }

                index.Append("scale=\"");
                index.Append(sca.x.ToString(c) + " " + sca.y.ToString(c) + " " + sca.z.ToString(c) + "\" ");

                index.Append("xdir=\"");
                index.Append(xDir.x.ToString(c) + " " + xDir.y.ToString(c) + " " + xDir.z.ToString(c) + "\" ");

                index.Append("ydir=\"");
                index.Append(yDir.x.ToString(c) + " " + yDir.y.ToString(c) + " " + yDir.z.ToString(c) + "\" ");

                index.Append("zdir=\"");
                index.Append(zDir.x.ToString(c) + " " + zDir.y.ToString(c) + " " + zDir.z.ToString(c) + "\" ");

                index.Append("/>");

            }

            index.Append("\n\t\t\t</Room>\n\t\t</FireBoxRoom>\n\t</body>\n</html>");

            string indexPath = Path.Combine(exportPath, "index.html");
            File.WriteAllText(indexPath, index.ToString());
        }

        private void ExportMesh(Mesh mesh, string path, ExportMeshFormat format, object data, bool switchUv)
        {
            if (updateOnlyHtml)
            {
                return;
            }

            string formatName = GetMeshFormat(format);
            string finalPath = path + formatName;
            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }

            switch (format)
            {
                case ExportMeshFormat.FBX:
                    FBXExporter.ExportMesh(mesh, finalPath, switchUv);
                    break;
                case ExportMeshFormat.OBJ_NotWorking:
                    break;
            }
        }

        private string ExportTexture(Texture2D tex, string path, ImageFormatEnum format, object data, bool zeroAlpha)
        {
            // see if the exporting format supports alpha
            if (!zeroAlpha && !TextureUtil.SupportsAlpha(format))
            {
                format = ImageFormatEnum.PNG;
            }

            string formatName = GetImageFormatName(format);
            string fileName = tex.name + formatName;

            if (updateOnlyHtml)
            {
                return fileName;
            }
            string fpath = path + formatName;

            try
            {
                using (Stream output = File.OpenWrite(fpath))
                {
                    TextureUtil.ExportTexture(tex, output, format, data, zeroAlpha);
                }
            }
            catch { }

            return fileName;
        }
    }
}
