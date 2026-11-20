"""Raspberry Pi Zero 2 W board support for Endpoint (Python).

Registers Zero2WBoardSupport with Core's BoardSupport.Current on import,
so user code uses Core's chip-neutral verbs:

    from GHIElectronics.Endpoint.Core import I2cController, SpiController, PwmController, SerialPortController
    from GHIElectronics.Endpoint.Drivers.RaspberryPi import Zero2W

    I2cController.Initialize(Zero2W.I2c.I2c1)
    PwmController.Initialize(Zero2W.Pwm.Pin.Pin12)
    SpiController.Initialize(Zero2W.Spi.Spi0, Zero2W.Spi.DefaultBufferSize)
    SerialPortController.Initialize(Zero2W.SerialPort.Uart1, False)

Identical shape to Endpoint boards; swap `EPDomino24.*` for `Zero2W.*`.
Zero2WBoardSupport only overrides I2C/SPI/PWM/SerialPort (the peripherals
the Pi actually implements). Everything else (CAN, DAC, Watchdog, ADC,
STM32 pin primitives) inherits UnknownBoardSupport's "not supported on
this board" throw from Core.
"""

# Import Core FIRST: this loads libendpoint-<arch>.so (with RTLD_GLOBAL, see
# Core/_native.py). Required BEFORE we dlopen libendpoint-zero2w-<arch>.so
# below, because that .so's __attribute__((constructor)) looks up
# endpoint_register_board_support via dlsym(RTLD_DEFAULT) and will find
# nothing if libendpoint has not been loaded globally first. On an Endpoint
# chip (EPM815/EPN256/EPN128) the libendpoint load also auto-registers that
# chip's BoardSupport, and the zero2w constructor then no-ops on the
# ChipDetect check so the Endpoint board stays active.
from GHIElectronics.Endpoint.Core import _native as _unused_core_native  # noqa: F401

from GHIElectronics.Endpoint.Core._board_support import (
    BoardSupport as _BoardSupport,
    UnknownBoardSupport as _UnknownBoardSupport,
)
from GHIElectronics.Endpoint.Core._chip_detect import Chip as _Chip, Current as _ChipCurrent

from ._pinmux import Pinmux as _Pinmux

# Load the native board-support .so so libendpoint's C++ dispatch sees the
# Pi. Python GPIO goes through libendpoint directly (unlike I2C/SPI/PWM/UART
# which still use the Python-side Zero2WBoardSupport below), so without this
# load every gpio.OpenPin would throw "GPIO is not supported on this board
# ('Unknown')".
def _load_native_board_driver():
    import ctypes, os, platform
    machine = platform.machine().lower()
    if machine in ("aarch64", "arm64"):
        name = "libendpoint-zero2w-arm64.so"
    elif machine.startswith("armv") or machine == "arm":
        name = "libendpoint-zero2w-arm.so"
    else:
        # Dev PC (Linux x86_64, Windows, Mac); no board native lib to load.
        # Python-side Zero2WBoardSupport below still registers for the
        # Python-dispatched peripherals; GPIO on dev has no meaning anyway.
        return
    here = os.path.dirname(os.path.abspath(__file__))
    path = os.path.join(here, name)
    if not os.path.exists(path):
        # Driver wheel was built without the per-arch .so (dev install from
        # source skipped the C++ build step). Python peripherals still work;
        # Python GPIO on a Pi will fall through to libendpoint's "Unknown
        # board" throw.
        return
    # RTLD_GLOBAL is overkill here (the .so has no symbols other callers need
    # to see), but matches Core's load mode so the dynamic linker behavior is
    # uniform. Harmless either way.
    ctypes.CDLL(path, mode=ctypes.RTLD_GLOBAL)

_load_native_board_driver()


class Zero2W:
    """Raspberry Pi Zero 2 W (BCM2710 SoC). Pin constants + bus / port / channel
    identifiers. The chip-neutral verbs live on Core's I2cController etc.;
    this class is just the hardware tables.
    """

    class Gpio:
        """BCM GPIO pin constants. All pin numbers are BCM GPIO numbers
        (as labelled in every Pi pinout diagram, e.g. https://pinout.xyz),
        NOT physical header pin numbers.
        """
        Led = 29  # on-board ACT LED, active-low
        Pin0  = 0
        Pin1  = 1
        Pin2  = 2
        Pin3  = 3
        Pin4  = 4
        Pin5  = 5
        Pin6  = 6
        Pin7  = 7
        Pin8  = 8
        Pin9  = 9
        Pin10 = 10
        Pin11 = 11
        Pin12 = 12
        Pin13 = 13
        Pin14 = 14
        Pin15 = 15
        Pin16 = 16
        Pin17 = 17
        Pin18 = 18
        Pin19 = 19
        Pin20 = 20
        Pin21 = 21
        Pin22 = 22
        Pin23 = 23
        Pin24 = 24
        Pin25 = 25
        Pin26 = 26
        Pin27 = 27
        GpioChip = 0  # pass to python-gpiod's Chip() / libgpiod

    class I2c:
        """Pi user I2C: SDA on GPIO 2 (pin 3), SCL on GPIO 3 (pin 5), ALT0.
        Maps to /dev/i2c-1.
        """
        I2c1 = 1
        DefaultFrequency = 400_000  # fast mode

    class Spi:
        """Pi user SPI: MOSI=GPIO10, MISO=GPIO9, SCLK=GPIO11, CE0=GPIO8.
        All ALT0. Maps to /dev/spidev0.0.
        """
        Spi0 = 0
        DefaultBufferSize = 4096

    class Pwm:
        """BCM2837 has one PWM block (pwmchip0) with two channels, each
        routable to one of two GPIOs.
        """
        class Pin:
            Pin12 = 12  # PWM0 ch0 at ALT0 (header pin 32)
            Pin13 = 13  # PWM0 ch1 at ALT0 (header pin 33)
            Pin18 = 18  # PWM0 ch0 at ALT5 (header pin 12)
            Pin19 = 19  # PWM0 ch1 at ALT5 (header pin 35)

    class SerialPort:
        """Mini-UART on GPIO 14 TX / GPIO 15 RX, ALT5. Maps to /dev/ttyS0.
        PL011 stays on Bluetooth; mini-UART owns the header pins.
        """
        Uart1 = "/dev/ttyS0"


