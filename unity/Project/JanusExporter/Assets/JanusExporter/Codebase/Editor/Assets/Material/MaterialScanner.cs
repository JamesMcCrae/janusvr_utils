﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace JanusVR
{
    public class MaterialScanner
    {
        private JanusRoom room;
        private List<string> textureNames;
        private Dictionary<int, List<RoomObject>> lightmapped;
        private List<string> colors;

        public MaterialScanner(JanusRoom room)
        {
            this.room = room;

            textureNames = new List<string>();
            lightmapped = new Dictionary<int, List<RoomObject>>();
            colors = new List<string>();
        }

        public void Initialize()
        {
        }

        public void PreProcessObject(MeshRenderer renderer, Mesh mesh, AssetObject obj, RoomObject rObj, bool assignLightmapScale, int subMesh)
        {
            LightmapExportType lightmapExportType = room.LightmapType;
            ExportTextureFormat format = room.TextureFormat;

            if (lightmapExportType != LightmapExportType.None)
            {
                int lightMap = renderer.lightmapIndex;
                if (lightMap != -1)
                {
                    // Register mesh for lightmap render
                    List<RoomObject> toRender;
                    if (!lightmapped.TryGetValue(lightMap, out toRender))
                    {
                        toRender = new List<RoomObject>();
                        lightmapped.Add(lightMap, toRender);
                    }

                    toRender.Add(rObj);

                    if (assignLightmapScale)
                    {
                        if (lightmapExportType == LightmapExportType.Packed)
                        {
                            Vector4 lmap = renderer.lightmapScaleOffset;
                            lmap.x = Mathf.Clamp(lmap.x, -2, 2);
                            lmap.y = Mathf.Clamp(lmap.y, -2, 2);
                            lmap.z = Mathf.Clamp(lmap.z, -2, 2);
                            lmap.w = Mathf.Clamp(lmap.w, -2, 2);
                            rObj.SetLightmap(lmap);
                        }
                    }

                    if (lightmapExportType != LightmapExportType.BakedMaterial)
                    {
                        // check if we already have the texture
                        string lmapId = "Lightmap" + lightMap;
                        AssetImage lmapImage = room.TryGetTexture(lmapId);
                        if (lmapImage == null)
                        {
                            lmapImage = new AssetImage();
                            lmapImage.id = lmapId;
                            lmapImage.src = lmapId;
                            room.AddAssetImage(lmapImage);
                        }
                        rObj.lmap_id = lmapImage.id;
                    }
                }
            }

            switch (lightmapExportType)
            {
                case LightmapExportType.BakedMaterial:
                case LightmapExportType.Unpacked:
                    {
                        AssetImage image = new AssetImage();
                        string imgId = obj.id + "_Baked";

                        image.id = imgId;
                        image.src = imgId;

                        rObj.image_id = image.id;

                        room.AddAssetImage(image);
                    }
                    break;
            }

            Material[] mats = renderer.sharedMaterials;
            if (lightmapExportType != LightmapExportType.BakedMaterial &&
                room.ExportMaterials)
            {
                // search for textures/color on object
                //for (int i = 0; i < mats.Length; i++)
                //{
                //    Material mat = mats[i];
                //    if (room.TryGetMaterial(mat.name) == null)
                //    {
                //        AssetMaterial assetMat = new AssetMaterial();
                //        assetMat.id = mat.name;
                //        room.AddAssetMaterial(assetMat);

                //        Shader shader = mat.shader;

                //        Texture2D diffuseTexture;
                //        Color? objColor;
                //        ExtractFromMaterial(mat, out objColor, out diffuseTexture);

                //        AssetImage image = RegisterImage(diffuseTexture);
                //        assetMat.tex0 = image;
                //        assetMat.col = JanusUtil.FormatColor(objColor.Value);

                //        rObj.mat_id = assetMat.id;
                //    }
                //}
            }
            else
            {
                if (mats.Length > subMesh)
                {
                    Material mat = mats[subMesh];
                    if (mat != null)
                    {
                        Texture2D diffuseTexture;
                        Color? objColor;
                        ExtractFromMaterial(mat, out objColor, out diffuseTexture);
                        if (objColor != null)
                        {
                            rObj.col = JanusUtil.FormatColor(objColor.Value);
                        }

                        AssetImage image = RegisterImage(diffuseTexture);
                        rObj.image_id = image;
                    }
                }
            }
        }

        private AssetImage RegisterImage(Texture2D texture)
        {
            if (!texture)
            {
                return null;
            }

            if (textureNames.Contains(texture.name))
            {
                AssetImage img = room.AssetImages.FirstOrDefault(c => c.Texture == texture);
                return img;
            }
            textureNames.Add(texture.name);

            AssetImage image = new AssetImage();
            image.Texture = texture;
            image.id = texture.name;
            image.src = texture.name;
            room.AddAssetImage(image);
            return image;
        }

        private void ExtractFromMaterial(Material mat, out Color? objColor, out Texture2D diffuseTexture)
        {
            Shader shader = mat.shader;

            diffuseTexture = null;
            objColor = null;

            int props = ShaderUtil.GetPropertyCount(shader);
            for (int k = 0; k < props; k++)
            {
                string name = ShaderUtil.GetPropertyName(shader, k);

                ShaderUtil.ShaderPropertyType propType = ShaderUtil.GetPropertyType(shader, k);
                if (propType == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    if (JanusGlobals.SemanticsMainTex.Contains(name.ToLower()))
                    {
                        // main texture texture
                        Texture matTex = mat.GetTexture(name);
                        if (matTex is Texture2D)
                        {
                            diffuseTexture = (Texture2D)matTex;
                        }
                    }
                }
                else if (propType == ShaderUtil.ShaderPropertyType.Color)
                {
                    if (JanusGlobals.SemanticsColor.Contains(name.ToLower()))
                    {
                        objColor = mat.GetColor(name);
                    }
                }
            }

            if (diffuseTexture == null && room.ExportMaterialColorsAsTextures && objColor.HasValue)
            {
                // render the color to a 2x2 texture we can show in Janus as the diffuse
                diffuseTexture = new Texture2D(2, 2, TextureFormat.RGB24, false, false);

                Color objCol = objColor.Value;
                diffuseTexture.SetPixels(new Color[] { objCol, objCol, objCol, objCol });
                diffuseTexture.Apply();

                string colorName = "ExportedColor" + colors.Count;
                colors.Add(colorName);
                diffuseTexture.name = colorName;
                objColor = null;
            }
        }

        public void ProcessTextures()
        {
            LightmapExportType lightmapExportType = room.LightmapType;
            LightmapTextureFormat lightmapTextureFormat = room.LightmapTextureFormat;
            ExportTextureFormat format = room.TextureFormat;

            // export everything that has already been loaded
            List<AssetImage> assetImages = room.AssetImages;
            for (int i = 0; i < assetImages.Count; i++)
            {
                AssetImage image = assetImages[i];
                if (image.Texture || (room.ExportOnlyHtml && image.Created))
                {
                    if (image.Texture)
                    {
                        EditorUtility.DisplayProgressBar("Exporting main textures...", image.Texture.name, i / (float)assetImages.Count);
                    }
                    ExportTexture(image.Texture, format, image, true);
                }
            }

            switch (lightmapExportType)
            {
                case LightmapExportType.BakedMaterial:
                    ProcessLightmapsBaked();
                    break;
                case LightmapExportType.Packed:
                    {
                        switch (lightmapTextureFormat)
                        {
                            case LightmapTextureFormat.EXR:
                                ProcessLightmapsPackedSourceEXR();
                                break;
                            default:
                                ProcessLightmapsPacked();
                                break;
                        }
                    }
                    break;
                case LightmapExportType.Unpacked:
                    ProcessLightmapsUnpacked();
                    break;
            }
        }

        private void UpdateTextureFormat(ExportTextureFormat format, AssetImage asset, string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                asset.src = asset.src + JanusUtil.GetImageExtension(format);
            }
            else
            {
                asset.src = asset.src + extension;
            }
        }

        private void ExportTexture(Texture2D texture, ExportTextureFormat format, AssetImage asset, bool canCopy)
        {
            // force PNG if alpha??
            string sourcePath = AssetDatabase.GetAssetPath(asset.Texture);
            string sourceExtension = Path.GetExtension(sourcePath);
            bool forceConversion = room.TextureForceReExport && !JanusUtil.IgnoreReExport(sourcePath);

            string rootDir = room.RootFolder;

            if (!forceConversion && canCopy && JanusUtil.SupportsImageFormat(sourceExtension))
            {
                asset.src = asset.src + sourceExtension;
                if (room.ExportOnlyHtml)
                {
                    return;
                }
                string texPath = Path.Combine(rootDir, asset.src);
                File.Copy(sourcePath, texPath, true);
            }
            else
            {
                try
                {
                    asset.src = asset.src + JanusUtil.GetImageExtension(format);
                    if (room.ExportOnlyHtml)
                    {
                        return;
                    }
                    string texPath = Path.Combine(rootDir, asset.src);

                    using (Stream output = File.OpenWrite(texPath))
                    {
                        TempTextureData data = TextureUtil.LockTexture(texture, sourcePath);
                        TextureUtil.ExportTexture(texture, output, format, room.TextureData, false);
                        TextureUtil.UnlockTexture(data);
                    }
                }
                catch
                {
                    Debug.LogError("Failure exporting texture " + asset.id);
                }
            }
        }

        private void ProcessLightmapsPackedSourceEXR()
        {
            if (room.ExportOnlyHtml)
            {
                foreach (var lightPair in lightmapped)
                {
                    int id = lightPair.Key;
                    string imgId = "Lightmap" + id;
                    UpdateTextureFormat(ExportTextureFormat.PNG, room.GetTexture(imgId), ".exr");
                }
                return;
            }

            string lightMapsFolder = UnityUtil.GetLightmapsFolder();

            foreach (var lightPair in lightmapped)
            {
                int id = lightPair.Key;
                List<RoomObject> toRender = lightPair.Value;
                AssetImage lmapImage = room.TryGetTexture("Lightmap" + id);

                // get the path to the lightmap file
                string lightMapFile = Path.Combine(lightMapsFolder, "Lightmap-" + id + "_comp_light.exr");
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(lightMapFile);
                if (texture == null)
                {
                    continue;
                }

                string sourcePath = AssetDatabase.GetAssetPath(texture);
                string sourceExtension = Path.GetExtension(sourcePath);
                string rootDir = room.RootFolder;

                lmapImage.src = lmapImage.src + sourceExtension;
                string texPath = Path.Combine(rootDir, lmapImage.src);
                File.Copy(sourcePath, texPath, true);
            }
        }

        public static bool ProcessExposure(int id, float fStops)
        {
            string lightMapsFolder = UnityUtil.GetLightmapsFolder();

            DirectoryInfo sceneDir = new DirectoryInfo(lightMapsFolder);
            FileInfo[] maps = sceneDir.GetFiles("*.exr");
            FileInfo first = maps.FirstOrDefault(c => c.Name.Contains("_comp_light"));
            if (first == null)
            {
                return false;
            }

            string lightMapFile = Path.Combine(lightMapsFolder, first.Name);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(lightMapFile);
            if (texture == null)
            {
                return false;
            }
            Material exposureMat = JanusResources.ExposureMaterial;
            exposureMat.SetFloat("_RelFStops", fStops);
            exposureMat.SetFloat("_IsLinear", PlayerSettings.colorSpace == ColorSpace.Linear ? 1 : 0);
            exposureMat.SetTexture("_InputTex", texture);

            // We need to access unity_Lightmap_HDR to decode the lightmap,
            // but we can't, so we have to render everything to a custom RenderTexture!
            Texture2D decTex = JanusResources.TempRenderTexture;
            decTex.name = "LightmapPreview";

            RenderTexture renderTexture = RenderTexture.GetTemporary(decTex.width, decTex.height);
            Graphics.SetRenderTarget(renderTexture);
            GL.Clear(true, true, new Color(0, 0, 0, 0)); // clear to transparent

            exposureMat.SetPass(0);
            Graphics.DrawMeshNow(JanusResources.PlaneMesh, Matrix4x4.identity);

            decTex.ReadPixels(new Rect(0, 0, decTex.width, decTex.height), 0, 0);
            decTex.Apply();

            Graphics.SetRenderTarget(null);
            RenderTexture.ReleaseTemporary(renderTexture);
            return true;
        }

        private void ProcessLightmapsPacked()
        {
            ExportTextureFormat format = room.TextureFormat;
            if (room.ExportOnlyHtml)
            {
                foreach (var lightPair in lightmapped)
                {
                    int id = lightPair.Key;
                    string imgId = "Lightmap" + id;
                    UpdateTextureFormat(format, room.GetTexture(imgId), "");
                }
                return;
            }

            string lightMapsFolder = UnityUtil.GetLightmapsFolder();

            Shader exposureShader = Shader.Find("Hidden/ExposureShader");
            JanusUtil.AssertShader(exposureShader);

            Material exposureMat = new Material(exposureShader);
            exposureMat.SetPass(0);
            exposureMat.SetFloat("_RelFStops", room.LightmapRelFStops);
            exposureMat.SetFloat("_IsLinear", PlayerSettings.colorSpace == ColorSpace.Linear ? 1 : 0);

            foreach (var lightPair in lightmapped)
            {
                int id = lightPair.Key;
                AssetImage lmapImage = room.TryGetTexture("Lightmap" + id);
                if (lmapImage == null)
                {
                    continue;
                }

                // get the path to the lightmap file
                string lightMapFile = Path.Combine(lightMapsFolder, "Lightmap-" + id + "_comp_light.exr");
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(lightMapFile);
                if (texture == null)
                {
                    continue;
                }

                exposureMat.SetTexture("_InputTex", texture);

                // We need to access unity_Lightmap_HDR to decode the lightmap,
                // but we can't, so we have to render everything to a custom RenderTexture!
                Texture2D decTex = new Texture2D(texture.width, texture.height);
                decTex.name = "Lightmap" + id;
                //texturesExported.Add(decTex);

                RenderTexture renderTexture = RenderTexture.GetTemporary(texture.width, texture.height);
                Graphics.SetRenderTarget(renderTexture);
                GL.Clear(true, true, new Color(0, 0, 0, 0)); // clear to transparent

                exposureMat.SetPass(0);
                Graphics.DrawMeshNow(JanusResources.PlaneMesh, Matrix4x4.identity);

                decTex.ReadPixels(new Rect(0, 0, decTex.width, decTex.height), 0, 0);

                Graphics.SetRenderTarget(null);
                RenderTexture.ReleaseTemporary(renderTexture);

                // save the lightmap file
                ExportTexture(decTex, format, lmapImage, false);
                UObject.DestroyImmediate(decTex);
            }
            UObject.DestroyImmediate(exposureMat);
        }

        private void ProcessLightmapsUnpacked()
        {
            ExportTextureFormat format = room.TextureFormat;
            if (room.ExportOnlyHtml)
            {
                foreach (var lightPair in lightmapped)
                {
                    int id = lightPair.Key;
                    List<RoomObject> toRender = lightPair.Value;

                    for (int i = 0; i < toRender.Count; i++)
                    {
                        RoomObject rObj = toRender[i];
                        string imgId = rObj.id + "_Baked";
                        UpdateTextureFormat(format, room.GetTexture(imgId), "");
                    }
                }
                return;
            }

            string lightMapsFolder = UnityUtil.GetLightmapsFolder();

            Shader lightMapShader = Shader.Find("Hidden/LMapUnpacked");
            JanusUtil.AssertShader(lightMapShader);

            Material lightMap = new Material(lightMapShader);
            lightMap.SetPass(0);
            lightMap.SetFloat("_RelFStops", room.LightmapRelFStops);
            lightMap.SetFloat("_IsLinear", PlayerSettings.colorSpace == ColorSpace.Linear ? 1 : 0);

            // export lightmaps
            int lmap = 0;
            foreach (var lightPair in lightmapped)
            {
                int id = lightPair.Key;
                List<RoomObject> toRender = lightPair.Value;

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
                    RoomObject rObj = toRender[i];
                    GameObject obj = rObj.UnityObj;
                    MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
                    MeshFilter filter = obj.GetComponent<MeshFilter>();

                    Mesh mesh = filter.sharedMesh;
                    Transform trans = obj.transform;
                    Matrix4x4 world = Matrix4x4.TRS(trans.position, trans.rotation, trans.lossyScale);

                    Vector4 scaleOffset = renderer.lightmapScaleOffset;
                    float width = (1 - scaleOffset.z) * scaleOffset.x;
                    float height = (1 - scaleOffset.w) * scaleOffset.y;
                    float size = Math.Max(width, height);

                    int lightMapSize = (int)(room.LightmapMaxResolution * size);
                    lightMapSize = (int)Math.Pow(2, Math.Ceiling(Math.Log(lightMapSize) / Math.Log(2)));
                    lightMapSize = Math.Min(room.LightmapMaxResolution, Math.Max(lightMapSize, 16));

                    RenderTexture renderTexture = RenderTexture.GetTemporary(lightMapSize, lightMapSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.SetRenderTarget(renderTexture);
                    GL.Clear(true, true, new Color(0, 0, 0, 0)); // clear to transparent

                    Material[] mats = renderer.sharedMaterials;
                    lightMap.SetVector("_LightMapUV", renderer.lightmapScaleOffset);

                    for (int j = 0; j < mats.Length; j++)
                    {
                        //Material mat = mats[j];
                        lightMap.SetPass(0);
                        Graphics.DrawMeshNow(mesh, world, j);
                    }

                    // This is the only way to access data from a RenderTexture
                    Texture2D tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false, false);
                    tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                    string imgId = rObj.id + "_Baked";
                    tex.name = imgId;
                    tex.Apply(); // send the data back to the GPU so we can draw it on the preview area

                    Graphics.SetRenderTarget(null);
                    RenderTexture.ReleaseTemporary(renderTexture);

                    lmap++;

                    // save the texture file
                    ExportTexture(tex, format, room.GetTexture(rObj.image_id), false);
                    UObject.DestroyImmediate(tex);
                }
            }
            UObject.DestroyImmediate(lightMap);
        }

        private void ProcessLightmapsBaked()
        {
            ExportTextureFormat format = room.TextureFormat;

            if (room.ExportOnlyHtml)
            {
                foreach (var lightPair in lightmapped)
                {
                    int id = lightPair.Key;
                    List<RoomObject> toRender = lightPair.Value;

                    for (int i = 0; i < toRender.Count; i++)
                    {
                        RoomObject rObj = toRender[i];
                        string imgId = rObj.id + "_Baked";
                        UpdateTextureFormat(format, room.GetTexture(imgId), "");
                    }
                }
                return;
            }

            string lightMapsFolder = UnityUtil.GetLightmapsFolder();

            // only load shader now, so if the user is not exporting lightmaps
            // he doesn't need to have it on his project folder
            Shader lightMapShader = Shader.Find("Hidden/LMapBaked");
            JanusUtil.AssertShader(lightMapShader);

            Material lightMap = new Material(lightMapShader);
            lightMap.SetPass(0);
            lightMap.SetFloat("_RelFStops", room.LightmapRelFStops);
            lightMap.SetFloat("_IsLinear", PlayerSettings.colorSpace == ColorSpace.Linear ? 1 : 0);

            // export lightmaps
            int lmap = 0;
            foreach (var lightPair in lightmapped)
            {
                int id = lightPair.Key;
                List<RoomObject> toRender = lightPair.Value;

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
                    RoomObject rObj = toRender[i];
                    GameObject obj = rObj.UnityObj;
                    MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
                    MeshFilter filter = obj.GetComponent<MeshFilter>();

                    Mesh mesh = filter.sharedMesh;
                    Transform trans = obj.transform;
                    Matrix4x4 world = Matrix4x4.TRS(trans.position, trans.rotation, trans.lossyScale);

                    Vector4 scaleOffset = renderer.lightmapScaleOffset;
                    float width = (1 - scaleOffset.z) * scaleOffset.x;
                    float height = (1 - scaleOffset.w) * scaleOffset.y;
                    float size = Math.Max(width, height);

                    // guarantee were not scaling stuff up
                    int maxLmapRes = Math.Min(room.LightmapMaxResolution, texture.width);
                    int lightMapSize = (int)(maxLmapRes * size);
                    lightMapSize = (int)Math.Pow(2, Math.Ceiling(Math.Log(lightMapSize) / Math.Log(2)));
                    lightMapSize = Math.Min(maxLmapRes, Math.Max(lightMapSize, 16));

                    RenderTexture renderTexture = RenderTexture.GetTemporary(lightMapSize, lightMapSize, 0, RenderTextureFormat.ARGB32);
                    Graphics.SetRenderTarget(renderTexture);
                    GL.Clear(true, true, new Color(0, 0, 0, 0)); // clear to transparent

                    Material[] mats = renderer.sharedMaterials;
                    for (int j = 0; j < mats.Length; j++)
                    {
                        Material mat = mats[j];

                        // clear to default
                        lightMap.SetTexture("_MainTex", EditorGUIUtility.whiteTexture);
                        lightMap.SetColor("_Color", Color.white);

                        // uvs
                        lightMap.SetVector("_LightMapUV", renderer.lightmapScaleOffset);

                        Shader shader = mat.shader;
                        int props = ShaderUtil.GetPropertyCount(shader);
                        for (int k = 0; k < props; k++)
                        {
                            string name = ShaderUtil.GetPropertyName(shader, k);

                            ShaderUtil.ShaderPropertyType propType = ShaderUtil.GetPropertyType(shader, k);
                            if (propType == ShaderUtil.ShaderPropertyType.TexEnv)
                            {
                                if (JanusGlobals.SemanticsMainTex.Contains(name.ToLower()))
                                {
                                    // main texture texture
                                    Texture matTex = mat.GetTexture(name);
                                    if (matTex)
                                    {
                                        lightMap.SetTexture("_MainTex", matTex);
                                    }
                                }
                            }
                            else if (propType == ShaderUtil.ShaderPropertyType.Color)
                            {
                                if (JanusGlobals.SemanticsColor.Contains(name.ToLower()))
                                {
                                    lightMap.SetColor("_Color", mat.GetColor(name));
                                }
                            }
                        }

                        lightMap.SetPass(0);
                        Graphics.DrawMeshNow(mesh, world, j);
                    }

                    // This is the only way to access data from a RenderTexture
                    Texture2D tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false, false);
                    tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                    string imgId = rObj.id + "_Baked";

                    tex.name = imgId;

                    Graphics.SetRenderTarget(null);
                    RenderTexture.ReleaseTemporary(renderTexture);

                    lmap++;

                    // save the texture file
                    ExportTexture(tex, format, room.GetTexture(rObj.image_id), false);
                    UObject.DestroyImmediate(tex);
                }
            }
            UObject.DestroyImmediate(lightMap);
        }
    }
}