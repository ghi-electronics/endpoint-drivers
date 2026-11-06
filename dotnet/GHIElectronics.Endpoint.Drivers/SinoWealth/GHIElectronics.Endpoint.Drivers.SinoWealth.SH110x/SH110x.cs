// SinoWealth SH1106 / SH1107 I2C 1bpp OLED driver for GHI Endpoint. Inherits
// from GHIElectronics.Endpoint.Devices.Display.DisplayController so applications
// use the same open / Enable / Flush pattern as the built-in parallel panel.
// The Flush override accepts an RGB565 source and thresholds any non-black
// pixel to "lit".
//
//     using var i2c  = I2cDevice.Create(SH110xController.GetConnectionSettings(EPDomino24.I2c.I2c1));
//     using var oled = new SH110xController(i2c, SH110xChip.SH1106);
//     oled.Enable();
//     oled.Flush(rgb565Bytes);   // 128 * 64 * 2 bytes
//
// Differences from SSD1306 that this driver handles:
//  - SH110x has no horizontal addressing mode; a frame is pushed one page at a
//    time (set page, set column low/high, write bytes).
//  - SH1106 has 132 columns of RAM behind 128 columns of glass, so the visible
//    area normally starts at column 2. Adjust at runtime via ColumnOffset if
//    your module needs a different value.
//  - The charge pump command is 0xAD 0x8B, not the SSD1306 0x8D 0x14.

using System;
using System.Device.I2c;

using GHIElectronics.Endpoint.Devices.Display;

// Alias to the same System.Drawing.Imaging.PixelFormat type the base class uses.
using PixelFormat = System.Drawing.Imaging.PixelFormat;

#pragma warning disable CA1416  // System.Drawing.Imaging.PixelFormat is Windows-annotated; we only use the int values.

namespace GHIElectronics.Endpoint.Drivers.SinoWealth.SH110x {
    public enum SH110xChip {
        /// <summary>132x64 RAM behind 128x64 glass. Needs a column offset (usually 2). No horizontal addressing mode.</summary>
        SH1106 = 0,
        /// <summary>128x128 RAM (16 pages). Used for 128x64 panels and for rotated 64x128 panels.</summary>
        SH1107 = 1,
    }

    /// <summary>
    /// SinoWealth SH1106 / SH1107 monochrome I2C OLED. Subclass of
    /// <see cref="DisplayController"/>; call <see cref="Enable"/> after
    /// construction, then
    /// <see cref="DisplayController.Flush(ReadOnlySpan{byte})"/> to push pixels
    /// (non-black = lit). Input buffer layout for <see cref="DrawBufferNative(byte[])"/>
    /// is the native 1bpp page/column format:
    /// <c>index = (y / 8) * Width + x, bit = 1 shifted left by (y % 8)</c>.
    /// </summary>
    public class SH110xController : DisplayController {
        // Command set. Shared with SSD1306 except where noted.
        private const byte CMD_SET_LOW_COLUMN      = 0x00;
        private const byte CMD_SET_HIGH_COLUMN     = 0x10;
        private const byte CMD_SET_MEMORY_MODE     = 0x20;    // SH1107: 0x20 page mode, 0x21 vertical mode
        private const byte CMD_SET_CONTRAST        = 0x81;
        private const byte CMD_SET_SEG_REMAP       = 0xA0;    // OR 1 for reversed
        private const byte CMD_SET_MULTIPLEX       = 0xA8;
        private const byte CMD_DC_DC               = 0xAD;    // SH110x charge pump. SSD1306 uses 0x8D.
        private const byte CMD_DC_DC_ON            = 0x8B;
        private const byte CMD_DISPLAY_OFF         = 0xAE;
        private const byte CMD_DISPLAY_ON          = 0xAF;
        private const byte CMD_SET_PAGE            = 0xB0;
        private const byte CMD_SET_COM_SCAN_INC    = 0xC0;
        private const byte CMD_SET_COM_SCAN_DEC    = 0xC8;
        private const byte CMD_SET_DISPLAY_OFFSET  = 0xD3;
        private const byte CMD_SET_CLOCK_DIV       = 0xD5;
        private const byte CMD_SET_PRECHARGE       = 0xD9;
        private const byte CMD_SET_COM_PINS        = 0xDA;
        private const byte CMD_SET_VCOM_DETECT     = 0xDB;
        private const byte CMD_SET_START_LINE_1107 = 0xDC;    // SH1107 start line is 2 bytes. SSD1306/SH1106 use 0x40 OR line.

        private readonly I2cDevice i2c;
        private readonly SH110xChip chip;
        private readonly int width;
        private readonly int height;
        private readonly bool rotate90;

