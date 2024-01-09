using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using GHIElectronics.Endpoint.Core;
using GHIElectronics.Endpoint.Devices.Dcmi;
using Microsoft.VisualBasic;
using static GHIElectronics.Endpoint.Core.EPM815;

namespace GHIElectronics.Endpoint.Drivers.Omnivision.OV5640 {
    public class OV5640Controller : DcmiController {
        int resetPin;
        int pwdPin;

        GpioController gpioResetController;
        GpioController gpioPwdController;
       
       
        public OV5640Controller(DcmiSetting setting, int i2cController, int resetPin, int pwdPin) : base(setting) {
            if (i2cController != EPM815.I2c.I2c6) {
                throw new Exception("Support I2c6 only");
            }

            if (resetPin != Gpio.Pin.NONE && Gpio.IsPinReserved(resetPin)) {
                throw new Exception($"{resetPin} is already in used");
            }

            if (pwdPin != Gpio.Pin.NONE && Gpio.IsPinReserved(pwdPin)) {
                throw new Exception($"{pwdPin} is already in used");
            }

            
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

            this.Reset();
                       
            var script_insmod = new Script("modprobe", "/.", $"ov5640");
            script_insmod.Start();

            Thread.Sleep(100); // wait for ov5640 init

            this.Open();
        }

        private void Reset() {
            if (this.resetPin == Gpio.Pin.NONE)
                return;

            this.gpioResetController.Write(Gpio.GetPin(this.resetPin), false);
            Thread.Sleep(20);
            this.gpioResetController.Write(Gpio.GetPin(this.resetPin), true);
            Thread.Sleep(100);
        }

        public void SetPowerDown(bool enable) {
            if (this.pwdPin == Gpio.Pin.NONE)
                return;

            this.gpioPwdController.Write(Gpio.GetPin(this.pwdPin), enable);
        }
    }
}
