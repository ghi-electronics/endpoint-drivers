using GHIElectronics.Endpoint.Devices.I2c;
using static GHIElectronics.Endpoint.Pins.STM32MP1;
using System.Device.I2c;
using System.Device.Gpio;
using Iot.Device.Mcp23xxx;
using System.Device.Gpio.Drivers;

namespace GHIElectronics.Endpoint.Drivers.FocalTech.FT5xx6
{
    
    public class FT5xx6Controller : IDisposable
    {
        public class TouchEventArgs : EventArgs
        {
            public int X { get; }
            public int Y { get; }

            public TouchEventArgs(int x, int y)
            {
                this.X = x;
                this.Y = y;
            }
        }

        public class GestureEventArgs : EventArgs
        {
            public Gesture Gesture { get; }

            public GestureEventArgs(Gesture gesture) => this.Gesture = gesture;
        }

        public enum Gesture
        {
            MoveUp = 0x10,
            MoveLeft = 0x14,
            MoveDown = 0x18,
            MoveRight = 0x1C,
            ZoomIn = 0x48,
            ZoomOut = 0x49,
        }

        public delegate void TouchEventHandler(FT5xx6Controller sender, TouchEventArgs e);
        public delegate void GestureEventHandler(FT5xx6Controller sender, GestureEventArgs e);

        public enum TouchOrientation
        {
            Degrees0 = 0,
            Degrees90 = 1,
            Degrees180 = 2,
            Degrees270 = 3
        }

        private readonly byte[] addressBuffer = new byte[1];
        private readonly byte[] read32 = new byte[32];
        private readonly I2cController i2c;
        private readonly int interruptPin;

        public event TouchEventHandler TouchDown;
        public event TouchEventHandler TouchUp;
        public event TouchEventHandler TouchMove;
        public event GestureEventHandler GestureReceived;

        public int Width { get; set; }
        public int Height { get; set; }

        public TouchOrientation Orientation { get; set; } = TouchOrientation.Degrees0;

        public int SampleCount { get; set; } = 5;

        public static byte DeviceAddress = 0x38;

        private int portNumber = -1;
        private int pinNumber = -1;

        private GpioController gpioController;
        public FT5xx6Controller(I2cController i2c, int interrupt)
        {
            this.i2c = i2c;

            this.interruptPin = interrupt;

            this.portNumber = this.interruptPin / 16;
            this.pinNumber = this.interruptPin % 16;

            var gpioDriver = new LibGpiodDriver(portNumber);
            this.gpioController = new GpioController(PinNumberingScheme.Logical, gpioDriver);

            gpioController.OpenPin(pinNumber);
            gpioController.SetPinMode(pinNumber, PinMode.Input);

            gpioController.RegisterCallbackForPinValueChangedEvent(pinNumber, PinEventTypes.Falling, OnInterrupt);

        }
        

        private void OnInterrupt(object sender, PinValueChangedEventArgs e)
        {
            try
            {
                this.i2c.WriteRead(this.addressBuffer,  this.read32);

                if (this.read32[1] != 0 && this.GestureReceived != null)
                    this.GestureReceived(this, new GestureEventArgs((Gesture)this.read32[1]));

                //We do not read the TD_STATUS register because it returns a touch count _excluding_ touch up events, even though the touch registers contain the proper touch up data.
                for (var i = 0; i < this.SampleCount; i++)
                {
                    var idx = i * 6 + 3;
                    var flag = (this.read32[0 + idx] & 0xC0) >> 6;
                    var x = ((this.read32[0 + idx] & 0x0F) << 8) | this.read32[1 + idx];
                    var y = ((this.read32[2 + idx] & 0x0F) << 8) | this.read32[3 + idx];

                    if (this.Orientation != TouchOrientation.Degrees0)
                    {
                        // Need width, height to know do swap x,y
                        if (this.Width == 0 || this.Height == 0)
                            throw new ArgumentException("Width and Height must be not zero");

                        switch (this.Orientation)
                        {
                            case TouchOrientation.Degrees180:
                                x = this.Width - x;
                                y = this.Height - y;
                                break;

                            case TouchOrientation.Degrees270:
                                var temp = x;
                                x = this.Width - y;
                                y = temp;

                                break;

                            case TouchOrientation.Degrees90:
                                var tmp = x;
                                x = y;
                                y = this.Width - tmp;
                                break;
                        }
                    }

                    (flag == 0 ? this.TouchDown : flag == 1 ? this.TouchUp : flag == 2 ? this.TouchMove : null)?.Invoke(this, new TouchEventArgs(x, y));

                    if (flag == 3)
                        break;
                }
            }
            catch
            {
                return; // Don't stop main application
            }
        }

        public void Dispose()
        {
            this.i2c.Dispose();

            this.gpioController.UnregisterCallbackForPinValueChangedEvent(pinNumber,  OnInterrupt);
            this.gpioController.ClosePin(pinNumber);
            
        }

    }
}
