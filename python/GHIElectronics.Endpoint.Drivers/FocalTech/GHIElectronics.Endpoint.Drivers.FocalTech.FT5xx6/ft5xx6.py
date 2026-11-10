"""FocalTech FT5xx6 capacitive-touch driver for GHI Endpoint (Python).

Companion to GHIElectronics.Endpoint.Drivers.FocalTech.FT5xx6 (.NET).
Reusable across any Endpoint board: the caller opens a GpioController
and passes it in; this file does no chip-specific pin math.

Usage:

    from GHIElectronics.Endpoint.Core import EPDomino24, I2cController
    from GHIElectronics.Endpoint.Devices.Gpio import GpioController
    from ft5xx6 import Ft5xx6Controller

    I2cController.Initialize(EPDomino24.I2c.I2c5)
    gpio = GpioController()
    touch = Ft5xx6Controller(
        i2c_bus=EPDomino24.I2c.I2c5,
        interrupt_pin=EPDomino24.Gpio.Pin.PB11,
        gpio=gpio)
    touch.TouchDown += lambda s, e: print(f"down ({e.X}, {e.Y})")
"""

from enum import IntEnum

from GHIElectronics.Endpoint.Core._event import Event
from GHIElectronics.Endpoint.Devices.Gpio import PinMode, PinEventTypes
from GHIElectronics.Endpoint.Devices.I2c import I2cDevice, I2cConnectionSettings


DEVICE_ADDRESS = 0x38


class TouchEventArgs:
    __slots__ = ("X", "Y")

    def __init__(self, x, y):
        self.X = x
        self.Y = y


class Gesture(IntEnum):
    MoveUp = 0x10
    MoveLeft = 0x14
    MoveDown = 0x18
    MoveRight = 0x1C
    ZoomIn = 0x48
    ZoomOut = 0x49


class GestureEventArgs:
    __slots__ = ("Gesture",)

    def __init__(self, gesture):
        self.Gesture = gesture


class TouchOrientation(IntEnum):
    Degrees0 = 0
    Degrees90 = 1
    Degrees180 = 2
    Degrees270 = 3


class Ft5xx6Controller:
    """FocalTech FT5xx6 capacitive touch controller. TouchDown / TouchUp /
    TouchMove and GestureReceived are `+=` / `-=` events (same shape as
    GpioController.ValueChanged)."""

    DeviceAddress = DEVICE_ADDRESS

    def __init__(self, i2c_bus, interrupt_pin, gpio):
        if gpio is None:
            raise ValueError("gpio must not be None")

        self._i2c = I2cDevice.Create(I2cConnectionSettings(i2c_bus, DEVICE_ADDRESS))
        self._gpio = gpio
        self._interrupt_pin = interrupt_pin
        self._address_buffer = bytes([0])
        self._read32 = bytearray(32)

        self.TouchDown = Event()
        self.TouchUp = Event()
        self.TouchMove = Event()
        self.GestureReceived = Event()

        self.Width = 0
        self.Height = 0
        self.Orientation = TouchOrientation.Degrees0
        self.SampleCount = 5

        self._gpio.OpenPin(self._interrupt_pin, PinMode.Input)
        self._gpio.RegisterCallbackForPinValueChangedEvent(
            self._interrupt_pin, PinEventTypes.Falling, self._on_interrupt)

    def _on_interrupt(self, _sender, _args):
        try:
            self._i2c.WriteRead(self._address_buffer, self._read32)
            buf = self._read32

            if buf[1] != 0 and len(self.GestureReceived):
                self.GestureReceived(self, GestureEventArgs(Gesture(buf[1])))

            # We do not read TD_STATUS: it excludes touch-up events even
            # though the per-sample registers contain the correct data.
            for i in range(self.SampleCount):
                idx = i * 6 + 3
                flag = (buf[idx] & 0xC0) >> 6
                x = ((buf[idx] & 0x0F) << 8) | buf[idx + 1]
                y = ((buf[idx + 2] & 0x0F) << 8) | buf[idx + 3]

                if self.Orientation != TouchOrientation.Degrees0:
                    if self.Width == 0 or self.Height == 0:
                        raise ValueError("Width and Height must be set before rotating")
                    if self.Orientation == TouchOrientation.Degrees180:
                        x = self.Width - x
                        y = self.Height - y
                    elif self.Orientation == TouchOrientation.Degrees270:
                        temp = x
                        x = self.Width - y
                        y = temp
                    elif self.Orientation == TouchOrientation.Degrees90:
                        tmp = x
                        x = y
                        y = self.Width - tmp

                args = TouchEventArgs(x, y)
                if flag == 0:
                    self.TouchDown(self, args)
                elif flag == 1:
                    self.TouchUp(self, args)
                elif flag == 2:
                    self.TouchMove(self, args)
                elif flag == 3:
                    break
        except Exception:
            # Don't tear down the IRQ handler on a single bad read.
            pass

    def Dispose(self):
        try:
            self._gpio.UnregisterCallbackForPinValueChangedEvent(
                self._interrupt_pin, self._on_interrupt)
        except Exception:
            pass
        try: self._gpio.ClosePin(self._interrupt_pin)
        except Exception: pass
        try: self._i2c.Dispose()
        except Exception: pass

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.Dispose()

    def __del__(self):
        try: self.Dispose()
        except Exception: pass
