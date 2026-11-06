// Sitronix ST7735 SPI TFT driver for GHI Endpoint. Inherits from
// GHIElectronics.Endpoint.Devices.Display.DisplayController so applications
// use the same open / Enable / Flush pattern as the built-in parallel panel.
// Chip-agnostic: the caller passes in an already-opened SpiDevice plus a
// GpioController constructed with the right chip's Driver (EPM815.Gpio.Driver
// or EPN256.Gpio.Driver) — this file never does /16 / %16 math.
//
//     using var gpio = new GpioController(new EPM815.Gpio.Driver());
//     using var spi  = SpiDevice.Create(ST7735Controller.GetConnectionSettings(EPM815.Spi.Spi1));
//     using var tft  = new ST7735Controller(
//         spi:    spi,
//         cs:     EPM815.Gpio.Pin.PA14,
//         dc:     EPM815.Gpio.Pin.PA10,
//         reset:  EPM815.Gpio.Pin.PF4,
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

namespace GHIElectronics.Endpoint.Drivers.Sitronix.ST7735 {
    public enum ST7735CommandId : byte {
        // System
        NOP     = 0x00, SWRESET = 0x01, RDDID = 0x04, RDDST = 0x09,
        RDDPM   = 0x0A, RDDMADCTL = 0x0B, RDDCOLMOD = 0x0C, RDDIM = 0x0D, RDDSM = 0x0E,
        SLPIN   = 0x10, SLPOUT = 0x11, PTLON = 0x12, NORON = 0x13,
        INVOFF  = 0x20, INVON = 0x21, GAMSET = 0x26, DISPOFF = 0x28, DISPON = 0x29,
        CASET   = 0x2A, RASET = 0x2B, RAMWR = 0x2C, RAMRD = 0x2E,
        PTLAR   = 0x30, TEOFF = 0x34, TEON = 0x35, MADCTL = 0x36,
        IDMOFF  = 0x38, IDMON = 0x39, COLMOD = 0x3A,
        RDID1   = 0xDA, RDID2 = 0xDB, RDID3 = 0xDC,

        // Panel
        FRMCTR1 = 0xB1, FRMCTR2 = 0xB2, FRMCTR3 = 0xB3,
        INVCTR  = 0xB4, DISSET5 = 0xB6,
        PWCTR1  = 0xC0, PWCTR2 = 0xC1, PWCTR3 = 0xC2, PWCTR4 = 0xC3, PWCTR5 = 0xC4,
        VMCTR1  = 0xC5, VMOFCTR = 0xC7,
        WRID2   = 0xD1, WRID3 = 0xD2,
        NVCTR1  = 0xD9, NVCTR2 = 0xDE, NVCTR3 = 0xDF,
        GAMCTRP1 = 0xE0, GAMCTRN1 = 0xE1,
    }

    public enum DataFormat {
        Rgb565 = 0,
        Rgb444 = 1
    }

    /// <summary>
    /// Sitronix ST7735 128x160 (or 160x128 rotated) SPI TFT. Subclass of
    /// <see cref="DisplayController"/>; call <see cref="Enable"/> after
    /// construction, then <see cref="DisplayController.Flush(ReadOnlySpan{byte})"/>
    /// to push RGB565 pixels.
    /// </summary>
    public class ST7735Controller : DisplayController {
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

        public DataFormat DataFormat { get; private set; } = DataFormat.Rgb565;

        public override int Width  => this.rowColumnSwapped ? 160 : 128;
        public override int Height => this.rowColumnSwapped ? 128 : 160;
        public override bool IsEnabled => this.isEnabled;

        /// <summary>Default SPI settings for the ST7735: Mode 3, 8 MHz, CS line 0.</summary>
        public static SpiConnectionSettings GetConnectionSettings(int busId) => new(busId, chipSelectLine: 0) {
            Mode = SpiMode.Mode3,
            ClockFrequency = 8_000_000,
        };

        /// <summary>
        /// Open the display. <paramref name="spi"/> and <paramref name="gpio"/>
        /// are owned by the caller; this driver opens the CS / DC / reset pins
        /// and releases them on <see cref="Dispose"/>. Pass <c>-1</c> for
        /// <paramref name="reset"/> if the panel has no reset pin wired.
        /// </summary>
        public ST7735Controller(SpiDevice spi, int cs, int dc, int reset, GpioController gpio) {
            this.spi = spi ?? throw new ArgumentNullException(nameof(spi));
            this.gpio = gpio ?? throw new ArgumentNullException(nameof(gpio));
            this.chipSelectPin = cs;
            this.dataControlPin = dc;
            this.resetPin = reset;

            // SpiBusInfo.BufferSize isn't exposed on all .NET versions; pick a
            // sane 4 KiB chunk size for the row writes.
            this.spiBufferSize = 4096;

            this.gpio.OpenPin(this.chipSelectPin, PinMode.Output);
            this.gpio.OpenPin(this.dataControlPin, PinMode.Output);
            if (this.resetPin >= 0) this.gpio.OpenPin(this.resetPin, PinMode.Output);

            this.gpio.Write(this.chipSelectPin, PinValue.High);
        }

        /// <summary>Convenience 3-pin constructor (no reset line).</summary>
        public ST7735Controller(SpiDevice spi, int cs, int dc, GpioController gpio)
            : this(spi, cs, dc, -1, gpio) { }

