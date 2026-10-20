# -*- coding: utf-8 -*-
"""ArcAI Assistant Python 2.7 runner.

This script is executed by the standalone ArcGIS Python 2.7 interpreter, never
inside ArcMap. It reads a job JSON, executes the generated arcpy code with a
small helper API, and writes a result JSON for the .NET add-in.

Contract:
    python runner.py job.json result.json
    job    = {"code": unicode, "context": object, "mxd": path|null,
              "out_dir": path, "manifest": path}
    result = {"ok": bool, "stdout": unicode, "stderr": unicode,
              "error": unicode|null, "traceback": unicode|null,
              "add_layers": [{"path": path, "name": name}]}

Helpers exposed to generated code:
    arcai_context   dict copied from the live ArcMap session
    arcai_paths     dict of layer name -> resolved data source path
    arcai_path(name) return the resolved path for a layer name
    arcai_mxd       path of a temporary copy of the current .mxd (or None)
    arcai_out_dir   writable output directory
    arcai_add_layer(path, name=None)
    arcai_add_layers(paths)
    arcai_log(message)
"""
import io
import json
import os
import re
import sys
import time
import traceback

try:
    text_type = unicode
    binary_type = str
except NameError:
    text_type = str
    binary_type = bytes

_CODING_RE = re.compile(r'^[ \t]*#.*coding[:=]')


def _u(value):
    """Best-effort conversion to unicode (Python 2.7) / str (Python 3)."""
    if value is None:
        return u""
    if isinstance(value, text_type):
        return value
    if isinstance(value, binary_type):
        for encoding in ("utf-8", "gbk", "latin-1"):
            try:
                return value.decode(encoding)
            except Exception:
                pass
        return value.decode("latin-1", "replace")
    return text_type(value)


class _Buffer(object):
    """Unicode-safe replacement for sys.stdout/sys.stderr."""

    def __init__(self):
        self._parts = []
        self.softspace = 0

    def write(self, data):
        if data is None:
            return
        self._parts.append(_u(data))

    def writelines(self, lines):
        for line in lines:
            self.write(line)

    def flush(self):
        pass

    def getvalue(self):
        return u"".join(self._parts)


def _read_json(path):
    with io.open(path, "r", encoding="utf-8") as handle:
        return json.load(handle)


def _write_json(path, value):
    text = json.dumps(value, ensure_ascii=False, indent=2)
    if not isinstance(text, text_type):
        text = text.decode("utf-8")
    with io.open(path, "w", encoding="utf-8") as handle:
        handle.write(text)


def _extract_code(code):
    text = _u(code)
    match = re.search(r"```(?:python|py)?\s*\n(.*?)```", text,
                      re.IGNORECASE | re.DOTALL)
    if match:
        text = match.group(1)
    text = re.sub(r"^\s*python\s*\n", "", text, count=1, flags=re.IGNORECASE)
    return text


def _clean_source(code):
    """Remove encoding declarations before compiling unicode source.

    Python 2.7 rejects a coding declaration inside a unicode string. The JSON
    contract already gives us decoded text, so the declaration is redundant; we
    replace it with a comment to keep line numbers stable.
    """
    text = _extract_code(code)
    lines = text.splitlines(True)
    for index in range(min(2, len(lines))):
        if _CODING_RE.match(lines[index]):
            lines[index] = u"\n" if lines[index].endswith("\n") else u""
    return u"".join(lines)


def _install_clip_fallback(arcpy_module):
    """Clip_management raster can fail outside ArcMap with RasterCommander.ImageServer.
    Fall back to Spatial Analyst ExtractByMask for raster inputs; vector Clip is untouched.
    """
    original = getattr(arcpy_module, "Clip_management", None)
    if original is None:
        return

    def clip_with_fallback(*args, **kwargs):
        try:
            return original(*args, **kwargs)
        except Exception as exc:
            message = _u(exc)
            if "ImageServer" not in message and u"影像服务器" not in message:
                raise
            in_raster = kwargs.get("in_raster")
            out_raster = kwargs.get("out_raster")
            template = kwargs.get("in_template_dataset")
            if in_raster is None and len(args) > 0:
                in_raster = args[0]
            if out_raster is None and len(args) > 2:
                out_raster = args[2]
            if template is None and len(args) > 3:
                template = args[3]
            if not in_raster or not out_raster or not template:
                raise
            try:
                desc = arcpy_module.Describe(in_raster)
                if "raster" not in _u(getattr(desc, "dataType", "")).lower():
                    raise
                from arcpy.sa import ExtractByMask
                arcpy_module.CheckOutExtension("Spatial")
                ExtractByMask(in_raster, template).save(out_raster)
                try:
                    arcpy_module.AddMessage(
                        u"[ArcAI] Clip_management 兼容回退：已改用 ExtractByMask")
                except Exception:
                    pass
                return
            except Exception:
                raise exc

    arcpy_module.Clip_management = clip_with_fallback


