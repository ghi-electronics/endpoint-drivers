"""HiLetgo ILI9341 240x320 SPI TFT driver for GHI Endpoint (Python).

Companion to GHIElectronics.Endpoint.Drivers.HiLetgo.ILI9341 (.NET).
Drop this file into a project; the caller passes in an already-opened
SpiDevice plus a GpioController.

Wiring (any free GPIOs for CS/DC/RST):
    ILI9341     EPDomino24
    -------     ----------
    VCC         mikroBUS 3.3V
    GND         mikroBUS GND
    SCK         mikroBUS SCK   (e.g. PZ0 on mikroBUS #2)
    SDI/MOSI    mikroBUS MOSI  (e.g. PZ2 on mikroBUS #2)
    CS          any free GPIO  (driven in software by this driver)
    DC / RS     any free GPIO  (low = command, high = data)
    RST         any free GPIO  (active-low pulse at init)
    LED / BL    tie to +3.3V   (backlight always on)

Usage:

    from GHIElectronics.Endpoint.Core import EPDomino24, EPM815, SpiController
    from GHIElectronics.Endpoint.Devices.Spi import (
        SpiDevice, SpiConnectionSettings, SpiMode)
    from GHIElectronics.Endpoint.Devices.Gpio import GpioController
    from ili9341 import Ili9341

    SpiController.Initialize(EPDomino24.Spi.Spi1, bufferSize=4096)
    settings = SpiConnectionSettings(EPDomino24.Spi.Spi1)
    settings.Mode = SpiMode.Mode0
    settings.ClockFrequency = 10_000_000
    spi = SpiDevice.Create(settings)

    gpio = GpioController()
    tft = Ili9341(gpio, spi,
                  cs_pin=EPM815.Gpio.Pin.PA14,
                  dc_pin=EPM815.Gpio.Pin.PF14,
                  rst_pin=EPM815.Gpio.Pin.PF4,
                  swap_red_blue=True)   # set for modules where yellow shows as cyan
    tft.initialize()

    # Canvas drawing...
    tft.flush(canvas.Buffer)
"""

import time
from GHIElectronics.Endpoint.Devices.Gpio import PinMode, PinValue


class Ili9341:
    """ILI9341 SPI TFT driver. Caller owns the GpioController + SpiDevice.
    `swap_red_blue` compensates for modules with physical BGR filter wiring
    (yellow would otherwise render as cyan)."""

    def __init__(self, gpio, spi, cs_pin, dc_pin, rst_pin, *,
                 width=240, height=320, swap_red_blue=False):
        self._gpio, self._spi = gpio, spi
        self._cs, self._dc, self._rst = cs_pin, dc_pin, rst_pin
        self.width, self.height = width, height
        self.swap_red_blue = swap_red_blue
        for p in (self._cs, self._dc, self._rst):
            self._gpio.OpenPin(p, PinMode.Output, PinValue.High)
        # Staging buffer for the per-frame byte re-encode in flush. Allocated
        # once; reused on every push.
        self._swap = bytearray(width * height * 2)

    def initialize(self):
        # Hard reset: low 20 ms, high 150 ms before the chip answers.
        self._gpio.Write(self._rst, PinValue.Low);  time.sleep(0.02)
        self._gpio.Write(self._rst, PinValue.High); time.sleep(0.15)
        self._cmd(0x01); time.sleep(0.15)                       # SWRESET
        self._cmd(0x11); time.sleep(0.12)                       # SLPOUT
        self._cmd(0x3A, bytes([0x55]))                          # COLMOD: 16bpp
        # MADCTL: MX only (0x40). Portrait 240x320, RGB filter. BGR-wired
        # panels are handled via swap_red_blue in flush, not here.
        self._cmd(0x36, bytes([0x40]))
        self._cmd(0x29); time.sleep(0.02)                       # DISPON

    def flush(self, pixels):
        """Push a full frame to the panel.

        `pixels` is an RGB565 little-endian buffer (bytes / bytearray /
        memoryview) as produced by Canvas.Buffer or by LVGL's custom
        display flush callback. ILI9341 reads 16 bpp big-endian over SPI,
        so we always byte-swap; swap_red_blue additionally swaps R and B
        for BGR-wired modules."""
        src, dst = pixels, self._swap
        if len(dst) != len(src):
            dst = bytearray(len(src))
            self._swap = dst
        if self.swap_red_blue:
            for i in range(0, len(src), 2):
                lo, hi = src[i], src[i + 1]
                r = hi >> 3
                g = ((hi & 0x07) << 3) | (lo >> 5)
                b = lo & 0x1F
                dst[i]     = (b << 3) | (g >> 3)
                dst[i + 1] = ((g & 0x07) << 5) | r
        else:
            for i in range(0, len(src), 2):
                dst[i], dst[i + 1] = src[i + 1], src[i]
        self._set_window(0, 0, self.width, self.height)
        self._cmd(0x2C)                                         # RAMWR
        self._write_data(dst)

    # ---- internals ---------------------------------------------------

    def _cmd(self, cmd, data=None):
        self._gpio.Write(self._cs, PinValue.Low)
        self._gpio.Write(self._dc, PinValue.Low)
        self._spi.Write(bytes([cmd]))
        if data:
            self._gpio.Write(self._dc, PinValue.High)
            self._spi.Write(data)
        self._gpio.Write(self._cs, PinValue.High)

    def _write_data(self, data):
        self._gpio.Write(self._cs, PinValue.Low)
        self._gpio.Write(self._dc, PinValue.High)
        # Linux spidev default bufsiz tops out near 4 KB per write(); chunk at 2 KB.
        view = memoryview(data)
        for off in range(0, len(view), 2048):
            self._spi.Write(view[off: off + 2048])
        self._gpio.Write(self._cs, PinValue.High)

    def _set_window(self, x, y, w, h):
        x1, x2, y1, y2 = x, x + w - 1, y, y + h - 1
        self._cmd(0x2A, bytes([x1 >> 8, x1 & 0xFF, x2 >> 8, x2 & 0xFF]))
        self._cmd(0x2B, bytes([y1 >> 8, y1 & 0xFF, y2 >> 8, y2 & 0xFF]))
