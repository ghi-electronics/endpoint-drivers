// HiLetgo ILI9341 SPI TFT driver for GHI Endpoint. Inherits from
// GHIElectronics.Endpoint.Devices.Display.DisplayController so applications
// use the same open / Enable / Flush pattern as the built-in parallel panel.
// Chip-agnostic: the caller passes in an already-opened SpiDevice plus a
// GpioController constructed with the right chip's Driver (EPM815.Gpio.Driver
// or EPN256.Gpio.Driver) - this file never does /16 / %16 math.
//
//     using var gpio = new GpioController(new EPDomino24.Gpio.Driver());
//     using var spi  = SpiDevice.Create(ILI9341Controller.GetConnectionSettings(EPDomino24.Spi.Spi1));
//     using var tft  = new ILI9341Controller(
//         spi:    spi,
//         cs:     EPDomino24.Gpio.Pin.PA14,
//         dc:     EPDomino24.Gpio.Pin.PA10,
//         reset:  EPDomino24.Gpio.Pin.PF4,
//         gpio:   gpio);
//     tft.Enable();
//     tft.Flush(rgb565Bytes);

using System;
using System.Device.Gpio;
using System.Device.Spi;
using System.Threading;

using GHIElectronics.Endpoint.Devices.Display;

// Alias to the same System.Drawing.Imaging.PixelFormat type the base class uses.
using PixelFormat = System.Drawing.Imaging.PixelFormat;

#pragma warning disable CA1416  // System.Drawing.Imaging.PixelFormat is Windows-annotated; we only use the int values.

namespace GHIElectronics.Endpoint.Drivers.HiLetgo.ILI9341 {
    public enum ILI9341CommandId : byte {
        SWRESET  = 0x01,
        SLPOUT   = 0x11,
        INVOFF   = 0x20,
        INVON    = 0x21,
        GAMMASET = 0x26,
        DISPOFF  = 0x28,
        DISPON   = 0x29,
        CASET    = 0x2A,
        PASET    = 0x2B,
        RAMWR    = 0x2C,
        MADCTL   = 0x36,
        PIXFMT   = 0x3A,
        FRMCTR1  = 0xB1,
        DFUNCTR  = 0xB6,
        EMSET    = 0xB7,
    }

    /// <summary>
    /// HiLetgo ILI9341 240x320 (or 320x240 rotated) SPI TFT. Subclass of
    /// <see cref="DisplayController"/>; call <see cref="Enable"/> after
    /// construction, then <see cref="DisplayController.Flush(ReadOnlySpan{byte})"/>
    /// to push RGB565 pixels.
    /// </summary>
    public class ILI9341Controller : DisplayController {
        private readonly byte[] buffer1 = new byte[1];
        private readonly byte[] buffer4 = new byte[4];

        private readonly SpiDevice spi;
        private readonly GpioController gpio;
        private readonly int chipSelectPin;
        private readonly int dataControlPin;
        private readonly int resetPin;
        private readonly int spiBufferSize;

        private bool rowColumnSwapped;
        private bool isEnabled;

        public override int Width  => this.rowColumnSwapped ? 320 : 240;
        public override int Height => this.rowColumnSwapped ? 240 : 320;
        public override bool IsEnabled => this.isEnabled;

        /// <summary>Default SPI settings for the ILI9341: Mode 0, 12 MHz, CS line 0.</summary>
        public static SpiConnectionSettings GetConnectionSettings(int busId) => new(busId, chipSelectLine: 0) {
            Mode = SpiMode.Mode0,
            ClockFrequency = 12_000_000,
        };

        /// <summary>
        /// Open the display. <paramref name="spi"/> and <paramref name="gpio"/>
        /// are owned by the caller; this driver opens the CS / DC / reset pins
        /// and releases them on <see cref="Dispose"/>. Pass <c>-1</c> for
        /// <paramref name="reset"/> if the panel has no reset pin wired.
        /// </summary>
        public ILI9341Controller(SpiDevice spi, int cs, int dc, int reset, GpioController gpio) {
            this.spi = spi ?? throw new ArgumentNullException(nameof(spi));
            this.gpio = gpio ?? throw new ArgumentNullException(nameof(gpio));
            this.chipSelectPin = cs;
            this.dataControlPin = dc;
            this.resetPin = reset;

            // 4 KiB SPI chunk matches ST7735; keeps row writes responsive on
            // kernels that cap a single spi_sync() transfer.
            this.spiBufferSize = 4096;

            this.gpio.OpenPin(this.chipSelectPin, PinMode.Output);
            this.gpio.OpenPin(this.dataControlPin, PinMode.Output);
            if (this.resetPin >= 0) this.gpio.OpenPin(this.resetPin, PinMode.Output);

            this.gpio.Write(this.chipSelectPin, PinValue.High);
        }

        /// <summary>Convenience 3-pin constructor (no reset line).</summary>
        public ILI9341Controller(SpiDevice spi, int cs, int dc, GpioController gpio)
            : this(spi, cs, dc, -1, gpio) { }

