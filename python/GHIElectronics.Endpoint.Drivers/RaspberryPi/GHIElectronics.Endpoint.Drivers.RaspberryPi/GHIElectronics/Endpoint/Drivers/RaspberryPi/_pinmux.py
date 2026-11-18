"""Fork+exec wrapper around /usr/sbin/endpoint-pinmux, the C helper that
writes SoC pinmux registers via /dev/mem. One binary per chip ships in
the Buildroot image; this driver uses the bcm2837 build on Pi Zero 2 W.
Keeps chip-specific register knowledge in one place (the C binary) so
.NET, Python, and C/C++ don't each re-implement it. Same shape as
GHIElectronics.Endpoint.Core._board_support.Pinmux.

`function` is a chip-specific packed uint32 - see
Endpoint-Firmware/pinmux/src/pinmux.h for each chip's layout. For
BCM2837 (Pi): bits 0-2 = GPFSEL function (0=input, 1=output,
2..7 = alt5/alt4/alt0/alt1/alt2/alt3).

Peripheral verb classes (Zero2W.I2c / Spi / Pwm) use Get before their
first Set to snapshot the user's pre-Initialize state, then Set again
in UnInitialize to put it back. The in-process state lives in the
peripheral class; nothing persists across processes.
"""

import subprocess

_PINMUX_PATH = "/usr/sbin/endpoint-pinmux"
_MODPROBE_PATH = "/sbin/modprobe"


class Pinmux:
    """Internal fork+exec wrapper. Not part of the public API."""

    @staticmethod
    def Set(pin, function):
        """Set the pin's function. Throws if the binary exits non-zero."""
        Pinmux._run(_PINMUX_PATH, "set", str(int(pin)), f"0x{int(function):X}")

    @staticmethod
    def Get(pin):
        """Read the pin's current function (packed uint32, chip-specific layout)."""
        out = Pinmux._run_capture(_PINMUX_PATH, "get", str(int(pin)))
        # Output: "pin=<n> function=0x<hex> level=<0|1>\n"
        for token in out.split():
            if token.startswith("function="):
                return int(token[len("function="):], 0)  # auto-detects 0x prefix
        raise RuntimeError(
            f"endpoint-pinmux get {pin}: unexpected output '{out.rstrip()}'"
        )

    @staticmethod
    def Modprobe(module):
        """Load a kernel module via /sbin/modprobe (idempotent)."""
        Pinmux._run(_MODPROBE_PATH, module)

    @staticmethod
    def _run(path, *args):
        try:
            subprocess.run(
                [path, *args],
                check=True,
                capture_output=True,
                text=True,
            )
        except subprocess.CalledProcessError as e:
            raise RuntimeError(
                f"{path} {' '.join(args)} failed "
                f"(exit {e.returncode}): {e.stderr.rstrip()}"
            ) from None

    @staticmethod
    def _run_capture(path, *args):
        try:
            result = subprocess.run(
                [path, *args],
                check=True,
                capture_output=True,
                text=True,
            )
            return result.stdout
        except subprocess.CalledProcessError as e:
            raise RuntimeError(
                f"{path} {' '.join(args)} failed "
                f"(exit {e.returncode}): {e.stderr.rstrip()}"
            ) from None
