// Raspberry Pi Zero 2 W GPIO support: a GpioDriver subclass plus whatever
// board-level pin constants are useful.
//
// The Driver is a thin wrapper on Microsoft's LibGpiodDriver. It exists so
// board code can say `new Zero2W.Gpio.Driver()` (matching the EPDomino24 /
// EPN256 shape) instead of making every user know about LibGpiodDriver and
// which gpiochip number to pass.
//
// Why not just `new GpioController()`? The parameterless constructor picks
// Microsoft's RaspberryPi3LinuxDriver on the Pi, which rejects pin numbers
// above 27 (it only validates 40-pin header pins) — so OpenPin(29) throws.
// LibGpiodDriver goes straight to /dev/gpiochip0 and handles any line.

using System.Device.Gpio.Drivers;

namespace GHIElectronics.Endpoint.Drivers.RaspberryPi;

public static partial class Zero2W
{
    public static class Gpio
    {
        /// <summary>GpioDriver for the Pi Zero 2 W. Opens /dev/gpiochip0 via libgpiod.</summary>
        public sealed class Driver : LibGpiodDriver
        {
            public Driver() : base(gpioChip: 0) { }
        }
    }
}