        public override void Enable() {
            this.Reset();
            this.Initialize();
            this.SetDataAccessControl(swapRowColumn: false, invertRow: false, invertColumn: false, useBgrPanel: true);
            this.SetDrawWindow(0, 0, this.Width - 1, this.Height - 1);
            this.SendCommand(ILI9341CommandId.DISPON);
            this.isEnabled = true;
        }

        public override void Disable() {
            if (!this.isEnabled) return;
            this.SendCommand(ILI9341CommandId.DISPOFF);
            this.isEnabled = false;
        }

        public override void Flush(ReadOnlySpan<byte> pixels, PixelFormat format,
                                   int x, int y, int width, int height) {
            if (format != PixelFormat.Format16bppRgb565) {
                throw new NotSupportedException("ILI9341Controller only supports Format16bppRgb565 today.");
            }
            this.SetDrawWindow(x, y, x + width - 1, y + height - 1);
            this.SendCommand(ILI9341CommandId.RAMWR);
            this.gpio.Write(this.dataControlPin, PinValue.High);
            this.SpiWrite(pixels);
        }

        public override void Dispose() {
            try { this.Disable(); } catch { }
            try { this.gpio.ClosePin(this.chipSelectPin); } catch { }
            try { this.gpio.ClosePin(this.dataControlPin); } catch { }
            if (this.resetPin >= 0) { try { this.gpio.ClosePin(this.resetPin); } catch { } }
            try { this.spi.Dispose(); } catch { }
        }

        private void Reset() {
            if (this.resetPin < 0) return;
            this.gpio.Write(this.resetPin, PinValue.Low);
            Thread.Sleep(50);
            this.gpio.Write(this.resetPin, PinValue.High);
            Thread.Sleep(200);
        }

        private void Initialize() {
            this.SendCommand(ILI9341CommandId.SWRESET);
            Thread.Sleep(10);

            this.SendCommand(ILI9341CommandId.DISPOFF);

            this.SendCommand(ILI9341CommandId.MADCTL);
            this.SendData(0x08 | 0x40);

            this.SendCommand(ILI9341CommandId.PIXFMT);
            this.SendData(0x55);                     // 16 bpp / 565

            this.SendCommand(ILI9341CommandId.FRMCTR1);
            this.SendData(0x00); this.SendData(0x1B);

            this.SendCommand(ILI9341CommandId.GAMMASET);
            this.SendData(0x01);

            this.SendCommand(ILI9341CommandId.EMSET);
            this.SendData(0x07);

            this.SendCommand(ILI9341CommandId.DFUNCTR);
            this.SendData(0x0A); this.SendData(0x82); this.SendData(0x27); this.SendData(0x00);

            this.SendCommand(ILI9341CommandId.SLPOUT);
            Thread.Sleep(120);
        }

        public void SetDataAccessControl(bool swapRowColumn, bool invertRow, bool invertColumn, bool useBgrPanel) {
            var val = (byte)0;
            if (useBgrPanel)  val |= 0b0000_1000;
            if (swapRowColumn) val |= 0b0010_0000;
            if (invertColumn) val |= 0b0100_0000;
            if (invertRow)    val |= 0b1000_0000;
            this.SendCommand(ILI9341CommandId.MADCTL);
            this.SendData(val);
            this.rowColumnSwapped = swapRowColumn;
        }

        private void SetDrawWindow(int x, int y, int xEnd, int yEnd) {
            this.buffer4[0] = (byte)(x >> 8);    this.buffer4[1] = (byte)x;
            this.buffer4[2] = (byte)(xEnd >> 8); this.buffer4[3] = (byte)xEnd;
            this.SendCommand(ILI9341CommandId.CASET);
            this.SendData(this.buffer4);

            this.buffer4[0] = (byte)(y >> 8);    this.buffer4[1] = (byte)y;
            this.buffer4[2] = (byte)(yEnd >> 8); this.buffer4[3] = (byte)yEnd;
            this.SendCommand(ILI9341CommandId.PASET);
            this.SendData(this.buffer4);
        }

        private void SendCommand(ILI9341CommandId command) {
            this.buffer1[0] = (byte)command;
            this.gpio.Write(this.dataControlPin, PinValue.Low);
            this.SpiWrite(this.buffer1);
        }

        private void SendData(byte data) {
            this.buffer1[0] = data;
            this.gpio.Write(this.dataControlPin, PinValue.High);
            this.SpiWrite(this.buffer1);
        }

        private void SendData(byte[] data) {
            this.gpio.Write(this.dataControlPin, PinValue.High);
            this.SpiWrite(data);
        }

        private void SpiWrite(ReadOnlySpan<byte> buffer) {
            this.gpio.Write(this.chipSelectPin, PinValue.Low);
            try {
                int index = 0, remaining = buffer.Length;
                while (remaining > 0) {
                    int chunk = Math.Min(remaining, this.spiBufferSize);
                    this.spi.Write(buffer.Slice(index, chunk));
                    index += chunk;
                    remaining -= chunk;
                }
            }
            finally {
                this.gpio.Write(this.chipSelectPin, PinValue.High);
            }
        }
    }
}