        private readonly byte[] vram;              // caller space frame, (width * height) / 8
        private readonly byte[] shadow;            // what was last pushed, for skip unchanged pages
        private readonly byte[] pageBuffer;        // 0x40 control byte plus one controller page
        private readonly byte[] commandBuffer = new byte[2];

        private readonly int pageCount;            // controller pages pushed per frame
        private readonly int pageWidth;            // controller columns per page

        private int columnOffset;
        private bool forceFullFlush = true;
        private bool isEnabled;

        public override int Width  => this.width;
        public override int Height => this.height;
        public override bool IsEnabled => this.isEnabled;
        public SH110xChip Chip => this.chip;

        /// <summary>
        /// First visible RAM column. SH1106 on 128 wide glass is almost always
        /// 2, a few clones are 0. If the image is shifted sideways with junk
        /// at one edge, this is the knob to turn.
        /// </summary>
        public int ColumnOffset {
            get => this.columnOffset;
            set {
                this.columnOffset = value;
                this.forceFullFlush = true;
            }
        }

        /// <summary>When true (default) pages whose content did not change are not re-sent over I2C.</summary>
        public bool SkipUnchangedPages { get; set; } = true;

        /// <summary>Default I2C settings: 7-bit address 0x3C. Bus speed is set per I2C bus at the kernel level.</summary>
        public static I2cConnectionSettings GetConnectionSettings(int busId) => GetConnectionSettings(busId, 0x3C);

        /// <summary>I2C settings with an explicit slave address (0x3D when SA0 is pulled high).</summary>
        public static I2cConnectionSettings GetConnectionSettings(int busId, int slaveAddress) =>
            new(busId, slaveAddress);

        /// <summary>SH1106 128x64 with the usual column offset of 2.</summary>
        public SH110xController(I2cDevice i2c) : this(i2c, SH110xChip.SH1106) { }

        /// <summary>128x64 panel, default column offset for the chip: 2 for SH1106, 0 for SH1107.</summary>
        public SH110xController(I2cDevice i2c, SH110xChip chip)
            : this(i2c, chip, 128, 64, chip == SH110xChip.SH1106 ? 2 : 0, false) { }

        /// <param name="rotate90">
        /// SH1107 only. Set this for panels whose glass is physically 64 wide
        /// by 128 tall but that you want to drive as 128x64 landscape (the
        /// Adafruit style 1.12 inch and FeatherWing modules). The frame is
        /// transposed in software, which costs real CPU time, so leave it
        /// false unless the picture comes out sideways.
        /// </param>
        public SH110xController(I2cDevice i2c, SH110xChip chip, int width, int height, int columnOffset, bool rotate90) {
            if (i2c == null) throw new ArgumentNullException(nameof(i2c));
            if (width <= 0 || height <= 0 || (width % 8) != 0 || (height % 8) != 0)
                throw new ArgumentException("Width and height must be positive multiples of 8.");
            if (rotate90 && chip != SH110xChip.SH1107)
                throw new ArgumentException("rotate90 is only supported on SH1107.");

            this.i2c = i2c;
            this.chip = chip;
            this.width = width;
            this.height = height;
            this.columnOffset = columnOffset;
            this.rotate90 = rotate90;

            // Not rotated: a controller page spans the screen width and pages stack down the height.
            // Rotated: the frame is transposed, so controller columns run down the screen height.
            this.pageWidth = rotate90 ? height : width;
            this.pageCount = rotate90 ? width / 8 : height / 8;

            this.vram = new byte[width * height / 8];
            this.shadow = new byte[this.pageCount * this.pageWidth];
            this.pageBuffer = new byte[this.pageWidth + 1];
            this.pageBuffer[0] = 0x40;    // data stream control byte
        }

        public override void Enable() {
            if (this.chip == SH110xChip.SH1106)
                this.InitializeSH1106();
            else
                this.InitializeSH1107();

            this.Clear();

            // GDDRAM powers up random, so only light the panel once it has been cleared.
            this.SendCommand(CMD_DISPLAY_ON);
            this.isEnabled = true;
        }

        public override void Disable() {
            if (!this.isEnabled) return;
            this.SendCommand(CMD_DISPLAY_OFF);
            this.isEnabled = false;
        }

