"""Packages the Endpoint C/C++ drivers and stages them for release:
cpp/publish/<tag>/endpoint-drvcpp-<tag>.zip, plus a one-line cpp/publish/latest.txt
pointing at the current tag.

    python publish_drvcpp.py            # tag derived from first csproj Version
    python publish_drvcpp.py v052       # explicit override

The zip contains the whole 'endpoint' folder of driver headers:

    endpoint/drivers/FocalTech/Ft5xx6.h
    endpoint/drivers/HiLetgo/Ili9341.h
    endpoint/drivers/SinoWealth/Sh110x.h

so extracting it over a C/C++ project's existing endpoint/ folder lands the
.h files at endpoint/drivers/<Vendor>/<Chip>.h, next to the libendpoint
headers already shipped by endpoint-libcpp.
"""

import pathlib
import re
import sys
import zipfile

here = pathlib.Path(__file__).resolve().parent  # endpoint-drivers/cpp/

if len(sys.argv) > 1:
    tag = sys.argv[1]
else:
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

drivers_root = here / "GHIElectronics.Endpoint.Drivers"
if not drivers_root.exists():
    sys.exit(f"{drivers_root} is missing.")

headers = sorted(drivers_root.glob("*/GHIElectronics.Endpoint.Drivers.*/*.h"))
if not headers:
    sys.exit(f"No .h files under {drivers_root}/*/GHIElectronics.Endpoint.Drivers.*/")

target_dir = here / "publish" / tag
target_dir.mkdir(parents=True, exist_ok=True)
zip_path = target_dir / f"endpoint-drvcpp-{tag}.zip"
if zip_path.exists():
    zip_path.unlink()

count = 0
with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
    for h in headers:
        # <...>/GHIElectronics.Endpoint.Drivers/<Vendor>/<Pkg>/<Chip>.h
        # -> endpoint/drivers/<Vendor>/<Chip>.h
        vendor = h.parent.parent.name     # FocalTech / HiLetgo / SinoWealth
        arcname = f"endpoint/drivers/{vendor}/{h.name}"
        z.write(h, arcname)
        count += 1

(here / "publish" / "latest.txt").write_text(tag + "\n", encoding="utf-8")

size_kb = zip_path.stat().st_size // 1024
print(f"Published Endpoint C/C++ drivers {tag}: {zip_path.name} ({count} headers, {size_kb} KB)")
print(f"Updated cpp/publish/latest.txt -> {tag}")
