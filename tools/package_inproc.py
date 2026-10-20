# -*- coding: utf-8 -*-
"""Package InProcAddIn into dist/ArcAI-InProc.esriaddin.

The ArcMap Add-In Manager needs forward-slash ZIP entries.  This script is
called by build.ps1 and deliberately has no third-party dependencies.
"""
import os
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "InProcAddIn")
OUT = os.path.join(ROOT, "dist", "ArcAI-InProc.esriaddin")


def main():
    if not os.path.isfile(os.path.join(SRC, "config.xml")):
        raise SystemExit("InProcAddIn/config.xml is missing")
    if os.path.exists(OUT):
        os.remove(OUT)
    if not os.path.isdir(os.path.dirname(OUT)):
        os.makedirs(os.path.dirname(OUT))
    with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as archive:
        for base, dirs, files in os.walk(SRC):
            for name in files:
                if name.endswith(".pyc"):
                    continue
                full = os.path.join(base, name)
                archive.write(full, os.path.relpath(full, SRC).replace(os.sep, "/"))
    print("packaged %s (%d bytes)" % (OUT, os.path.getsize(OUT)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