        /// <summary>
        /// Flush a region of the panel. Source must be
        /// <see cref="PixelFormat.Format16bppRgb565"/>; any non-zero source
        /// pixel lights the corresponding OLED cell. The dirty region is
        /// written into VRAM and then only changed pages are pushed over I2C
        /// (unless <see cref="SkipUnchangedPages"/> is false).
        /// </summary>
        public override void Flush(ReadOnlySpan<byte> pixels, PixelFormat format,
                                   int x, int y, int width, int height) {
            if (format != PixelFormat.Format16bppRgb565) {
                throw new NotSupportedException("SH110xController only supports Format16bppRgb565 today.");
            }
            if (x < 0 || y < 0 || width <= 0 || height <= 0
                || x + width > this.width || y + height > this.height) {
                throw new ArgumentException("Flush region outside the panel");
            }
            if (pixels.Length < width * height * 2) {
                throw new ArgumentException("Pixel buffer is smaller than the region");
            }

            for (int row = 0; row < height; row++) {
                int panelY = y + row;
                int page = panelY >> 3;
                byte mask = (byte)(1 << (panelY & 7));
                int srcRow = row * width * 2;
                for (int col = 0; col < width; col++) {
                    int srcIndex = srcRow + col * 2;
                    ushort px = (ushort)(pixels[srcIndex] | (pixels[srcIndex + 1] << 8));
                    int vIndex = page * this.width + (x + col);
                    if (px != 0) this.vram[vIndex] |= mask;
                    else         this.vram[vIndex] = (byte)(this.vram[vIndex] & ~mask);
                }
            }

            this.FlushVram();
        }

        /// <summary>
        /// Push a pre-packed VRAM blob (SH110x native page/column format,
        /// <c>Width * Height / 8</c> bytes). Faster than the RGB565 path when
        /// the caller already has the panel's native layout.
        /// </summary>
        public void DrawBufferNative(byte[] buffer) => this.DrawBufferNative(buffer, 0, buffer.Length);

        public void DrawBufferNative(byte[] buffer, int offset, int count) {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (count > this.vram.Length) count = this.vram.Length;
            Array.Copy(buffer, offset, this.vram, 0, count);
            this.FlushVram();
        }

        public override void Dispose() {
            try { this.Disable(); } catch { }
            try { this.i2c.Dispose(); } catch { }
        }

        /// <summary>Raw command escape hatch, useful while feeling out an unknown module.</summary>
        public void SendCommand(byte command) {
            this.commandBuffer[0] = 0x00;    // command stream control byte
            this.commandBuffer[1] = command;
            this.i2c.Write(this.commandBuffer);
        }

        public void SendCommands(byte[] commands) {
            for (var i = 0; i < commands.Length; i++)
                this.SendCommand(commands[i]);
        }

        /// <summary>Invert the display (hardware-level inversion).</summary>
        public void SetColorFormat(bool invert) => this.SendCommand((byte)(invert ? 0xA7 : 0xA6));

        public void SetContrast(byte level) {
            this.SendCommand(CMD_SET_CONTRAST);
            this.SendCommand(level);
        }

        /// <summary>Turns the picture 180 degrees. Both commands have to agree or the image comes out mirrored.</summary>
        public void SetUpsideDown(bool upsideDown) {
            this.SendCommand((byte)(upsideDown ? CMD_SET_SEG_REMAP : (CMD_SET_SEG_REMAP | 0x01)));
            this.SendCommand(upsideDown ? CMD_SET_COM_SCAN_INC : CMD_SET_COM_SCAN_DEC);
        }

        public void TurnOn()  => this.SendCommand(CMD_DISPLAY_ON);
        public void TurnOff() => this.SendCommand(CMD_DISPLAY_OFF);

        public void Clear() {
            Array.Clear(this.vram, 0, this.vram.Length);

            // Wipe every RAM column, not just the visible window. SH1106 has
            // 132 columns behind 128 of glass, so columns outside the current
            // ColumnOffset window would otherwise keep showing power-up
            // garbage as a stripe at one edge.
            this.ClearControllerRam();

            Array.Clear(this.shadow, 0, this.shadow.Length);

            this.forceFullFlush = false;    // controller RAM and shadow are both known zero now
        }

        private void InitializeSH1106() {
            this.SendCommand(CMD_DISPLAY_OFF);
            this.SendCommand(CMD_SET_CLOCK_DIV);     this.SendCommand(0x80);
            this.SendCommand(CMD_SET_MULTIPLEX);     this.SendCommand((byte)(this.height - 1));
            this.SendCommand(CMD_SET_DISPLAY_OFFSET); this.SendCommand(0x00);
            this.SendCommand(0x40);                                              // start line 0
            this.SendCommand(CMD_DC_DC);             this.SendCommand(CMD_DC_DC_ON);
            this.SendCommand(0x32);                                              // pump output 8.0V, range 0x30 to 0x33
            this.SendCommand((byte)(CMD_SET_SEG_REMAP | 0x01));                  // column 127 maps to SEG0
            this.SendCommand(CMD_SET_COM_SCAN_DEC);                              // scan COM[N-1] down to COM0
            this.SendCommand(CMD_SET_COM_PINS);      this.SendCommand((byte)(this.height > 32 ? 0x12 : 0x02));
            this.SendCommand(CMD_SET_CONTRAST);      this.SendCommand(0x80);
            this.SendCommand(CMD_SET_PRECHARGE);     this.SendCommand(0x22);
            this.SendCommand(CMD_SET_VCOM_DETECT);   this.SendCommand(0x35);
            this.SendCommand(0xA4);                                              // resume from RAM
            this.SendCommand(0xA6);                                              // normal, not inverted
        }

