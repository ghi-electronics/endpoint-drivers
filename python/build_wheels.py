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

# Pi Zero 2 W native board-support .so: Python GPIO on Pi goes through
# libendpoint, which needs its C++ BoardSupport::Current() to be set to
# Zero2WBoardSupport. The .so's constructor does that at ctypes.CDLL time
# (see endpoint-drivers/cpp/.../RaspberryPi/.../native/libendpoint-zero2w.cpp).
# Build the .so first with:
#     cd ../cpp/GHIElectronics.Endpoint.Drivers/RaspberryPi/...RaspberryPi/native
#     cmake --preset endpoint-zero2w-arm64 && cmake --build --preset endpoint-zero2w-arm64
#     (same for -arm)
# then re-run this script. Missing .so is a warning, not an error: a dev who
# only touched Python can still build + test the Python wheel; Pi GPIO just
# will not work until the .so is present.
pi_cpp_build = (here.parent / "cpp" / "GHIElectronics.Endpoint.Drivers" / "RaspberryPi"
                / "GHIElectronics.Endpoint.Drivers.RaspberryPi" / "native" / "build")
pi_py_dir = (here / "GHIElectronics.Endpoint.Drivers" / "RaspberryPi"
             / "GHIElectronics.Endpoint.Drivers.RaspberryPi"
             / "GHIElectronics" / "Endpoint" / "Drivers" / "RaspberryPi")
pi_arch_sources = {
    "arm":   pi_cpp_build / "linux-arm"   / "libendpoint-zero2w-arm.so",
    "arm64": pi_cpp_build / "linux-arm64" / "libendpoint-zero2w-arm64.so",
}
# Scrub any stale copies so a one-arch rebuild never ships both a fresh
# arm64 and a day-old arm.
for stale in pi_py_dir.glob("libendpoint-zero2w-*.so"):
    stale.unlink()
pi_copied = []
for arch, source in pi_arch_sources.items():
    if source.exists():
        shutil.copy2(source, pi_py_dir / source.name)
        pi_copied.append(arch)
    else:
        print(f"note: skipping Pi {arch} native .so (no {source})")
if pi_copied:
    print(f"Bundled libendpoint-zero2w for: {', '.join(pi_copied)}")
else:
    print("warning: no Pi native .so bundled. Python GPIO on Pi will throw "
          "'GPIO is not supported' until you build "
          "../cpp/.../RaspberryPi/.../native via its cmake presets and re-run.")

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
