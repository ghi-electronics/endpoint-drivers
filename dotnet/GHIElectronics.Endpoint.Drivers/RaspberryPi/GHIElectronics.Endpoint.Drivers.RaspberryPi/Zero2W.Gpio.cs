// Raspberry Pi Zero 2 W GPIO support: a GpioDriver subclass + BCM GPIO
// pin constants for every pin on the 40-pin header.
//
// All pin numbers are BCM GPIO numbers (as labelled in every Pi pinout
// diagram, e.g. https://pinout.xyz). NOT physical header pin numbers.
// So Zero2W.Gpio.Pin12 means "BCM GPIO 12" (which happens to sit at physical
// header pin 32), and Zero2W.Led means "BCM GPIO 29" (the on-board ACT LED,
// internal to the SoC and not on the header).
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
        // BCM GPIO numbers exposed on the 40-pin header. Pin0 and Pin1 are
        // reserved for HAT EEPROM I2C (ID_SDA/ID_SCL) and should normally not
        // be used by user code; they're included for completeness.
        public const int Pin0  = 0;
        public const int Pin1  = 1;
        public const int Pin2  = 2;
        public const int Pin3  = 3;
        public const int Pin4  = 4;
        public const int Pin5  = 5;
        public const int Pin6  = 6;
        public const int Pin7  = 7;
        public const int Pin8  = 8;
        public const int Pin9  = 9;
        public const int Pin10 = 10;
        public const int Pin11 = 11;
        public const int Pin12 = 12;
        public const int Pin13 = 13;
        public const int Pin14 = 14;
        public const int Pin15 = 15;
        public const int Pin16 = 16;
        public const int Pin17 = 17;
        public const int Pin18 = 18;
        public const int Pin19 = 19;
        public const int Pin20 = 20;
        public const int Pin21 = 21;
        public const int Pin22 = 22;
        public const int Pin23 = 23;
        public const int Pin24 = 24;
        public const int Pin25 = 25;
        public const int Pin26 = 26;
        public const int Pin27 = 27;

        /// <summary>GpioDriver for the Pi Zero 2 W. Opens /dev/gpiochip0 via libgpiod.</summary>
        public sealed class Driver : LibGpiodDriver
        {
            public Driver() : base(gpioChip: 0) { }
        }
    }
}
