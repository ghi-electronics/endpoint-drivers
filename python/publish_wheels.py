"""Copies freshly-built driver wheels from dist/ into publish/<tag>/ and writes
an index.html so pip can resolve them via --find-links at that URL.

    python publish_wheels.py            # tag derived from first csproj Version
    python publish_wheels.py v052       # explicit override

The folder name uses the ci_build.bat RELEASE_TAG convention (v052, v053, ...)
so one source of truth drives both. Reads the first driver csproj's <Version>
to derive the tag when no arg is passed.
"""

import pathlib
import re
import shutil
import sys

here = pathlib.Path(__file__).resolve().parent  # endpoint-drivers/python/
dist = here / "dist"

if len(sys.argv) > 1:
    tag = sys.argv[1]
else:
    # Derive tag from the first driver csproj Version. Version "0.5.2-preview" -> "v052".
    csproj = here.parent / "dotnet" / "GHIElectronics.Endpoint.Drivers" / "FocalTech" \
             / "GHIElectronics.Endpoint.Drivers.FocalTech.FT5xx6" \
             / "GHIElectronics.Endpoint.Drivers.FocalTech.FT5xx6.csproj"
    match = re.search(r"<Version>([^<]+)</Version>", csproj.read_text(encoding="utf-8"))
    if not match:
        sys.exit(f"No <Version> in {csproj}")
    m = re.match(r"(\d+)\.(\d+)\.(\d+)", match.group(1).strip())
    if not m:
        sys.exit(f"Could not parse MAJOR.MINOR.PATCH from <Version>{match.group(1)}</Version>")
    tag = f"v{m.group(1)}{m.group(2)}{m.group(3)}"

wheels = sorted(dist.glob("*.whl"))
if not wheels:
    sys.exit(f"No wheels in {dist}. Run build_wheels.py first.")

target = here / "publish" / tag
if target.exists():
    shutil.rmtree(target)
target.mkdir(parents=True)

for wheel in wheels:
    shutil.copy2(wheel, target / wheel.name)

# pip's --find-links accepts a URL that returns an HTML page with <a href=...>
# links to each wheel. Keep the links relative so the same index.html works
# from any URL (raw.githubusercontent.com, GitHub Pages, a local webserver, ...)
links = "\n".join(f'    <a href="{w.name}">{w.name}</a><br>' for w in wheels)
index = f"""<!DOCTYPE html>
<html><head><title>GHIElectronics.Endpoint.Drivers {tag} wheels</title></head>
<body>
<h1>GHIElectronics.Endpoint.Drivers Python wheels, {tag}</h1>
<p>Served as a temporary pip --find-links source while PyPI publishing
catches up. Once every driver is on PyPI, this folder goes away.</p>
{links}
</body></html>
"""
(target / "index.html").write_text(index, encoding="utf-8")

# publish/latest.txt names the current tag. Tools that want the newest set of
# wheels fetch this file first.
(here / "publish" / "latest.txt").write_text(tag + "\n", encoding="utf-8")

print(f"Published {len(wheels)} driver wheels + index.html to {target}")
print(f"Updated publish/latest.txt -> {tag}")
for wheel in wheels:
    print(f"  {wheel.name}")
