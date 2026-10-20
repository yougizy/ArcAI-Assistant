using System;
using System.Collections.Generic;
using System.IO;
using ESRI.ArcGIS.ArcMapUI;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.Framework;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.Geometry;
using Newtonsoft.Json.Linq;
using Path = System.IO.Path;

namespace ArcAI.AddIn
{
    /// <summary>
    /// Captures a compact, token-bounded description of the live ArcMap session.
    /// This is sent to the LLM only when the user enables "发送当前地图上下文".
    /// </summary>
    internal static class ArcMapContext
    {
        private const int MaxLayers = 500;

        public static JObject Capture(IApplication app)
        {
            var root = new JObject();
            root["captured_at"] = DateTime.Now.ToString("s");
            root["arcgis"] = ArcSession.VersionText();
            root["output_dir"] = CreateOutputDir();

            try
            {
                IMxDocument doc = ArcSession.Doc(app);
                IMap map = doc.FocusMap;
                string mxdPath = ArcSession.MxdPath(app);

                var document = new JObject();
                document["path"] = mxdPath == null ? JValue.CreateNull() : new JValue(mxdPath);
                document["name"] = string.IsNullOrEmpty(mxdPath)
                    ? "未保存的地图文档"
                    : Path.GetFileName(mxdPath);
                root["document"] = document;

                var mapInfo = new JObject();
                mapInfo["name"] = SafeMapName(map);
                mapInfo["map_units"] = SafeMapUnits(map);
                mapInfo["spatial_reference"] = SafeSpatialReference(map.SpatialReference);
                mapInfo["layer_count"] = SafeLayerCount(map);
                mapInfo["scale"] = SafeScale(doc);
                mapInfo["extent"] = SafeExtent(doc);
                root["map"] = mapInfo;

                var layers = new JArray();
                int total = 0;
                int count = SafeLayerCount(map);
                for (int i = 0; i < count; i++)
                {
                    ILayer layer = null;
                    try { layer = map.get_Layer(i); }
                    catch { continue; }
                    CollectLayer(layer, string.Empty, layers, ref total, mxdPath);
                }
                root["layers"] = layers;
                root["layers_truncated"] = total > MaxLayers;
                root["data_paths"] = BuildDataPathCatalog(layers);

                try
                {
                    ILayer selected = doc.SelectedLayer as ILayer;
                    root["selected_layer"] = selected == null ? JValue.CreateNull() : new JValue(SafeLayerName(selected));
                }
                catch
                {
                    root["selected_layer"] = JValue.CreateNull();
                }
            }
            catch (Exception ex)
            {
                root["error"] = ex.Message;
                Log.Error("ArcMapContext.Capture failed.", ex);
            }
            return root;
        }

        private static void CollectLayer(ILayer layer, string parent, JArray destination,
            ref int total, string mxdPath)
        {
            if (layer == null)
                return;
            total++;
            if (destination.Count < MaxLayers)
                destination.Add(DescribeLayer(layer, parent, mxdPath));

            ICompositeLayer composite = layer as ICompositeLayer;
            if (composite == null)
                return;
            int count;
            try { count = composite.Count; }
            catch { return; }
            for (int i = 0; i < count; i++)
            {
                ILayer child = null;
                try { child = composite.get_Layer(i); }
                catch { continue; }
                string childParent = parent.Length == 0 ? SafeLayerName(layer) : parent + "/" + SafeLayerName(layer);
                CollectLayer(child, childParent, destination, ref total, mxdPath);
            }
        }

