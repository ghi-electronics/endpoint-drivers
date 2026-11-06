// Solomon Systech SSD1306 I2C 128x64 OLED driver for GHI Endpoint. Inherits
// from GHIElectronics.Endpoint.Devices.Display.DisplayController so
// applications use the same open / Enable / Flush pattern as the built-in
// parallel panel. The OLED is monochrome; the Flush override accepts an
// RGB565 source and thresholds any non-black pixel to "lit".
//
//     using var i2c = I2cDevice.Create(SSD1306Controller.GetConnectionSettings(EPDomino24.I2c.I2c1));
//     using var oled = new SSD1306Controller(i2c);
//     oled.Enable();
//     oled.Flush(rgb565Bytes);   // 128 * 64 * 2 bytes

using System;
using System.Device.I2c;

using GHIElectronics.Endpoint.Devices.Display;

// Alias to the same System.Drawing.Imaging.PixelFormat type the base class uses.
using PixelFormat = System.Drawing.Imaging.PixelFormat;

#pragma warning disable CA1416  // System.Drawing.Imaging.PixelFormat is Windows-annotated; we only use the int values.

namespace GHIElectronics.Endpoint.Drivers.SolomonSystech.SSD1306 {
    /// <summary>
    /// Solomon Systech SSD1306 128x64 monochrome I2C OLED. Subclass of
    /// <see cref="DisplayController"/>; call <see cref="Enable"/> after
    /// construction, then <see cref="DisplayController.Flush(ReadOnlySpan{byte})"/>
    /// to push pixels (non-black = lit).
    /// </summary>
    public class SSD1306Controller : DisplayController {
        private const int PanelWidth  = 128;
        private const int PanelHeight = 64;
        private const int Pages       = PanelHeight / 8;
        private const int VramBytes   = PanelWidth * Pages;      // 1024

        // The I2C control byte (0x40) prefixes the pixel payload for every
        // data burst - we keep it as vram[0] so we can push the whole frame
        // in a single Write().
        private readonly byte[] vram = new byte[VramBytes + 1];
        private readonly byte[] command2 = new byte[2];

        private readonly I2cDevice i2c;
        private bool isEnabled;

        public override int Width  => PanelWidth;
        public override int Height => PanelHeight;
        public override bool IsEnabled => this.isEnabled;

        /// <summary>Default I2C settings for the SSD1306: 7-bit address 0x3C. Bus speed is set per I2C bus at the kernel level.</summary>
        public static I2cConnectionSettings GetConnectionSettings(int busId) =>
            new(busId, deviceAddress: 0x3C);

        /// <summary>
        /// Open the display. <paramref name="i2c"/> is owned by the caller
        /// and released on <see cref="Dispose"/>.
        /// </summary>
        public SSD1306Controller(I2cDevice i2c) {
            this.i2c = i2c ?? throw new ArgumentNullException(nameof(i2c));
            this.vram[0] = 0x40;    // Co=0, D/C#=1 (data)
        }

        public override void Enable() {
            this.Initialize();
            this.isEnabled = true;
        }

        public override void Disable() {
            if (!this.isEnabled) return;
            this.SendCommand(0xAE);                // display off
            this.isEnabled = false;
        }

        /// <summary>
        /// Flush a region of the panel. Source must be
        /// <see cref="PixelFormat.Format16bppRgb565"/>; any non-zero source
        /// pixel lights the corresponding OLED cell. The whole VRAM is sent
        /// on every call, even for a partial region.
        /// </summary>
        public override void Flush(ReadOnlySpan<byte> pixels, PixelFormat format,
                                   int x, int y, int width, int height) {
            if (format != PixelFormat.Format16bppRgb565) {
                throw new NotSupportedException("SSD1306Controller only supports Format16bppRgb565 today.");
            }
            if (x < 0 || y < 0 || width <= 0 || height <= 0
                || x + width > PanelWidth || y + height > PanelHeight) {
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
                    int vIndex = 1 + page * PanelWidth + (x + col);
                    if (px != 0) this.vram[vIndex] |= mask;
                    else         this.vram[vIndex] = (byte)(this.vram[vIndex] & ~mask);
                }
            }

            this.i2c.Write(this.vram);
        }

        /// <summary>
        /// Push a pre-packed VRAM blob (SSD1306 native page/column format,
        /// <c>Width * Height / 8</c> bytes). Faster than the RGB565 path when
        /// the caller already has the panel's native layout.
        /// </summary>
        public void DrawBufferNative(byte[] buffer) => this.DrawBufferNative(buffer, 0, buffer.Length);

        public void DrawBufferNative(byte[] buffer, int offset, int count) {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (count > VramBytes) throw new ArgumentException("Buffer exceeds panel VRAM");
            Array.Copy(buffer, offset, this.vram, 1, count);
            this.i2c.Write(this.vram);
        }

        public override void Dispose() {
            try { this.Disable(); } catch { }
            try { this.i2c.Dispose(); } catch { }
        }

        private void Initialize() {
            this.SendCommand(0xAE);    // display off
            this.SendCommand(0x00);    // low column address
            this.SendCommand(0x10);    // high column address
            this.SendCommand(0x40);    // start line address
            this.SendCommand(0x81); this.SendCommand(0xCF);    // contrast
            this.SendCommand(0xA1);    // segment re-map 127 -> 0
            this.SendCommand(0xA6);    // normal display (not inverted)
            this.SendCommand(0xA8); this.SendCommand(0x3F);    // multiplex ratio 1/64
            this.SendCommand(0xD3); this.SendCommand(0x00);    // display offset = 0
            this.SendCommand(0xD5); this.SendCommand(0x80);    // clock divide / osc freq
            this.SendCommand(0xD9); this.SendCommand(0xF1);    // pre-charge period
            this.SendCommand(0xDA); this.SendCommand(0x12);    // com pins hw config
            this.SendCommand(0xDB); this.SendCommand(0x40);    // vcomh deselect level
            this.SendCommand(0x8D); this.SendCommand(0x14);    // charge pump enable
            this.SendCommand(0xAF);    // display on
            this.SendCommand(0xC8);    // COM output scan dir remap

            // Addressing: horizontal mode, full-screen window.
            this.SendCommand(0x20); this.SendCommand(0x00);                // horizontal addressing
            this.SendCommand(0x21); this.SendCommand(0); this.SendCommand((byte)(PanelWidth - 1));
            this.SendCommand(0x22); this.SendCommand(0); this.SendCommand((byte)(Pages - 1));
        }

        /// <summary>Invert the display (hardware-level inversion).</summary>
        public void SetColorFormat(bool invert) => this.SendCommand((byte)(invert ? 0xA7 : 0xA6));

        private void SendCommand(byte cmd) {
            this.command2[0] = 0x00;    // Co=0, D/C#=0 (command)
            this.command2[1] = cmd;
            this.i2c.Write(this.command2);
        }
    }
}
