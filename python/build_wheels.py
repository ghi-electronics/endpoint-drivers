"""Builds the Endpoint driver Python packages into dist/ (one wheel each).

    python build_wheels.py

Driver pyprojects live two levels down under
GHIElectronics.Endpoint.Drivers/<Vendor>/<Package>/, mirroring the .NET
solution layout. Each package ships a namespaced module tree
(GHIElectronics/Endpoint/Drivers/<Vendor>/<Chip>/__init__.py) that imports
match the .NET namespace character-for-character.
"""

import pathlib
import shutil
import subprocess
import sys

here = pathlib.Path(__file__).resolve().parent
dist = here / "dist"

shutil.rmtree(dist, ignore_errors=True)

# A driver = a directory GHIElectronics.Endpoint.Drivers.<Vendor>.<Chip> with
# a pyproject.toml. Globbed recursively because the vendor subfolder adds a
# middle layer the libraries tree does not have.
packages = sorted(
    p for p in here.glob("GHIElectronics.Endpoint.Drivers/*/GHIElectronics.Endpoint.Drivers.*")
    if p.is_dir() and (p / "pyproject.toml").is_file()
)
if not packages:
    sys.exit("No GHIElectronics.Endpoint.Drivers.* packages with pyproject.toml found.")

for package in packages:
    print("Building", package.name)
    subprocess.run([sys.executable, "-m", "pip", "wheel", "--no-deps", "--quiet", "--wheel-dir", str(dist), str(package)],
                   check=True)
    for leftover in ("build", f"{package.name}.egg-info"):
        shutil.rmtree(package / leftover, ignore_errors=True)

print()
for wheel in sorted(dist.glob("*.whl")):
    print(f"{wheel.name}  ({wheel.stat().st_size // 1024} KB)")
