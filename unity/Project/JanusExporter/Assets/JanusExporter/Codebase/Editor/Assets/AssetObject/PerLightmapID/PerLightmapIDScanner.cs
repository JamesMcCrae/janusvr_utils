﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace JanusVR
{
    public class PerLightmapIDScanner : ObjectScanner
    {
        private Dictionary<int, PerMaterialMeshExportData> meshesToExport;
        private JanusRoom room;
        private LightmapIDScanner lightmapIdScanner;
        private JanusComponentExtractor compExtractor;
        private Bounds sceneBounds;

        public override void Initialize(JanusRoom room, GameObject[] rootObjects)
        {
            this.room = room;

            lightmapIdScanner = new LightmapIDScanner(room);
            compExtractor = new JanusComponentExtractor(room);

            meshesToExport = new Dictionary<int, PerMaterialMeshExportData>();

            EditorUtility.DisplayProgressBar("Janus VR Exporter", "Per lightmap id scanning for AssetObjects...", 0.0f);
            lightmapIdScanner.Initialize();

            for (int i = 0; i < rootObjects.Length; i++)
            {
                GameObject root = rootObjects[i];
                RecursiveSearch(root);
            }

            room.FarPlaneDistance = (int)Math.Max(500, sceneBounds.size.magnitude * 1.3f);
        }

        public void RecursiveSearch(GameObject root)
        {
            if (room.IgnoreInactiveObjects &&
                !root.activeInHierarchy)
            {
                return;
            }

            // loop thorugh all the gameObjects components
            // to see what we can extract
            Component[] comps = root.GetComponents<Component>();
            if (!compExtractor.CanExport(comps))
            {
                compExtractor.Process(comps);
                return;
            }

            for (int i = 0; i < comps.Length; i++)
            {
                Component comp = comps[i];
                if (!comp)
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
                    if (mesh == null ||
                        !room.CanExportObj(comps))
                    {
                        continue;
                    }

                    Material mat = meshRen.sharedMaterial;
                    int lmapId = meshRen.lightmapIndex;
                    if (!mat || lmapId == -1)
                    {
                        continue;
                    }

                    if (!room.ExportDynamicGameObjects &&
                         !root.isStatic)
                    {
                        continue;
                    }

                    sceneBounds.Encapsulate(meshRen.bounds);

                    PerMaterialMeshExportData data;
                    if (!meshesToExport.TryGetValue(lmapId, out data))
                    {
                        data = new PerMaterialMeshExportData();
                        data.LightmapEnabled = true;
                        meshesToExport.Add(lmapId, data);

                        AssetObject asset = new AssetObject();
                        asset.id = mat.name;
                        asset.src = mat.name + ".fbx";
                        room.AddAssetObject(asset);

                        RoomObject obj = new RoomObject();
                        data.Object = obj;
                        obj.id = mat.name;
                        obj.SetNoUnityObj(room);

                        room.AddRoomObject(obj);
                        compExtractor.ProcessNewRoomObject(obj, comps);
                        lightmapIdScanner.PreProcessObject(meshRen, mesh, asset, obj, false);
                    }

                    PerMaterialMeshExportDataObj dObj = new PerMaterialMeshExportDataObj();
                    dObj.LightmapEnabled = true;
                    dObj.Mesh = mesh;
                    dObj.Transform = root.transform;
                    dObj.Renderer = meshRen;
                    data.Meshes.Add(dObj);
                }
            }

            // loop through all this GameObject children
            foreach (Transform child in root.transform)
            {
                RecursiveSearch(child.gameObject);
            }
        }

        public override void ExportAssetImages()
        {
            // process textures
            lightmapIdScanner.ProcessTextures();
        }

        public MeshData GetMeshData(int index, PerMaterialMeshExportData data)
        {
            MeshData meshData = new MeshData();
            List<PerMaterialMeshExportDataObj> objs = data.Meshes;

            meshData.Name = "Mesh" + index;

            // pre-calc
            List<Vector3> allVertices = new List<Vector3>();
            List<Vector3> allNormals = new List<Vector3>();
            List<int> allTriangles = new List<int>();
            List<List<Vector2>> allUVs = new List<List<Vector2>>();

            int uvLayers = BruteForceObjectScanner.LightmapNeedsUV1(room) ? 2 : 1;
            for (int i = 0; i < uvLayers; i++)
            {
                allUVs.Add(new List<Vector2>());
            }

            for (int i = 0; i < objs.Count; i++)
            {
                PerMaterialMeshExportDataObj obj = objs[i];
                Transform trans = obj.Transform;
                Mesh mesh = obj.Mesh;

                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                int[] triangles = mesh.triangles;

                string meshPath = AssetDatabase.GetAssetPath(mesh);
                if (!string.IsNullOrEmpty(meshPath))
                {
                    ModelImporter importer = (ModelImporter)ModelImporter.GetAtPath(meshPath);
                    if (importer != null)
                    {
                        meshData.Scale = importer.globalScale;
                    }
                }

                if (vertices == null || vertices.Length == 0)
                {
                    Debug.LogWarning("Mesh is empty " + mesh.name, mesh);
                    return null;
                }
                // check if we have all the data
                int maximum = triangles.Max();
                if (normals.Length < maximum)
                {
                    Debug.LogWarning("Mesh has not enough normals - " + mesh.name, mesh);
                    return null;
                }

                int start = allVertices.Count;

                // transform vertices and normals
                for (int j = 0; j < vertices.Length; j++)
                {
                    Vector3 vertex = vertices[j];
                    Vector3 normal = normals[j];
                    Vector3 nWorldSpace = trans.TransformDirection(normal);
                    Vector3 vWorldSpace = trans.TransformPoint(vertex);
                    allVertices.Add(vWorldSpace);
                    allNormals.Add(nWorldSpace);
                }

                for (int j = 0; j < triangles.Length; j++)
                {
                    // offset the triangle indexes
                    int tri = triangles[j];
                    allTriangles.Add(tri + start);
                }

                Vector2[][] uvs = BruteForceObjectScanner.GetMeshUVs(room, mesh, obj.LightmapEnabled, vertices.Length);
                for (int j = 0; j < allUVs.Count; j++)
                {
                    List<Vector2> uvLayer = allUVs[j];
                    if (j == 1)
                    {
                        // recalculate lightmap UVs
                        Vector4 lmap = obj.Renderer.lightmapScaleOffset;
                        Vector2[] uv = uvs[j];

                        for (int k = 0; k < uv.Length; k++)
                        {
                            Vector2 tvert = uv[k];
                            tvert.x *= lmap.x;
                            tvert.y *= lmap.y;
                            tvert.x += lmap.z;
                            tvert.y += lmap.w;
                            uvLayer.Add(tvert);
                        }
                    }
                    else
                    {
                        uvLayer.AddRange(uvs[j]);
                    }
                }
            }

            meshData.Vertices = allVertices.ToArray();
            meshData.Normals = allNormals.ToArray();
            meshData.Triangles = allTriangles.ToArray();
            Vector2[][] meshUVs = new Vector2[2][];
            for (int j = 0; j < allUVs.Count; j++)
            {
                meshUVs[j] = allUVs[j].ToArray();
            }
            meshData.UV = meshUVs;

            return meshData;
        }

        public override void ExportAssetObjects()
        {
            if (room.ExportOnlyHtml)
            {
                return;
            }

            int exported = 0;
            MeshExporter exporter = room.GetMeshExporter(ExportMeshFormat.FBX);
            MeshExportParameters parameters = new MeshExportParameters(false, true);

            foreach (var pair in meshesToExport)
            {
                int index = pair.Key;
                PerMaterialMeshExportData data = pair.Value;

                MeshData meshData = GetMeshData(index, data);

                //Mesh mesh = data.Mesh;
                string meshId = meshData.Name;
                exporter.ExportMesh(meshData, meshId, parameters);
                exported++;
                EditorUtility.DisplayProgressBar("Exporting meshes...", meshId + ".fbx", exported / (float)meshesToExport.Count);
            }
        }

    }
}
