using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using System.Device.Spi;
using GHIElectronics.Endpoint.Devices.Display;
using Iot.Device.Nmea0183.Sentences;
using UnitsNet;

namespace GHIElectronics.TinyCLR.Drivers.Sitronix.ST7735 {
    public enum ST7735CommandId : byte {
        //System
        NOP = 0x00,
        SWRESET = 0x01,
        RDDID = 0x04,
        RDDST = 0x09,
        RDDPM = 0x0A,
        RDDMADCTL = 0x0B,
        RDDCOLMOD = 0x0C,
        RDDIM = 0x0D,
        RDDSM = 0x0E,
        SLPIN = 0x10,
        SLPOUT = 0x11,
        PTLON = 0x12,
        NORON = 0x13,
        INVOFF = 0x20,
        INVON = 0x21,
        GAMSET = 0x26,
        DISPOFF = 0x28,
        DISPON = 0x29,
        CASET = 0x2A,
        RASET = 0x2B,
        RAMWR = 0x2C,
        RAMRD = 0x2E,
        PTLAR = 0x30,
        TEOFF = 0x34,
        TEON = 0x35,
        MADCTL = 0x36,
        IDMOFF = 0x38,
        IDMON = 0x39,
        COLMOD = 0x3A,
        RDID1 = 0xDA,
        RDID2 = 0xDB,
        RDID3 = 0xDC,

        //Panel
        FRMCTR1 = 0xB1,
        FRMCTR2 = 0xB2,
        FRMCTR3 = 0xB3,
        INVCTR = 0xB4,
        DISSET5 = 0xB6,
        PWCTR1 = 0xC0,
        PWCTR2 = 0xC1,
        PWCTR3 = 0xC2,
        PWCTR4 = 0xC3,
        PWCTR5 = 0xC4,
        VMCTR1 = 0xC5,
        VMOFCTR = 0xC7,
        WRID2 = 0xD1,
        WRID3 = 0xD2,
        NVCTR1 = 0xD9,
        NVCTR2 = 0xDE,
        NVCTR3 = 0xDF,
        GAMCTRP1 = 0xE0,
        GAMCTRN1 = 0xE1,
    }

    public enum DataFormat {
        Rgb565 = 0,
        Rgb444 = 1
    }


    public class ST7735Controller : IDisplayProvider {
        private readonly byte[] buffer1 = new byte[1];
        private readonly byte[] buffer4 = new byte[4];

        private int chipselect;
        private int dataControl;
        private int reset;

        GpioController gpioChipselect;
        GpioController gpioReset;
        GpioController gpioDataControl;

        SpiDevice spi;
        private int bufferSize = 4096;

        private int bpp;
        private bool rowColumnSwapped;

        public DataFormat DataFormat { get; private set; }

        public int Width => this.rowColumnSwapped ? 160 : 128;
        public int Height => this.rowColumnSwapped ? 128 : 160;

        public DisplayConfiguration Configuration => throw new NotImplementedException();

        public static SpiConnectionSettings GetConnectionSettings(int busId) => new SpiConnectionSettings(busId) {
            Mode = SpiMode.Mode3,
            ClockFrequency = 8_000_000,
            BusId = busId,
            ChipSelectLine = 0
        };

        public ST7735Controller(SpiDevice spi, int cs, int control) : this(spi, cs, control, -1) {

        }


