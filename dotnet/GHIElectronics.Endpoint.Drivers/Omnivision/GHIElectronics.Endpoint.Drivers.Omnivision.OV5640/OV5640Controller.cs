using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using System.Device.I2c;
using GHIElectronics.Endpoint.Core;
using GHIElectronics.Endpoint.Devices.Camera;
using GHIElectronics.Endpoint.Devices.Dcmi;
using Microsoft.VisualBasic;
using static GHIElectronics.Endpoint.Core.EPM815;

namespace GHIElectronics.Endpoint.Drivers.Omnivision.OV5640 {
    public class OV5640Controller : DcmiController {
        int resetPin = -1;
        int pwdPin = -1;
        int i2cController;

        GpioController gpioResetController;
        GpioController gpioPwdController;
        I2cDevice i2CDevice;
       
        public OV5640Controller(int i2cController, int resetPin, int pwdPin ) : base() {
            if (i2cController != EPM815.I2c.I2c6) {
                throw new Exception("Support I2c6 only");
            }

            if (resetPin != Gpio.Pin.NONE && Gpio.IsPinReserved(resetPin)) {
                throw new Exception($"{resetPin} is already in used");
            }

            if (pwdPin != Gpio.Pin.NONE && Gpio.IsPinReserved(pwdPin)) {
                throw new Exception($"{pwdPin} is already in used");
            }

            this.i2cController = i2cController; 
            EPM815.I2c.Initialize(i2cController);

            if (resetPin != Gpio.Pin.NONE) {
                this.resetPin = resetPin;

                this.gpioResetController = new GpioController(PinNumberingScheme.Logical, new LibGpiodDriver(Gpio.GetPort(this.resetPin)));
                this.gpioResetController.OpenPin(Gpio.GetPin(this.resetPin), PinMode.Output);

            }

            if (pwdPin != Gpio.Pin.NONE) {
                this.pwdPin = pwdPin;

                this.gpioPwdController = new GpioController(PinNumberingScheme.Logical, new LibGpiodDriver(Gpio.GetPort(this.pwdPin)));
                this.gpioPwdController.OpenPin(Gpio.GetPin(this.pwdPin), PinMode.Output);
            }
            
            this.SetPowerDown(false);

            Thread.Sleep(5);
            this.Reset();

            this.SetPowerDown(false);

            Thread.Sleep(20);

            var script_insmod = new Script("modprobe", "/.", $"ov5640");
            script_insmod.Start();

            Thread.Sleep(100); // wait for ov5640 init

            this.Open();
        }

        public void Reset() {
            if (this.resetPin == Gpio.Pin.NONE)
                return;

            this.gpioResetController.Write(Gpio.GetPin(this.resetPin), false);
            Thread.Sleep(20);
            this.gpioResetController.Write(Gpio.GetPin(this.resetPin), true);
            Thread.Sleep(20);
        }

        public void SetPowerDown(bool enable) {
            if (this.pwdPin == Gpio.Pin.NONE)
                return;

            this.gpioPwdController.Write(Gpio.GetPin(this.pwdPin), enable);
        }

        public void WriteRegister(ushort register, byte value) {
            var data = new byte[3];
            data[0] = (byte)((register >> 0) & 0xFF);
            data[1] = (byte)((register >> 8) & 0xFF);
            data[3] = value;
            if (this.i2CDevice == null) {
                this.i2CDevice = I2cDevice.Create(new I2cConnectionSettings(this.i2cController, 0x3C));
            }
            this.i2CDevice.Write(data);
        }

        public void ReadRegister(ushort register, byte[] value) {
            if (this.i2CDevice == null) {
                this.i2CDevice = I2cDevice.Create(new I2cConnectionSettings(this.i2cController, 0x3C));
            }

            var send = new byte[2];
            send[0] = (byte)((register >> 0) & 0xFF);
            send[1] = (byte)((register >> 8) & 0xFF);
            this.i2CDevice.WriteRead(send, value);
        }
    }
}
