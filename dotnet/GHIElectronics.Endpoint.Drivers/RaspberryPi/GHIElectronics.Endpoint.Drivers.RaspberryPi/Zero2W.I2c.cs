// Pi Zero 2 W I2C bus constants. The chip-neutral verb
// (GHIElectronics.Endpoint.Core.I2cController.Initialize) takes the bus id
// below; Zero2WBoardSupport maps it to SDA/SCL pins + ALT mux + modprobe
// at runtime.
//
// Usage:
//     using GHIElectronics.Endpoint.Core;
//     using GHIElectronics.Endpoint.Drivers.RaspberryPi;
//
//     I2cController.Initialize(Zero2W.I2c.I2c1);
//     using var dev = I2cDevice.Create(new I2cConnectionSettings(Zero2W.I2c.I2c1, 0x50));
//     dev.Read(buffer);
//     I2cController.UnInitialize(Zero2W.I2c.I2c1);

namespace GHIElectronics.Endpoint.Drivers.RaspberryPi;

public static partial class Zero2W
{
    public static class I2c
    {
        /// <summary>Pi user I2C — SDA on GPIO 2 (header pin 3), SCL on GPIO 3 (header pin 5), ALT0. Maps to /dev/i2c-1.</summary>
        // static readonly (not const) so the reference forces the Drivers.RaspberryPi
        // assembly to load, which runs [ModuleInitializer] and registers Zero2WBoardSupport.
        public static readonly int I2c1 = 1;

        /// <summary>Default I2C speed (fast mode).</summary>
        public static readonly int DefaultFrequency = 400_000;
    }
}
