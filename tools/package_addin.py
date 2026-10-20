# -*- coding: utf-8 -*-
"""Create a flat .esriaddin ZIP with forward-slash entry names."""
import os
import sys
import zipfile

stage, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as archive:
    count = 0
    for root, dirs, files in os.walk(stage):
        for name in files:
            full = os.path.join(root, name)
            arc = os.path.relpath(full, stage).replace("\\", "/")
            archive.write(full, arc)
            count += 1
print("packaged %d entries -> %s" % (count, out))
