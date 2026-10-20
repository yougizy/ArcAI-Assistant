using System;
using System.Collections.Generic;
using System.IO;
using ESRI.ArcGIS.ArcMapUI;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.DataSourcesFile;
using ESRI.ArcGIS.DataSourcesGDB;
using ESRI.ArcGIS.DataSourcesRaster;
using ESRI.ArcGIS.Framework;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.Geometry;
using Newtonsoft.Json.Linq;
using Path = System.IO.Path;

namespace ArcAI.AddIn
{
    internal sealed class LayerResult
    {
        public string Path;
        public string Name;

        public LayerResult(string path, string name)
        {
            Path = path;
            Name = name;
        }
    }

    /// <summary>ArcObjects operations used by the run pipeline (STA/UI thread).</summary>
    internal static class ArcMapActions
    {
        private static readonly string[] RasterExtensions =
        {
            ".tif", ".tiff", ".img", ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".grd", ".dat"
        };

        /// <summary>
        /// Saves a temporary copy of the live .mxd. SaveAsDocument(path, true) is a
        /// copy operation: the active document is not changed.
        /// </summary>
        public static string CreateSnapshot(IApplication app)
        {
            AppPaths.Ensure();
            string mxdPath = ArcSession.MxdPath(app);
            string preferredDir = null;
            if (!string.IsNullOrEmpty(mxdPath))
            {
                try { preferredDir = Path.GetDirectoryName(mxdPath); }
                catch { }
            }

            string first = TrySnapshot(app, preferredDir);
            if (first != null)
                return first;
            return TrySnapshot(app, AppPaths.Snapshots);
        }

        private static string TrySnapshot(IApplication app, string directory)
        {
            try
            {
                if (string.IsNullOrEmpty(directory))
                    return null;
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "~arcai_" + Guid.NewGuid().ToString("N") + ".mxd");
                app.SaveAsDocument(path, true);
                if (!File.Exists(path))
                    return null;
                Log.Write("Created MXD snapshot: " + path);
                return path;
            }
            catch (Exception ex)
            {
                Log.Error("Could not create MXD snapshot in " + directory, ex);
                return null;
            }
        }