        private void InitializeSH1107() {
            this.SendCommand(CMD_DISPLAY_OFF);
            this.SendCommand(CMD_SET_CLOCK_DIV);     this.SendCommand(0x51);
            this.SendCommand(CMD_SET_MEMORY_MODE);                               // page addressing mode
            this.SendCommand(CMD_SET_CONTRAST);      this.SendCommand(0x4F);
            this.SendCommand(CMD_DC_DC);             this.SendCommand(CMD_DC_DC_ON);
            this.SendCommand(CMD_SET_SEG_REMAP);                                 // 0xA0 normal, 0xA1 mirrored
            this.SendCommand(CMD_SET_COM_SCAN_INC);                              // 0xC0 normal, 0xC8 flipped
            this.SendCommand(CMD_SET_START_LINE_1107); this.SendCommand(0x00);
            this.SendCommand(CMD_SET_DISPLAY_OFFSET); this.SendCommand(0x00);
            this.SendCommand(CMD_SET_MULTIPLEX);     this.SendCommand((byte)((this.rotate90 ? this.width : this.height) - 1));
            this.SendCommand(CMD_SET_PRECHARGE);     this.SendCommand(0x22);
            this.SendCommand(CMD_SET_VCOM_DETECT);   this.SendCommand(0x35);
            this.SendCommand(0xA4);
            this.SendCommand(0xA6);
        }

        private void ClearControllerRam() {
            var ramWidth = this.chip == SH110xChip.SH1106 ? 132 : 128;
            var ramPages = this.chip == SH110xChip.SH1106 ? 8 : 16;
            var zeros = new byte[ramWidth + 1];

            zeros[0] = 0x40;    // data stream control byte

            for (var page = 0; page < ramPages; page++) {
                this.SendCommand((byte)(CMD_SET_PAGE | page));
                this.SendCommand(CMD_SET_LOW_COLUMN);
                this.SendCommand(CMD_SET_HIGH_COLUMN);
                this.i2c.Write(zeros);
            }
        }

        /// <summary>Pushes vram to the panel one page per transaction. SH110x has no horizontal addressing.</summary>
        private void FlushVram() {
            var full = this.forceFullFlush;
            this.forceFullFlush = false;

            for (var page = 0; page < this.pageCount; page++) {
                var shadowStart = page * this.pageWidth;

                if (this.rotate90)
                    this.BuildRotatedPage(page);
                else
                    Array.Copy(this.vram, shadowStart, this.pageBuffer, 1, this.pageWidth);

                if (!full && this.SkipUnchangedPages && this.PageUnchanged(shadowStart))
                    continue;

                Array.Copy(this.pageBuffer, 1, this.shadow, shadowStart, this.pageWidth);

                this.SendCommand((byte)(CMD_SET_PAGE | page));
                this.SendCommand((byte)(CMD_SET_LOW_COLUMN  | (this.columnOffset & 0x0F)));
                this.SendCommand((byte)(CMD_SET_HIGH_COLUMN | ((this.columnOffset >> 4) & 0x0F)));

                this.i2c.Write(this.pageBuffer);
            }
        }

        private bool PageUnchanged(int shadowStart) {
            for (var i = 0; i < this.pageWidth; i++)
                if (this.pageBuffer[i + 1] != this.shadow[shadowStart + i])
                    return false;
            return true;
        }

        /// <summary>
        /// Transposes a landscape frame into the portrait layout an SH1107
        /// 64x128 panel expects. Controller page p covers screen columns
        /// x = p * 8 through p * 8 + 7, and controller column c is screen row y.
        /// </summary>
        private void BuildRotatedPage(int page) {
            var baseX = page * 8;

            for (var c = 0; c < this.pageWidth; c++) {
                var rowStart = (c >> 3) * this.width;
                var mask = (byte)(1 << (c & 7));
                byte value = 0;

                for (var bit = 0; bit < 8; bit++) {
                    if ((this.vram[rowStart + baseX + bit] & mask) != 0)
                        value |= (byte)(1 << bit);
                }

                this.pageBuffer[c + 1] = value;
            }
        }
    }
}