        private static JObject DescribeLayer(ILayer layer, string parent, string mxdPath)
        {
            var info = new JObject();
            info["name"] = SafeLayerName(layer);
            info["path"] = parent.Length == 0 ? SafeLayerName(layer) : parent + "/" + SafeLayerName(layer);
            try { info["visible"] = layer.Visible; } catch { }
            try { info["valid"] = layer.Valid; } catch { }

            string fullPath = null;
            try
            {
                IDataLayer dataLayer = layer as IDataLayer;
                IDatasetName datasetName = dataLayer == null ? null : dataLayer.DataSourceName as IDatasetName;
                if (datasetName != null)
                {
                    info["dataset"] = datasetName.Name;
                    try { info["dataset_type"] = datasetName.Type.ToString(); } catch { }
                    try
                    {
                        IWorkspaceName workspaceName = datasetName.WorkspaceName;
                        if (workspaceName != null)
                        {
                            string workspace = ResolvePath(workspaceName.PathName, mxdPath);
                            info["workspace"] = workspace;
                            fullPath = ResolvePath(CombineDatasetPath(datasetName), mxdPath);
                        }
                    }
                    catch { }
                }
            }
            catch { }

            try
            {
                IFeatureLayer featureLayer = layer as IFeatureLayer;
                if (featureLayer != null && featureLayer.FeatureClass != null)
                {
                    info["layer_type"] = "feature";
                    info["shape_type"] = featureLayer.FeatureClass.ShapeType.ToString();
                    IDataset dataset = featureLayer.FeatureClass as IDataset;
                    if (dataset != null)
                    {
                        if (info["dataset"] == null)
                            info["dataset"] = dataset.Name;
                        string datasetPath = DatasetPath(dataset, mxdPath);
                        if (!string.IsNullOrWhiteSpace(datasetPath))
                            fullPath = datasetPath;
                    }
                    IGeoDataset geoDataset = featureLayer.FeatureClass as IGeoDataset;
                    if (geoDataset != null)
                    {
                        info["spatial_reference"] = SafeSpatialReference(geoDataset.SpatialReference);
                        info["extent"] = ExtentToken(geoDataset.Extent);
                    }
                    info["fields"] = DescribeFields(featureLayer.FeatureClass);
                    IFeatureLayerDefinition definition = featureLayer as IFeatureLayerDefinition;
                    if (definition != null)
                    {
                        string query = definition.DefinitionExpression;
                        if (!string.IsNullOrWhiteSpace(query))
                            info["definition_query"] = query;
                    }
                }
            }
            catch { }

            try
            {
                IRasterLayer raster = layer as IRasterLayer;
                if (raster != null)
                {
                    info["layer_type"] = "raster";
                    info["raster"] = raster.FilePath;
                    if (!string.IsNullOrWhiteSpace(raster.FilePath))
                        fullPath = ResolvePath(raster.FilePath, mxdPath);
                    IRasterDataset dataset = raster.Raster as IRasterDataset;
                    if (dataset != null)
                    {
                        IGeoDataset geoDataset = dataset as IGeoDataset;
                        if (geoDataset != null)
                        {
                            info["spatial_reference"] = SafeSpatialReference(geoDataset.SpatialReference);
                            info["extent"] = ExtentToken(geoDataset.Extent);
                        }
                    }
                }
            }
            catch { }

            if (info["layer_type"] == null)
            {
                if (layer is ICompositeLayer)
                    info["layer_type"] = "group";
                else
                    info["layer_type"] = layer.GetType().Name;
            }

            if (!string.IsNullOrWhiteSpace(fullPath))
                info["full_path"] = NormalizeDataSourcePath(fullPath);

            try
            {
                IFeatureSelection selection = layer as IFeatureSelection;
                if (selection != null && selection.SelectionSet != null)
                    info["selected_count"] = selection.SelectionSet.Count;
            }
            catch { }

            return info;
        }

        private static JArray DescribeFields(IFeatureClass featureClass)
        {
            if (featureClass == null)
                return new JArray();
            return DescribeFields(featureClass.Fields);
        }

        private static JArray DescribeFields(IFields source)
        {
            var fields = new JArray();
            if (source == null)
                return fields;
            try
            {
                int count = Math.Min(source.FieldCount, 200);
                for (int i = 0; i < count; i++)
                {
                    IField field = source.get_Field(i);
                    if (field == null)
                        continue;
                    var item = new JObject();
                    item["name"] = field.Name;
                    try { item["type"] = field.Type.ToString(); } catch { }
                    try { item["length"] = field.Length; } catch { }
                    try { item["alias"] = field.AliasName; } catch { }
                    fields.Add(item);
                }
            }
            catch
            {
            }
            return fields;
        }

        private static JArray BuildDataPathCatalog(JArray layers)
        {
            var result = new JArray();
            var seen = new HashSet<string>();
            foreach (JToken token in layers)
            {
                JObject layer = token as JObject;
                if (layer == null)
                    continue;
                string name = (string)layer["name"];
                string path = (string)layer["full_path"];
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                string key = (name ?? string.Empty) + "|" + path;
                if (!seen.Add(key))
                    continue;
                var item = new JObject();
                item["name"] = name;
                item["path"] = path;
                item["layer_type"] = (string)layer["layer_type"];
                result.Add(item);
            }
            return result;
        }