        public static void DeleteSnapshot(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Error("Could not delete MXD snapshot " + path, ex);
            }
        }

        public static int AddLayers(IApplication app, IEnumerable<LayerResult> layers,
            List<string> errors)
        {
            if (layers == null)
                return 0;
            IMxDocument doc = ArcSession.Doc(app);
            IMap map = doc.FocusMap;
            int added = 0;
            foreach (LayerResult request in layers)
            {
                try
                {
                    ILayer layer = CreateLayer(request.Path);
                    if (layer == null)
                        throw new InvalidOperationException("不支持的图层来源。");
                    if (!string.IsNullOrWhiteSpace(request.Name))
                        layer.Name = request.Name;
                    map.AddLayer(layer);
                    added++;
                }
                catch (Exception ex)
                {
                    string message = request.Path + "：" + ex.Message;
                    errors.Add(message);
                    Log.Error("Could not add layer " + request.Path, ex);
                }
            }

            if (added > 0)
            {
                try { doc.ActiveView.Refresh(); } catch { }
                try { doc.UpdateContents(); } catch { }
            }
            return added;
        }

        /// <summary>
        /// Executes the map actions queued by generated Python code. Runs on the
        /// ArcMap UI/STA thread after the Python process has finished.
        /// </summary>
        public static int ExecuteMapActions(IApplication app, IEnumerable<MapAction> actions,
            List<string> errors)
        {
            if (actions == null)
                return 0;
            IMxDocument doc = ArcSession.Doc(app);
            IMap map = doc.FocusMap;
            IActiveView view = doc.ActiveView;
            int applied = 0;

            foreach (MapAction item in actions)
            {
                try
                {
                    string action = (item.Action ?? string.Empty).Trim().ToLowerInvariant();
                    JObject args = item.Args ?? new JObject();
                    switch (action)
                    {
                        case "remove_layer":
                        {
                            ILayer layer = RequireLayer(map, args);
                            map.DeleteLayer(layer);
                            break;
                        }
                        case "set_layer_visible":
                        {
                            ILayer layer = RequireLayer(map, args);
                            layer.Visible = BoolValue(args, "visible", true);
                            break;
                        }
                        case "zoom_to_layer":
                        {
                            ILayer layer = RequireLayer(map, args);
                            IGeoDataset dataset = LayerGeoDataset(layer);
                            if (dataset == null || dataset.Extent == null)
                                throw new InvalidOperationException("该图层没有可用于缩放的空间范围。");
                            view.Extent = dataset.Extent;
                            break;
                        }
                        case "zoom_to_extent":
                        {
                            double xmin = DoubleValue(args, "xmin", 0);
                            double ymin = DoubleValue(args, "ymin", 0);
                            double xmax = DoubleValue(args, "xmax", 0);
                            double ymax = DoubleValue(args, "ymax", 0);
                            if (xmax <= xmin || ymax <= ymin)
                                throw new ArgumentException("zoom_to_extent 的范围无效。");
                            IEnvelope envelope = new EnvelopeClass();
                            envelope.PutCoords(xmin, ymin, xmax, ymax);
                            int wkid = (int)DoubleValue(args, "wkid", 0);
                            if (wkid > 0)
                            {
                                try
                                {
                                    ISpatialReferenceFactory3 factory = new SpatialReferenceEnvironmentClass();
                                    envelope.SpatialReference = factory.CreateSpatialReference(wkid);
                                }
                                catch
                                {
                                }
                            }
                            if (envelope.SpatialReference == null)
                            {
                                try { envelope.SpatialReference = map.SpatialReference; }
                                catch { }
                            }
                            view.Extent = envelope;
                            break;
                        }
                        case "select_by_attribute":
                        {
                            ILayer layer = RequireLayer(map, args);
                            IFeatureSelection selection = layer as IFeatureSelection;
                            if (selection == null)
                                throw new InvalidOperationException("该图层不支持按属性选择。");
                            string where = StringValue(args, "where");
                            if (string.IsNullOrWhiteSpace(where))
                                throw new ArgumentException("select_by_attribute 缺少 where 条件。");
                            IQueryFilter filter = new QueryFilterClass();
                            filter.WhereClause = where;
                            selection.SelectFeatures(filter,
                                esriSelectionResultEnum.esriSelectionResultNew, false);
                            break;
                        }
                        case "clear_selection":
                        {
                            string name = StringValue(args, "layer");
                            if (string.IsNullOrWhiteSpace(name))
                            {
                                map.ClearSelection();
                            }
                            else
                            {
                                ILayer layer = FindLayer(map, name);
                                IFeatureSelection selection = layer as IFeatureSelection;
                                if (selection != null)
                                    selection.Clear();
                            }
                            break;
                        }
                        case "refresh":
                            break;
                        default:
                            throw new NotSupportedException("不支持的 ArcMap 操作：" + item.Action);
                    }
                    applied++;
                }
                catch (Exception ex)
                {
                    errors.Add((item.Action ?? "map_action") + "：" + ex.Message);
                    Log.Error("Map action failed: " + item.Action, ex);
                }
            }

            try { view.Refresh(); } catch { }
            try { doc.UpdateContents(); } catch { }
            return applied;
        }

        private static ILayer RequireLayer(IMap map, JObject args)
        {
            string name = StringValue(args, "name");
            if (string.IsNullOrWhiteSpace(name))
                name = StringValue(args, "layer");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("地图操作缺少图层名称。");
            ILayer layer = FindLayer(map, name);
            if (layer == null)
                throw new ArgumentException("找不到图层：" + name);
            return layer;
        }

        private static ILayer FindLayer(IMap map, string name)
        {
            if (map == null || string.IsNullOrWhiteSpace(name))
                return null;
            if (name.IndexOf('/') >= 0)
                name = name.Substring(name.LastIndexOf('/') + 1);
            for (int i = 0; i < map.LayerCount; i++)
            {
                ILayer layer = null;
                try { layer = map.get_Layer(i); }
                catch { continue; }
                ILayer found = FindLayer(layer, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static ILayer FindLayer(ILayer layer, string name)
        {
            if (layer == null)
                return null;
            try
            {
                if (string.Equals(layer.Name, name, StringComparison.OrdinalIgnoreCase))
                    return layer;
            }
            catch
            {
            }
            ICompositeLayer composite = layer as ICompositeLayer;
            if (composite == null)
                return null;
            int count;
            try { count = composite.Count; }
            catch { return null; }
            for (int i = 0; i < count; i++)
            {
                ILayer child = null;
                try { child = composite.get_Layer(i); }
                catch { continue; }
                ILayer found = FindLayer(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static IGeoDataset LayerGeoDataset(ILayer layer)
        {
            IFeatureLayer featureLayer = layer as IFeatureLayer;
            if (featureLayer != null)
                return featureLayer.FeatureClass as IGeoDataset;
            IRasterLayer rasterLayer = layer as IRasterLayer;
            if (rasterLayer != null)
            {
                IRasterDataset dataset = rasterLayer.Raster as IRasterDataset;
                return dataset as IGeoDataset;
            }
            return null;
        }

        private static string StringValue(JObject args, string key)
        {
            JToken token = args[key];
            if (token == null || token.Type == JTokenType.Null)
                return null;
            return token.ToString();
        }

        private static bool BoolValue(JObject args, string key, bool fallback)
        {
            JToken token = args[key];
            if (token == null || token.Type == JTokenType.Null)
                return fallback;
            try { return token.Value<bool>(); }
            catch { return fallback; }
        }

        private static double DoubleValue(JObject args, string key, double fallback)
        {
            JToken token = args[key];
            if (token == null || token.Type == JTokenType.Null)
                return fallback;
            try { return token.Value<double>(); }
            catch { return fallback; }
        }

        private static ILayer CreateLayer(string source)
        {            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("图层路径为空。");
            string full = Path.GetFullPath(source);
            string extension = (Path.GetExtension(full) ?? string.Empty).ToLowerInvariant();

            if (extension == ".lyr")
            {
                if (!File.Exists(full))
                    throw new FileNotFoundException("找不到图层文件。", full);
                ILayerFile layerFile = new LayerFileClass();
                layerFile.Open(full);
                return layerFile.Layer;
            }

            if (extension == ".shp")
            {
                if (!File.Exists(full))
                    throw new FileNotFoundException("找不到 shapefile。", full);
                IWorkspaceFactory factory = new ShapefileWorkspaceFactoryClass();
                IFeatureWorkspace workspace = (IFeatureWorkspace)factory.OpenFromFile(
                    Path.GetDirectoryName(full), 0);
                IFeatureClass featureClass = workspace.OpenFeatureClass(Path.GetFileNameWithoutExtension(full));
                return FeatureLayer(featureClass);
            }

            string gdb;
            string datasetName;
            if (SplitGdbPath(full, out gdb, out datasetName))
            {
                if (!Directory.Exists(gdb))
                    throw new DirectoryNotFoundException("找不到文件地理数据库：" + gdb);
                IWorkspaceFactory factory = new FileGDBWorkspaceFactoryClass();
                IFeatureWorkspace workspace = (IFeatureWorkspace)factory.OpenFromFile(gdb, 0);
                try
                {
                    return FeatureLayer(workspace.OpenFeatureClass(datasetName));
                }
                catch
                {
                    IRasterWorkspaceEx rasterWorkspace = workspace as IRasterWorkspaceEx;
                    if (rasterWorkspace == null)
                        throw;
                    IRasterDataset dataset = rasterWorkspace.OpenRasterDataset(datasetName);
                    IRasterLayer rasterLayer = new RasterLayerClass();
                    rasterLayer.CreateFromDataset(dataset);
                    return rasterLayer;
                }
            }

            if (Array.IndexOf(RasterExtensions, extension) >= 0)
            {
                if (!File.Exists(full))
                    throw new FileNotFoundException("找不到栅格文件。", full);
                IRasterLayer rasterLayer = new RasterLayerClass();
                rasterLayer.CreateFromFilePath(full);
                return rasterLayer;
            }

            throw new NotSupportedException("不支持的图层来源（支持 .shp、.lyr、文件 GDB 要素类/栅格、常见栅格文件）：" + full);
        }

        private static ILayer FeatureLayer(IFeatureClass featureClass)
        {
            IFeatureLayer layer = new FeatureLayerClass();
            layer.FeatureClass = featureClass;
            try { layer.Name = ((IDataset)featureClass).Name; }
            catch { }
            return layer;
        }

        private static bool SplitGdbPath(string path, out string gdb, out string dataset)
        {
            gdb = null;
            dataset = null;
            if (string.IsNullOrEmpty(path))
                return false;
            string lower = path.ToLowerInvariant();
            int from = 0;
            while (from <= lower.Length - 4)
            {
                int index = lower.IndexOf(".gdb", from, StringComparison.Ordinal);
                if (index < 0)
                    return false;
                int end = index + 4;
                if (end == lower.Length || lower[end] == '\\' || lower[end] == '/')
                {
                    gdb = path.Substring(0, end);
                    string rest = path.Substring(end).Trim('\\', '/');
                    if (rest.Length == 0)
                        return false;
                    int cut = rest.LastIndexOfAny(new[] { '\\', '/' });
                    dataset = cut >= 0 ? rest.Substring(cut + 1) : rest;
                    return dataset.Length > 0;
                }
                from = index + 1;
            }
            return false;
        }
    }
}