def main():
    if len(sys.argv) < 3:
        sys.stderr.write("usage: runner.py job.json result.json\n")
        return 1

    job_path = sys.argv[1]
    result_path = sys.argv[2]
    started = time.time()
    result = {
        "ok": False,
        "stdout": u"",
        "stderr": u"",
        "error": None,
        "traceback": None,
        "add_layers": [],
        "map_actions": [],
        "images": [],
        "python": sys.version,
        "elapsed_seconds": 0,
    }
    stdout_buffer = _Buffer()
    stderr_buffer = _Buffer()
    old_stdout = sys.stdout
    old_stderr = sys.stderr
    job = {}
    manifest = []
    images = []
    map_actions = []

    def arcai_add_layer(path, name=None):
        if not path:
            raise ValueError(u"arcai_add_layer 需要非空路径。")
        full = os.path.abspath(_u(path))
        item = {
            "path": full,
            "name": _u(name) if name else os.path.basename(full),
        }
        manifest.append(item)
        sys.stdout.write(u"[ArcAI] queued layer: %s\n" % full)
        return full

    def arcai_add_layers(paths):
        for path in (paths or []):
            arcai_add_layer(path)

    def arcai_map_action(action, **kwargs):
        item = {"action": _u(action), "args": kwargs}
        map_actions.append(item)
        sys.stdout.write(u"[ArcAI] queued map action: %s\n" % _u(action))
        return item

    def arcai_remove_layer(name):
        return arcai_map_action("remove_layer", name=_u(name))

    def arcai_set_layer_visible(name, visible=True):
        return arcai_map_action("set_layer_visible", name=_u(name), visible=bool(visible))

    def arcai_zoom_to_layer(name):
        return arcai_map_action("zoom_to_layer", name=_u(name))

    def arcai_zoom_to_extent(xmin, ymin, xmax, ymax, wkid=None):
        return arcai_map_action("zoom_to_extent", xmin=float(xmin), ymin=float(ymin),
                                xmax=float(xmax), ymax=float(ymax), wkid=wkid)

    def arcai_select_by_attribute(layer, where):
        return arcai_map_action("select_by_attribute", layer=_u(layer), where=_u(where))

    def arcai_clear_selection(layer=None):
        return arcai_map_action("clear_selection", layer=_u(layer) if layer else None)

    def arcai_refresh():
        return arcai_map_action("refresh")

    def arcai_log(message):
        sys.stdout.write(_u(message) + u"\n")

    def arcai_export_map(path=None, format="PNG", dpi=150):
        """Export the current MXD snapshot to an image/PDF and return its path."""
        if not mxd:
            raise ValueError(u"当前文档没有可用的 MXD 快照；请先保存地图文档再导出。")
        fmt = _u(format or "PNG").upper()
        ext_map = {"PNG": ".png", "JPG": ".jpg", "JPEG": ".jpg",
                   "PDF": ".pdf", "TIFF": ".tif", "TIF": ".tif"}
        extension = ext_map.get(fmt, ".png")
        if not path:
            path = os.path.join(out_dir, "arcai_map_%s%s"
                                % (time.strftime("%Y%m%d_%H%M%S"), extension))
        path = os.path.abspath(_u(path))
        mxd_document = arcpy.mapping.MapDocument(mxd)
        try:
            frames = arcpy.mapping.ListDataFrames(mxd_document)
            data_frame = frames[0] if frames else None
            if fmt == "PDF":
                arcpy.mapping.ExportToPDF(mxd_document, path, resolution=dpi)
            elif fmt in ("JPG", "JPEG"):
                if data_frame is not None:
                    arcpy.mapping.ExportToJPEG(mxd_document, path, data_frame, resolution=dpi)
                else:
                    arcpy.mapping.ExportToJPEG(mxd_document, path, resolution=dpi)
            elif fmt in ("TIFF", "TIF"):
                if data_frame is not None:
                    arcpy.mapping.ExportToTIFF(mxd_document, path, data_frame, resolution=dpi)
                else:
                    arcpy.mapping.ExportToTIFF(mxd_document, path, resolution=dpi)
            else:
                if data_frame is not None:
                    arcpy.mapping.ExportToPNG(mxd_document, path, data_frame, resolution=dpi)
                else:
                    arcpy.mapping.ExportToPNG(mxd_document, path, resolution=dpi)
        finally:
            try:
                del mxd_document
            except Exception:
                pass
        images.append(path)
        sys.stdout.write(u"[ArcAI] exported map: %s\n" % path)
        return path

    try:
        job = _read_json(job_path)
        out_dir = _u(job.get("out_dir") or os.getcwd())
        if not os.path.isdir(out_dir):
            os.makedirs(out_dir)

        sys.stdout = stdout_buffer
        sys.stderr = stderr_buffer

        import arcpy
        _install_clip_fallback(arcpy)

        try:
            arcpy.env.overwriteOutput = False
            arcpy.env.workspace = out_dir
            arcpy.env.scratchWorkspace = out_dir
        except Exception:
            pass

        context = job.get("context")
        if not isinstance(context, dict):
            context = {}
        mxd = job.get("mxd")

        def _collect_layer_paths(node, target):
            if not isinstance(node, dict):
                return
            name = node.get("name")
            path = node.get("full_path")
            if name and path:
                target.setdefault(_u(name), _u(path))
            for item in (node.get("data_paths") or []):
                if isinstance(item, dict):
                    item_name = item.get("name")
                    item_path = item.get("path")
                    if item_name and item_path:
                        target.setdefault(_u(item_name), _u(item_path))
            for child in (node.get("layers") or []):
                _collect_layer_paths(child, target)
            for child in (node.get("maps") or []):
                _collect_layer_paths(child, target)

        arcai_paths = {}
        _collect_layer_paths(context, arcai_paths)

        def arcai_path(name):
            key = _u(name)
            if key in arcai_paths:
                return arcai_paths[key]
            lowered = key.lower()
            for candidate, path in arcai_paths.items():
                if candidate.lower() == lowered:
                    return path
            raise KeyError(u"未找到图层路径: %s。可用图层: %s"
                           % (key, u", ".join(sorted(arcai_paths.keys()))))

        namespace = {
            "__name__": "arcai_generated",
            "arcpy": arcpy,
            "os": os,
            "sys": sys,
            "arcai_context": context,
            "arcai_paths": arcai_paths,
            "arcai_path": arcai_path,
            "arcai_mxd": mxd,
            "arcai_out_dir": out_dir,
            "arcai_add_layer": arcai_add_layer,
            "arcai_add_layers": arcai_add_layers,
            "arcai_map_action": arcai_map_action,
            "arcai_remove_layer": arcai_remove_layer,
            "arcai_set_layer_visible": arcai_set_layer_visible,
            "arcai_zoom_to_layer": arcai_zoom_to_layer,
            "arcai_zoom_to_extent": arcai_zoom_to_extent,
            "arcai_select_by_attribute": arcai_select_by_attribute,
            "arcai_clear_selection": arcai_clear_selection,
            "arcai_refresh": arcai_refresh,
            "arcai_export_map": arcai_export_map,
            "arcai_log": arcai_log,
        }

        source = _clean_source(job.get("code", u""))
        code_object = compile(source, "<arcai_generated>", "exec")
        exec(code_object, namespace)

        try:
            messages = arcpy.GetMessages()
            if messages:
                sys.stdout.write(u"\n--- arcpy messages ---\n")
                sys.stdout.write(_u(messages))
                sys.stdout.write(u"\n")
        except Exception:
            pass

        result["ok"] = True
    except BaseException as exc:
        result["ok"] = False
        message = _u(exc)
        if not message:
            message = exc.__class__.__name__
        result["error"] = message
        result["traceback"] = _u(traceback.format_exc())
    finally:
        sys.stdout = old_stdout
        sys.stderr = old_stderr
        result["stdout"] = stdout_buffer.getvalue()
        result["stderr"] = stderr_buffer.getvalue()
        result["add_layers"] = manifest
        result["map_actions"] = map_actions
        result["images"] = images
        result["elapsed_seconds"] = round(time.time() - started, 3)

        manifest_path = job.get("manifest") if isinstance(job, dict) else None
        if manifest_path:
            try:
                _write_json(manifest_path, manifest)
            except Exception:
                pass

        try:
            _write_json(result_path, result)
        except Exception:
            sys.stderr.write("ArcAI runner could not write %s\n" % result_path)
            sys.stderr.write(traceback.format_exc())
            return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