class Zero2WBoardSupport(_UnknownBoardSupport):
    """Zero2W implementation of IBoardSupport. Overrides only the methods
    Pi actually implements via endpoint-pinmux + modprobe; the rest keep
    UnknownBoardSupport's throw.
    """

    Name = "Zero2W"

    def __init__(self):
        # Shared per-pin pre-Initialize state so UnInitialize restores
        # exactly what was there before.
        self._saved = {}

    # ---------- GPIO introspection ----------
    def GpioChipLabel(self, port): return "gpiochip0"
    def GpioChipId(self, pin):     return 0
    def GpioLineId(self, pin):     return pin
    def GpioName(self, pin):       return f"GPIO{pin}"

    # ---------- I2C ----------
    def I2cInitialize(self, bus, frequency):
        if bus != Zero2W.I2c.I2c1:
            raise ValueError(f"Pi Zero 2 W exposes only I2c1 (/dev/i2c-1), got bus={bus}")
        # Snapshot BEFORE modprobe: the kernel i2c-bcm2835 bind may mux
        # SDA/SCL on its own; capture user's real pre-Init state first.
        self._saved[2] = _Pinmux.Get(2)  # SDA
        self._saved[3] = _Pinmux.Get(3)  # SCL
        _Pinmux.Modprobe("i2c-bcm2835")
        _Pinmux.Modprobe("i2c-dev")
        _Pinmux.Set(2, 4)  # ALT0 = SDA1
        _Pinmux.Set(3, 4)  # ALT0 = SCL1

    def I2cUnInitialize(self, bus):
        sda = self._saved.pop(2, None)
        scl = self._saved.pop(3, None)
        if sda is not None: _Pinmux.Set(2, sda)
        if scl is not None: _Pinmux.Set(3, scl)

    def I2cGetFrequency(self, bus):
        return Zero2W.I2c.DefaultFrequency

    # ---------- SPI ----------
    def SpiInitialize(self, bus, buffer_size):
        if bus != Zero2W.Spi.Spi0:
            raise ValueError(f"Pi Zero 2 W exposes only Spi0 (/dev/spidev0.0), got bus={bus}")
        for p in (10, 9, 11, 8):
            self._saved[p] = _Pinmux.Get(p)
        _Pinmux.Modprobe("spi-bcm2835")
        _Pinmux.Modprobe("spidev")
        for p in (10, 9, 11, 8):
            _Pinmux.Set(p, 4)  # ALT0

    def SpiUnInitialize(self, bus):
        for p in (10, 9, 11, 8):
            v = self._saved.pop(p, None)
            if v is not None: _Pinmux.Set(p, v)

    # ---------- PWM ----------
    _PWM_TABLE = {
        12: (0, 0, 4),  # chip 0 ch 0 at ALT0
        13: (0, 1, 4),  # chip 0 ch 1 at ALT0
        18: (0, 0, 2),  # chip 0 ch 0 at ALT5 (alt pin)
        19: (0, 1, 2),  # chip 0 ch 1 at ALT5 (alt pin)
    }

    def _pwm_lookup(self, pin):
        if pin not in self._PWM_TABLE:
            raise ValueError(f"Pi Zero 2 W PWM pins are 12, 13, 18, 19 — got {pin}")
        return self._PWM_TABLE[pin]

    def PwmInitialize(self, pin):
        _, _, alt = self._pwm_lookup(pin)
        self._saved[pin] = _Pinmux.Get(pin)
        _Pinmux.Modprobe("pwm-bcm2835")
        _Pinmux.Set(pin, alt)

    def PwmUnInitialize(self, pin):
        self._pwm_lookup(pin)  # validate
        v = self._saved.pop(pin, None)
        if v is not None: _Pinmux.Set(pin, v)

    def PwmGetChipId(self, pin):    return self._pwm_lookup(pin)[0]
    def PwmGetChannelId(self, pin): return self._pwm_lookup(pin)[1]

    # ---------- SerialPort ----------
    def SerialPortInitialize(self, port_name, enable_hw_flow_control):
        if port_name != Zero2W.SerialPort.Uart1:
            raise ValueError(f"Pi Zero 2 W exposes only Uart1 (/dev/ttyS0), got {port_name!r}")
        # GPIO 14 TXD1 / 15 RXD1, ALT5 (function code 2)
        self._saved[14] = _Pinmux.Get(14)
        self._saved[15] = _Pinmux.Get(15)
        _Pinmux.Set(14, 2)
        _Pinmux.Set(15, 2)

    def SerialPortUnInitialize(self, port_name):
        tx = self._saved.pop(14, None)
        rx = self._saved.pop(15, None)
        if tx is not None: _Pinmux.Set(14, tx)
        if rx is not None: _Pinmux.Set(15, rx)


# Register at import time, but only if Core's ChipDetect doesn't already
# identify a specific Endpoint chip. On an Endpoint board, the matching
# Pins package registers its own BoardSupport first; we don't fight it.
if _ChipCurrent == _Chip.Unknown:
    _BoardSupport.Register(Zero2WBoardSupport())


__all__ = ["Zero2W", "Zero2WBoardSupport"]