        public ST7735Controller(SpiDevice spi, int cs, int control, int reset) {
            this.chipselect = cs;
            this.dataControl = control;
            this.reset = reset;

            this.gpioChipselect = new GpioController(PinNumberingScheme.Logical, new LibGpiodDriver(this.chipselect / 16)); this.chipselect = this.chipselect % 16;
            this.gpioDataControl = new GpioController(PinNumberingScheme.Logical, new LibGpiodDriver(this.dataControl / 16)); this.dataControl = this.dataControl % 16;
            this.gpioReset = new GpioController(PinNumberingScheme.Logical, new LibGpiodDriver(this.reset / 16)); this.reset = this.reset % 16;


            this.spi = spi;

            this.gpioChipselect.OpenPin(this.chipselect);
            this.gpioChipselect.SetPinMode(this.chipselect, PinMode.Output);

            this.gpioDataControl.OpenPin(this.dataControl);
            this.gpioDataControl.SetPinMode(this.dataControl, PinMode.Output);

            this.gpioReset.OpenPin(this.reset);
            this.gpioReset.SetPinMode(this.reset, PinMode.Output);

            this.gpioChipselect.Write(this.chipselect, PinValue.High);


            this.bufferSize = (SpiBusInfo.BufferSize / 4096) * 4096;

            this.Reset();
            this.Initialize();
            this.SetDataFormat(DataFormat.Rgb565);
            this.SetDataAccessControl(true, false, false, false);
            this.SetDrawWindow(0, 0, this.Width - 1, this.Height - 1);
        }

        private void Reset() {
            if (this.reset < 0)
                return;

            this.gpioReset.Write(this.reset, PinValue.Low);
            Thread.Sleep(50);

            this.gpioReset.Write(this.reset, PinValue.High);
            Thread.Sleep(200);
        }

        private void Initialize() {
            for (var i = 0; i < 2; i++) {
                this.SendCommand(ST7735CommandId.SWRESET);
                Thread.Sleep(120);

                this.SendCommand(ST7735CommandId.SLPOUT);
                Thread.Sleep(120);

                this.SendCommand(ST7735CommandId.FRMCTR1);
                this.SendData(0x01);
                this.SendData(0x2C);
                this.SendData(0x2D);

                this.SendCommand(ST7735CommandId.FRMCTR2);
                this.SendData(0x01);
                this.SendData(0x2C);
                this.SendData(0x2D);

                this.SendCommand(ST7735CommandId.FRMCTR3);
                this.SendData(0x01);
                this.SendData(0x2C);
                this.SendData(0x2D);
                this.SendData(0x01);
                this.SendData(0x2C);
                this.SendData(0x2D);

                this.SendCommand(ST7735CommandId.INVCTR);
                this.SendData(0x07);

                this.SendCommand(ST7735CommandId.PWCTR1);
                this.SendData(0xA2);
                this.SendData(0x02);
                this.SendData(0x84);

                this.SendCommand(ST7735CommandId.PWCTR2);
                this.SendData(0xC5);

                this.SendCommand(ST7735CommandId.PWCTR3);
                this.SendData(0x0A);
                this.SendData(0x00);

                this.SendCommand(ST7735CommandId.PWCTR4);
                this.SendData(0x8A);
                this.SendData(0x2A);

                this.SendCommand(ST7735CommandId.PWCTR5);
                this.SendData(0x8A);
                this.SendData(0xEE);

                this.SendCommand(ST7735CommandId.VMCTR1);
                this.SendData(0x0E);

                this.SendCommand(ST7735CommandId.GAMCTRP1);
                this.SendData(0x0F);
                this.SendData(0x1A);
                this.SendData(0x0F);
                this.SendData(0x18);
                this.SendData(0x2F);
                this.SendData(0x28);
                this.SendData(0x20);
                this.SendData(0x22);
                this.SendData(0x1F);
                this.SendData(0x1B);
                this.SendData(0x23);
                this.SendData(0x37);
                this.SendData(0x00);
                this.SendData(0x07);
                this.SendData(0x02);
                this.SendData(0x10);

                this.SendCommand(ST7735CommandId.GAMCTRN1);
                this.SendData(0x0F);
                this.SendData(0x1B);
                this.SendData(0x0F);
                this.SendData(0x17);
                this.SendData(0x33);
                this.SendData(0x2C);
                this.SendData(0x29);
                this.SendData(0x2E);
                this.SendData(0x30);
                this.SendData(0x30);
                this.SendData(0x39);
                this.SendData(0x3F);
                this.SendData(0x00);
                this.SendData(0x07);
                this.SendData(0x03);
                this.SendData(0x10);

                Thread.Sleep(10);
            }
        }

