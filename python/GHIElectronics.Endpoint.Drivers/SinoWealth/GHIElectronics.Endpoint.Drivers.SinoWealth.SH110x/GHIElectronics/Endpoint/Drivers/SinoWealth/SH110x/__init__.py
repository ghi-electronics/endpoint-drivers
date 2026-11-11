"""SinoWealth SH1106 / SH1107 I2C 1bpp OLED driver for GHI Endpoint (Python).

Companion to GHIElectronics.Endpoint.Drivers.SinoWealth.SH110x (.NET).
Caller passes an already-opened I2cDevice.

Usage:

    from GHIElectronics.Endpoint.Core import EPDomino24, I2cController
    from GHIElectronics.Endpoint.Devices.I2c import I2cDevice, I2cConnectionSettings
    from GHIElectronics.Endpoint.Drawing import Canvas, Color, PixelFormat
    from GHIElectronics.Endpoint.Drivers.SinoWealth.SH110x import Sh110xController, Sh110xChip

    I2cController.Initialize(EPDomino24.I2c.I2c6)
    i2c = I2cDevice.Create(Sh110xController.get_connection_settings(EPDomino24.I2c.I2c6))
    oled = Sh110xController(i2c, Sh110xChip.SH1106)
    oled.enable()

    canvas = Canvas.Create(oled.width, oled.height, PixelFormat.Monochrome)
    canvas.Clear(Color.Black)
    canvas.DrawText("Hello", font, 2, 2, 22, Color.White)
    oled.flush(canvas.Buffer, PixelFormat.Monochrome)

Differences from SSD1306 that this driver handles:
 - SH110x has no horizontal addressing mode; a frame is pushed one page at a
   time (set page, set column low/high, write bytes).
 - SH1106 has 132 columns of RAM behind 128 columns of glass, so the visible
   area normally starts at column 2. Adjust at runtime via `column_offset`
   if your module needs a different value.
 - The charge pump command is 0xAD 0x8B, not the SSD1306 0x8D 0x14.
"""

from enum import IntEnum

from GHIElectronics.Endpoint.Devices.I2c import I2cConnectionSettings
from GHIElectronics.Endpoint.Drawing import PixelFormat


class Sh110xChip(IntEnum):
    """132x64 RAM behind 128x64 glass (SH1106) needs a column offset of 2.
    SH1107 is 128x128 RAM (16 pages), used for 128x64 panels and for rotated
    64x128 panels."""
    SH1106 = 0
    SH1107 = 1


# Command set. Shared with SSD1306 except where noted.
_CMD_SET_LOW_COLUMN      = 0x00
_CMD_SET_HIGH_COLUMN     = 0x10
_CMD_SET_MEMORY_MODE     = 0x20    # SH1107: 0x20 page mode, 0x21 vertical mode
_CMD_SET_CONTRAST        = 0x81
_CMD_SET_SEG_REMAP       = 0xA0    # OR 1 for reversed
_CMD_SET_MULTIPLEX       = 0xA8
_CMD_DC_DC               = 0xAD    # SH110x charge pump. SSD1306 uses 0x8D.
_CMD_DC_DC_ON            = 0x8B
_CMD_DISPLAY_OFF         = 0xAE
_CMD_DISPLAY_ON          = 0xAF
_CMD_SET_PAGE            = 0xB0
_CMD_SET_COM_SCAN_INC    = 0xC0
_CMD_SET_COM_SCAN_DEC    = 0xC8
_CMD_SET_DISPLAY_OFFSET  = 0xD3
_CMD_SET_CLOCK_DIV       = 0xD5
_CMD_SET_PRECHARGE       = 0xD9
_CMD_SET_COM_PINS        = 0xDA
_CMD_SET_VCOM_DETECT     = 0xDB
_CMD_SET_START_LINE_1107 = 0xDC    # SH1107 start line is 2 bytes. SSD1306/SH1106 use 0x40 OR line.


