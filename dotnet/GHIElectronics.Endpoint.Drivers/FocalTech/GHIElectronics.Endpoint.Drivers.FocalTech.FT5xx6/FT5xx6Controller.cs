// FocalTech FT5xx6 capacitive-touch driver for GHI Endpoint. Reusable across
// any Endpoint board: the user constructs a GpioController with the right
// chip's Driver (EPM815.Gpio.Driver, EPN256.Gpio.Driver, ...) and passes it
// in — no /16 / %16 math leaks into this file, no chip-specific code.
//
//     using var gpio = new GpioController(new EPM815.Gpio.Driver());
//     using var touch = new FT5xx6Controller(
//         i2cBus:       EPDomino24.I2c.I2c5,
//         interruptPin: EPM815.Gpio.Pin.PB11,
//         gpio:         gpio);
//     touch.TouchDown += (_, e) => Console.WriteLine($"({e.X}, {e.Y})");

using System;
using System.Device.Gpio;
using System.Device.I2c;

namespace GHIElectronics.Endpoint.Drivers.FocalTech.FT5xx6 {
    public class FT5xx6Controller : IDisposable {
        public class TouchEventArgs : EventArgs {
            public int X { get; }
            public int Y { get; }
            public TouchEventArgs(int x, int y) { this.X = x; this.Y = y; }
        }

        public class GestureEventArgs : EventArgs {
            public Gesture Gesture { get; }
            public GestureEventArgs(Gesture gesture) => this.Gesture = gesture;
        }

        public enum Gesture {
            MoveUp = 0x10,
            MoveLeft = 0x14,
            MoveDown = 0x18,
            MoveRight = 0x1C,
            ZoomIn = 0x48,
            ZoomOut = 0x49,
        }

        public delegate void TouchEventHandler(FT5xx6Controller sender, TouchEventArgs e);
        public delegate void GestureEventHandler(FT5xx6Controller sender, GestureEventArgs e);

        public enum TouchOrientation {
            Degrees0 = 0,
            Degrees90 = 1,
            Degrees180 = 2,
            Degrees270 = 3
        }

        private readonly byte[] addressBuffer = new byte[1];
        private readonly byte[] read32 = new byte[32];
        private readonly I2cDevice i2c;
        private readonly GpioController gpio;
        private readonly int interruptPin;

        public event TouchEventHandler TouchDown;
        public event TouchEventHandler TouchUp;
        public event TouchEventHandler TouchMove;
        public event GestureEventHandler GestureReceived;

        public int Width { get; set; }
        public int Height { get; set; }

        public TouchOrientation Orientation { get; set; } = TouchOrientation.Degrees0;

        public int SampleCount { get; set; } = 5;

        public byte DeviceAddress => 0x38;

        /// <summary>
        /// Open the FT5xx6 on <paramref name="i2cBus"/> with its IRQ on
        /// <paramref name="interruptPin"/>. The <paramref name="gpio"/>
        /// controller must already be constructed with the right chip's
        /// Driver (e.g. <c>new GpioController(new EPM815.Gpio.Driver())</c>);
        /// this driver only opens the IRQ pin and registers a falling-edge
        /// callback. The GpioController stays owned by the caller — Dispose
        /// here only unregisters the callback and closes the pin.
        /// </summary>
        public FT5xx6Controller(int i2cBus, int interruptPin, GpioController gpio) {
            this.i2c = I2cDevice.Create(new I2cConnectionSettings(i2cBus, this.DeviceAddress));
            this.gpio = gpio ?? throw new ArgumentNullException(nameof(gpio));
            this.interruptPin = interruptPin;

            this.gpio.OpenPin(this.interruptPin, PinMode.Input);
            this.gpio.RegisterCallbackForPinValueChangedEvent(
                this.interruptPin, PinEventTypes.Falling, this.OnInterrupt);
        }

        private void OnInterrupt(object sender, PinValueChangedEventArgs e) {
            try {
                this.i2c.WriteRead(this.addressBuffer, this.read32);

                if (this.read32[1] != 0 && this.GestureReceived != null)
                    this.GestureReceived(this, new GestureEventArgs((Gesture)this.read32[1]));

                // We do not read the TD_STATUS register because it returns a
                // touch count _excluding_ touch-up events, even though the
                // touch registers contain the proper touch-up data.
                for (var i = 0; i < this.SampleCount; i++) {
                    var idx = i * 6 + 3;
                    var flag = (this.read32[0 + idx] & 0xC0) >> 6;
                    var x = ((this.read32[0 + idx] & 0x0F) << 8) | this.read32[1 + idx];
                    var y = ((this.read32[2 + idx] & 0x0F) << 8) | this.read32[3 + idx];

                    if (this.Orientation != TouchOrientation.Degrees0) {
                        if (this.Width == 0 || this.Height == 0)
                            throw new ArgumentException("Width and Height must be set before rotating");

                        switch (this.Orientation) {
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

                    (flag == 0 ? this.TouchDown
                     : flag == 1 ? this.TouchUp
                     : flag == 2 ? this.TouchMove
                     : null)?.Invoke(this, new TouchEventArgs(x, y));

                    if (flag == 3)
                        break;
                }
            }
            catch {
                // Don't tear down the IRQ handler on a single bad read.
            }
        }

        public void Dispose() {
            try { this.gpio.UnregisterCallbackForPinValueChangedEvent(this.interruptPin, this.OnInterrupt); } catch { }
            try { this.gpio.ClosePin(this.interruptPin); } catch { }
            try { this.i2c.Dispose(); } catch { }
        }
    }
}