        public override void Enable() {
            this.Reset();
            this.Initialize();
            this.SetDataFormat(DataFormat.Rgb565);
            this.SetDataAccessControl(swapRowColumn: true, invertRow: false, invertColumn: false, useBgrPanel: false);
            this.SetDrawWindow(0, 0, this.Width - 1, this.Height - 1);
            this.SendCommand(ST7735CommandId.DISPON);
            this.isEnabled = true;
        }

        public override void Disable() {
            if (!this.isEnabled) return;
            this.SendCommand(ST7735CommandId.DISPOFF);
            this.isEnabled = false;
        }

        public override void Flush(ReadOnlySpan<byte> pixels, PixelFormat format,
                                   int x, int y, int width, int height) {
            if (format != PixelFormat.Format16bppRgb565) {
                throw new NotSupportedException("ST7735Controller only supports Format16bppRgb565 today.");
            }
            this.SetDrawWindow(x, y, x + width - 1, y + height - 1);
            this.SendCommand(ST7735CommandId.RAMWR);
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
            for (var i = 0; i < 2; i++) {
                this.SendCommand(ST7735CommandId.SWRESET);
                Thread.Sleep(120);

                this.SendCommand(ST7735CommandId.SLPOUT);
                Thread.Sleep(120);

                this.SendCommand(ST7735CommandId.FRMCTR1);
                this.SendData(0x01); this.SendData(0x2C); this.SendData(0x2D);
                this.SendCommand(ST7735CommandId.FRMCTR2);
                this.SendData(0x01); this.SendData(0x2C); this.SendData(0x2D);
                this.SendCommand(ST7735CommandId.FRMCTR3);
                this.SendData(0x01); this.SendData(0x2C); this.SendData(0x2D);
                this.SendData(0x01); this.SendData(0x2C); this.SendData(0x2D);

                this.SendCommand(ST7735CommandId.INVCTR);
                this.SendData(0x07);

                this.SendCommand(ST7735CommandId.PWCTR1);
                this.SendData(0xA2); this.SendData(0x02); this.SendData(0x84);
                this.SendCommand(ST7735CommandId.PWCTR2);
                this.SendData(0xC5);
                this.SendCommand(ST7735CommandId.PWCTR3);
                this.SendData(0x0A); this.SendData(0x00);
                this.SendCommand(ST7735CommandId.PWCTR4);
                this.SendData(0x8A); this.SendData(0x2A);
                this.SendCommand(ST7735CommandId.PWCTR5);
                this.SendData(0x8A); this.SendData(0xEE);

                this.SendCommand(ST7735CommandId.VMCTR1);
                this.SendData(0x0E);

                this.SendCommand(ST7735CommandId.GAMCTRP1);
                this.SendData(0x0F); this.SendData(0x1A); this.SendData(0x0F); this.SendData(0x18);
                this.SendData(0x2F); this.SendData(0x28); this.SendData(0x20); this.SendData(0x22);
                this.SendData(0x1F); this.SendData(0x1B); this.SendData(0x23); this.SendData(0x37);
                this.SendData(0x00); this.SendData(0x07); this.SendData(0x02); this.SendData(0x10);

                this.SendCommand(ST7735CommandId.GAMCTRN1);
                this.SendData(0x0F); this.SendData(0x1B); this.SendData(0x0F); this.SendData(0x17);
                this.SendData(0x33); this.SendData(0x2C); this.SendData(0x29); this.SendData(0x2E);
                this.SendData(0x30); this.SendData(0x30); this.SendData(0x39); this.SendData(0x3F);
                this.SendData(0x00); this.SendData(0x07); this.SendData(0x03); this.SendData(0x10);

                Thread.Sleep(10);
            }
        }

        public void SetDataAccessControl(bool swapRowColumn, bool invertRow, bool invertColumn, bool useBgrPanel) {
            var val = (byte)0;
            if (useBgrPanel)  val |= 0b0000_1000;
            if (swapRowColumn) val |= 0b0010_0000;
            if (invertColumn) val |= 0b0100_0000;
            if (invertRow)    val |= 0b1000_0000;
            this.SendCommand(ST7735CommandId.MADCTL);
            this.SendData(val);
            this.rowColumnSwapped = swapRowColumn;
        }

        public void SetDataFormat(DataFormat dataFormat) {
            this.SendCommand(ST7735CommandId.COLMOD);
            this.SendData((byte)(dataFormat switch {
                DataFormat.Rgb444 => 0x03,
                DataFormat.Rgb565 => 0x05,
                _ => throw new NotSupportedException(),
            }));
            this.DataFormat = dataFormat;
        }

        private void SetDrawWindow(int x, int y, int xEnd, int yEnd) {
            this.buffer4[0] = 0; this.buffer4[1] = (byte)x;
            this.buffer4[2] = 0; this.buffer4[3] = (byte)xEnd;
            this.SendCommand(ST7735CommandId.CASET);
            this.SendData(this.buffer4);

            this.buffer4[0] = 0; this.buffer4[1] = (byte)y;
            this.buffer4[2] = 0; this.buffer4[3] = (byte)yEnd;
            this.SendCommand(ST7735CommandId.RASET);
            this.SendData(this.buffer4);
        }

        private void SendCommand(ST7735CommandId command) {
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