class Sh110xController:
    """SinoWealth SH1106 / SH1107 monochrome I2C OLED. Call `enable()` after
    construction, then `flush(pixels, format)` with RGB565 (any non-black
    pixel lights the cell) or Monochrome (1 bpp MSB-first, matches
    Canvas.PixelFormat.Monochrome)."""

    @staticmethod
    def get_connection_settings(bus_id, slave_address=0x3C):
        """Default 7-bit address 0x3C; use 0x3D when SA0 is pulled high."""
        return I2cConnectionSettings(bus_id, slave_address)

    def __init__(self, i2c, chip=Sh110xChip.SH1106, *,
                 width=128, height=64, column_offset=None, rotate90=False):
        if i2c is None:
            raise ValueError("i2c must not be None")
        if width <= 0 or height <= 0 or (width % 8) != 0 or (height % 8) != 0:
            raise ValueError("Width and height must be positive multiples of 8.")
        if rotate90 and chip != Sh110xChip.SH1107:
            raise ValueError("rotate90 is only supported on SH1107.")

        self._i2c = i2c
        self._chip = chip
        self.width = width
        self.height = height
        self._rotate90 = rotate90
        self._column_offset = column_offset if column_offset is not None else (2 if chip == Sh110xChip.SH1106 else 0)

        # Not rotated: a controller page spans the width, pages stack down the height.
        # Rotated: controller columns run down the screen height, pages span the width.
        self._page_width = height if rotate90 else width
        self._page_count = (width // 8) if rotate90 else (height // 8)

        self._vram = bytearray(width * height // 8)
        self._shadow = bytearray(self._page_count * self._page_width)
        self._page_buffer = bytearray(self._page_width + 1)
        self._page_buffer[0] = 0x40    # data stream control byte
        self._command_buffer = bytearray(2)

        self.skip_unchanged_pages = True
        self._force_full_flush = True
        self._is_enabled = False

    @property
    def chip(self):
        return self._chip

    @property
    def is_enabled(self):
        return self._is_enabled

    @property
    def column_offset(self):
        """First visible RAM column. SH1106 on 128-wide glass is almost
        always 2, a few clones are 0."""
        return self._column_offset

    @column_offset.setter
    def column_offset(self, value):
        self._column_offset = value
        self._force_full_flush = True

    def enable(self):
        if self._chip == Sh110xChip.SH1106:
            self._initialize_sh1106()
        else:
            self._initialize_sh1107()

        self.clear()

        # GDDRAM powers up random; only light the panel once it is cleared.
        self.send_command(_CMD_DISPLAY_ON)
        self._is_enabled = True

    def disable(self):
        if not self._is_enabled:
            return
        self.send_command(_CMD_DISPLAY_OFF)
        self._is_enabled = False

    def flush(self, pixels, pixel_format, x=0, y=0, width=None, height=None):
        """Push a region of the panel. `pixel_format` is a
        GHIElectronics.Endpoint.Drawing.PixelFormat value (Rgb565 or
        Monochrome)."""
        if width is None: width = self.width
        if height is None: height = self.height
        if x < 0 or y < 0 or width <= 0 or height <= 0 \
                or x + width > self.width or y + height > self.height:
            raise ValueError("Flush region outside the panel")

        if pixel_format == PixelFormat.Rgb565:
            if len(pixels) < width * height * 2:
                raise ValueError("Pixel buffer is smaller than the region")
            for row in range(height):
                panel_y = y + row
                page = panel_y >> 3
                mask = 1 << (panel_y & 7)
                src_row = row * width * 2
                for col in range(width):
                    src_index = src_row + col * 2
                    px = pixels[src_index] | (pixels[src_index + 1] << 8)
                    v_index = page * self.width + (x + col)
                    if px != 0:
                        self._vram[v_index] |= mask
                    else:
                        self._vram[v_index] &= (~mask) & 0xFF
        elif pixel_format == PixelFormat.Monochrome:
            # Canvas.PixelFormat.Monochrome: 1 bit per pixel, MSB-first,
            # row stride = (width + 7) / 8 bytes.
            src_stride = (width + 7) // 8
            if len(pixels) < src_stride * height:
                raise ValueError("Pixel buffer is smaller than the region")
            for row in range(height):
                panel_y = y + row
                page = panel_y >> 3
                mask = 1 << (panel_y & 7)
                src_row = row * src_stride
                for col in range(width):
                    lit = (pixels[src_row + (col >> 3)] & (1 << (7 - (col & 7)))) != 0
                    v_index = page * self.width + (x + col)
                    if lit:
                        self._vram[v_index] |= mask
                    else:
                        self._vram[v_index] &= (~mask) & 0xFF
        else:
            raise ValueError(
                "Sh110xController accepts Rgb565 or Monochrome pixel formats.")

        self._flush_vram()

    def draw_buffer_native(self, buffer, offset=0, count=None):
        """Push a pre-packed VRAM blob (SH110x native page/column format,
        `width * height / 8` bytes). Faster than the Rgb565 / Monochrome paths
        when the caller already has the panel's native layout."""
        if buffer is None:
            raise ValueError("buffer must not be None")
        if count is None:
            count = len(buffer) - offset
        if count > len(self._vram):
            count = len(self._vram)
        self._vram[:count] = buffer[offset: offset + count]
        self._flush_vram()

    def send_command(self, command):
        """Raw command escape hatch, useful while feeling out an unknown module."""
        self._command_buffer[0] = 0x00    # command stream control byte
        self._command_buffer[1] = command
        self._i2c.Write(bytes(self._command_buffer))

    def send_commands(self, commands):
        for c in commands:
            self.send_command(c)

    def set_color_format(self, invert):
        """Invert the display (hardware-level inversion)."""
        self.send_command(0xA7 if invert else 0xA6)

    def set_contrast(self, level):
        self.send_command(_CMD_SET_CONTRAST)
        self.send_command(level & 0xFF)

    def set_upside_down(self, upside_down):
        """Turns the picture 180 degrees. Both commands have to agree or the
        image comes out mirrored."""
        self.send_command(_CMD_SET_SEG_REMAP if upside_down else (_CMD_SET_SEG_REMAP | 0x01))
        self.send_command(_CMD_SET_COM_SCAN_INC if upside_down else _CMD_SET_COM_SCAN_DEC)

    def turn_on(self):  self.send_command(_CMD_DISPLAY_ON)
    def turn_off(self): self.send_command(_CMD_DISPLAY_OFF)

    def clear(self):
        for i in range(len(self._vram)):
            self._vram[i] = 0
        # Wipe every RAM column, not just the visible window. SH1106 has 132
        # columns behind 128 of glass; outside columns otherwise keep showing
        # power-up garbage as a stripe at one edge.
        self._clear_controller_ram()
        for i in range(len(self._shadow)):
            self._shadow[i] = 0
        self._force_full_flush = False

    def dispose(self):
        try: self.disable()
        except Exception: pass
        try: self._i2c.Dispose()
        except Exception: pass

    def __enter__(self): return self
    def __exit__(self, *_): self.dispose()
    def __del__(self):
        try: self.dispose()
        except Exception: pass

    # ---- internals ---------------------------------------------------

    def _initialize_sh1106(self):
        self.send_command(_CMD_DISPLAY_OFF)
        self.send_command(_CMD_SET_CLOCK_DIV);     self.send_command(0x80)
        self.send_command(_CMD_SET_MULTIPLEX);     self.send_command(self.height - 1)
        self.send_command(_CMD_SET_DISPLAY_OFFSET); self.send_command(0x00)
        self.send_command(0x40)                                          # start line 0
        self.send_command(_CMD_DC_DC);             self.send_command(_CMD_DC_DC_ON)
        self.send_command(0x32)                                          # pump output 8.0V
        self.send_command(_CMD_SET_SEG_REMAP | 0x01)                      # column 127 maps to SEG0
        self.send_command(_CMD_SET_COM_SCAN_DEC)
        self.send_command(_CMD_SET_COM_PINS);      self.send_command(0x12 if self.height > 32 else 0x02)
        self.send_command(_CMD_SET_CONTRAST);      self.send_command(0x80)
        self.send_command(_CMD_SET_PRECHARGE);     self.send_command(0x22)
        self.send_command(_CMD_SET_VCOM_DETECT);   self.send_command(0x35)
        self.send_command(0xA4)                                          # resume from RAM
        self.send_command(0xA6)                                          # normal, not inverted

    def _initialize_sh1107(self):
        self.send_command(_CMD_DISPLAY_OFF)
        self.send_command(_CMD_SET_CLOCK_DIV);     self.send_command(0x51)
        self.send_command(_CMD_SET_MEMORY_MODE)                           # page addressing mode
        self.send_command(_CMD_SET_CONTRAST);      self.send_command(0x4F)
        self.send_command(_CMD_DC_DC);             self.send_command(_CMD_DC_DC_ON)
        self.send_command(_CMD_SET_SEG_REMAP)                             # 0xA0 normal, 0xA1 mirrored
        self.send_command(_CMD_SET_COM_SCAN_INC)                          # 0xC0 normal, 0xC8 flipped
        self.send_command(_CMD_SET_START_LINE_1107); self.send_command(0x00)
        self.send_command(_CMD_SET_DISPLAY_OFFSET);  self.send_command(0x00)
        self.send_command(_CMD_SET_MULTIPLEX);     self.send_command((self.width if self._rotate90 else self.height) - 1)
        self.send_command(_CMD_SET_PRECHARGE);     self.send_command(0x22)
        self.send_command(_CMD_SET_VCOM_DETECT);   self.send_command(0x35)
        self.send_command(0xA4)
        self.send_command(0xA6)

    def _clear_controller_ram(self):
        ram_width = 132 if self._chip == Sh110xChip.SH1106 else 128
        ram_pages = 8 if self._chip == Sh110xChip.SH1106 else 16
        zeros = bytearray(ram_width + 1)
        zeros[0] = 0x40    # data stream control byte

        for page in range(ram_pages):
            self.send_command(_CMD_SET_PAGE | page)
            self.send_command(_CMD_SET_LOW_COLUMN)
            self.send_command(_CMD_SET_HIGH_COLUMN)
            self._i2c.Write(bytes(zeros))

    def _flush_vram(self):
        """Pushes vram to the panel one page per transaction. SH110x has no
        horizontal addressing."""
        full = self._force_full_flush
        self._force_full_flush = False

        for page in range(self._page_count):
            shadow_start = page * self._page_width

            if self._rotate90:
                self._build_rotated_page(page)
            else:
                src = page * self.width
                self._page_buffer[1: 1 + self._page_width] = \
                    self._vram[src: src + self._page_width]

            if (not full) and self.skip_unchanged_pages \
                    and self._page_unchanged(shadow_start):
                continue

            self._shadow[shadow_start: shadow_start + self._page_width] = \
                self._page_buffer[1: 1 + self._page_width]

            self.send_command(_CMD_SET_PAGE | page)
            self.send_command(_CMD_SET_LOW_COLUMN  | (self._column_offset & 0x0F))
            self.send_command(_CMD_SET_HIGH_COLUMN | ((self._column_offset >> 4) & 0x0F))

            self._i2c.Write(bytes(self._page_buffer))

    def _page_unchanged(self, shadow_start):
        for i in range(self._page_width):
            if self._page_buffer[i + 1] != self._shadow[shadow_start + i]:
                return False
        return True

    def _build_rotated_page(self, page):
        """Transpose a landscape frame into the portrait layout an SH1107
        64x128 panel expects. Controller page p covers screen columns
        x = p*8 through p*8+7, controller column c is screen row y."""
        base_x = page * 8
        for c in range(self._page_width):
            row_start = (c >> 3) * self.width
            mask = 1 << (c & 7)
            value = 0
            for bit in range(8):
                if self._vram[row_start + base_x + bit] & mask:
                    value |= 1 << bit
            self._page_buffer[c + 1] = value