        public void Dispose() {
            this.spi.Dispose();

            this.gpioChipselect.ClosePin(this.chipselect);
            this.gpioChipselect?.Dispose();

            this.gpioDataControl.ClosePin(this.dataControl);
            this.gpioDataControl?.Dispose();

            this.gpioReset.ClosePin(this.reset);
            this.gpioReset?.Dispose();
        }

        public void Enable() => this.SendCommand(ST7735CommandId.DISPON);
        public void Disable() => this.SendCommand(ST7735CommandId.DISPOFF);

        private void SendCommand(ST7735CommandId command) {
            this.buffer1[0] = (byte)command;
            this.gpioDataControl.Write(this.dataControl, PinValue.Low);
            this.SpiWrite(this.buffer1);
        }

        private void SendData(byte data) {
            this.buffer1[0] = data;
            this.gpioDataControl.Write(this.dataControl, PinValue.High);
            this.SpiWrite(this.buffer1);
        }

        private void SendData(byte[] data) {
            this.gpioDataControl.Write(this.dataControl, PinValue.High);
            this.SpiWrite(data);
        }

        public void SetDataAccessControl(bool swapRowColumn, bool invertRow, bool invertColumn, bool useBgrPanel) {
            var val = default(byte);

            if (useBgrPanel) val |= 0b0000_1000;
            if (swapRowColumn) val |= 0b0010_0000;
            if (invertColumn) val |= 0b0100_0000;
            if (invertRow) val |= 0b1000_0000;

            this.SendCommand(ST7735CommandId.MADCTL);
            this.SendData(val);

            this.rowColumnSwapped = swapRowColumn;
        }

        public void SetDataFormat(DataFormat dataFormat) {
            switch (dataFormat) {
                case DataFormat.Rgb444:
                    this.bpp = 12;
                    this.SendCommand(ST7735CommandId.COLMOD);
                    this.SendData(0x03);

                    break;

                case DataFormat.Rgb565:
                    this.bpp = 16;
                    this.SendCommand(ST7735CommandId.COLMOD);
                    this.SendData(0x05);

                    break;

                default:
                    throw new NotSupportedException();
            }

            this.DataFormat = dataFormat;
        }

        public void SetDrawWindow(int x, int y, int width, int height) {

            this.buffer4[1] = (byte)x;
            this.buffer4[3] = (byte)(x + width);
            this.SendCommand(ST7735CommandId.CASET);
            this.SendData(this.buffer4);

            this.buffer4[1] = (byte)y;
            this.buffer4[3] = (byte)(y + height);
            this.SendCommand(ST7735CommandId.RASET);
            this.SendData(this.buffer4);
        }

        private void SendDrawCommand() {
            this.SendCommand(ST7735CommandId.RAMWR);
            this.gpioDataControl.Write(this.dataControl, PinValue.High);
        }

        private void SpiWrite(byte[] buffer) => this.SpiWrite(buffer, 0, buffer.Length);
        private void SpiWrite(Span<byte> buffer, int offset, int count) {


            var index = offset;
            int len;

            this.gpioChipselect.Write(this.chipselect, PinValue.Low);

            do {
                // calculate the amount of spi data to send in this chunk
                len = Math.Min(count, this.bufferSize);
                // send the slice of data off set by the index and of length len.
                this.spi.Write(buffer.Slice(index, len));
                // add the length just sent to the index
                index += len;
                count -= len;
            }
            while (count > 0); // repeat until all data sent.

            this.gpioChipselect.Write(this.chipselect, PinValue.High);
        }

        public void Flush(byte[] data, int offset, int length) => this.Flush(data, offset, length, this.Width, this.Height);
        public void Flush(byte[] data, int offset, int length, int width, int height) {
            this.SendDrawCommand();
            this.SpiWrite(data, offset, length);
        }


    }
}