        private static string ResolvePath(string path, string mxdPath)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;
            try
            {
                if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(mxdPath))
                {
                    string directory = Path.GetDirectoryName(mxdPath);
                    if (!string.IsNullOrEmpty(directory))
                        path = Path.Combine(directory, path);
                }
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        private static string DatasetPath(IDataset dataset, string mxdPath)
        {
            if (dataset == null)
                return null;
            string name = null;
            string workspace = null;
            try { name = dataset.Name; } catch { }
            try
            {
                if (dataset.Workspace != null)
                    workspace = dataset.Workspace.PathName;
            }
            catch { }
            if (string.IsNullOrEmpty(workspace))
                return ResolvePath(name, mxdPath);
            if (string.IsNullOrEmpty(name))
                return ResolvePath(workspace, mxdPath);
            return ResolvePath(Path.Combine(workspace, name), mxdPath);
        }

        private static string CombineDatasetPath(IDatasetName datasetName)
        {
            try
            {
                if (datasetName == null)
                    return null;
                string name = datasetName.Name;
                IWorkspaceName workspaceName = datasetName.WorkspaceName;
                string workspace = workspaceName == null ? null : workspaceName.PathName;
                if (string.IsNullOrEmpty(workspace))
                    return name;
                return Path.Combine(workspace, name);
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeDataSourcePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;
            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                    return path;
                if (File.Exists(path + ".shp"))
                    return path + ".shp";
            }
            catch
            {
            }
            return path;
        }

        private static string SafeLayerName(ILayer layer)
        {
            try { return layer == null ? "(null)" : (layer.Name ?? "(未命名)"); }
            catch { return "(未命名)"; }
        }

        private static string SafeMapName(IMap map)
        {
            try { return map == null ? string.Empty : (map.Name ?? string.Empty); }
            catch { return string.Empty; }
        }

        private static string SafeMapUnits(IMap map)
        {
            try { return map == null ? string.Empty : map.MapUnits.ToString(); }
            catch { return string.Empty; }
        }

        private static int SafeLayerCount(IMap map)
        {
            try { return map == null ? 0 : map.LayerCount; }
            catch { return 0; }
        }

        private static JToken SafeSpatialReference(ISpatialReference spatialReference)
        {
            if (spatialReference == null)
                return JValue.CreateNull();
            try { return new JValue(spatialReference.Name ?? string.Empty); }
            catch { return JValue.CreateNull(); }
        }

        private static JToken ExtentToken(IEnvelope extent)
        {
            if (extent == null)
                return JValue.CreateNull();
            try
            {
                var value = new JObject();
                value["xmin"] = extent.XMin;
                value["ymin"] = extent.YMin;
                value["xmax"] = extent.XMax;
                value["ymax"] = extent.YMax;
                return value;
            }
            catch
            {
                return JValue.CreateNull();
            }
        }

        private static JToken SafeScale(IMxDocument doc)
        {
            try
            {
                IActiveView view = doc.ActiveView;
                if (view == null || view.ScreenDisplay == null || view.ScreenDisplay.DisplayTransformation == null)
                    return JValue.CreateNull();
                return new JValue(view.ScreenDisplay.DisplayTransformation.ScaleRatio);
            }
            catch { return JValue.CreateNull(); }
        }

        private static JToken SafeExtent(IMxDocument doc)
        {
            try
            {
                IActiveView view = doc.ActiveView;
                IEnvelope extent = view == null ? null : view.Extent;
                if (extent == null)
                    return JValue.CreateNull();
                var value = new JObject();
                value["xmin"] = extent.XMin;
                value["ymin"] = extent.YMin;
                value["xmax"] = extent.XMax;
                value["ymax"] = extent.YMax;
                try { value["spatial_reference"] = SafeSpatialReference(extent.SpatialReference); }
                catch { }
                return value;
            }
            catch { return JValue.CreateNull(); }
        }

        public static string CreateOutputDir()
        {
            AppPaths.Ensure();
            Directory.CreateDirectory(AppPaths.Outputs);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(AppPaths.Outputs, stamp);
            if (Directory.Exists(path))
                path = Path.Combine(AppPaths.Outputs, stamp + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
