// Pi Zero 2 W PWM pin constants. The chip-neutral verb
// (GHIElectronics.Endpoint.Core.PwmController.Initialize) takes a pin;
// Zero2WBoardSupport looks it up in the table below for the right ALT
// function, chip id, and channel id.
//
// BCM2837 has one hardware PWM block (pwmchip0) with two channels, each
// routable to one of two GPIOs:
//   PWM0 channel 0: GPIO 12 (ALT0) or GPIO 18 (ALT5)
//   PWM0 channel 1: GPIO 13 (ALT0) or GPIO 19 (ALT5)
//
// Usage:
//     using GHIElectronics.Endpoint.Core;
//     using GHIElectronics.Endpoint.Drivers.RaspberryPi;
//
//     PwmController.Initialize(Zero2W.Pwm.Pin.Pin12);
//     using var pwm = PwmChannel.Create(
//         PwmController.GetChipId(Zero2W.Pwm.Pin.Pin12),
//         PwmController.GetChannelId(Zero2W.Pwm.Pin.Pin12),
//         frequency: 1000, dutyCyclePercentage: 0.5);
//     pwm.Start();
//     ...
//     PwmController.UnInitialize(Zero2W.Pwm.Pin.Pin12);

namespace GHIElectronics.Endpoint.Drivers.RaspberryPi;

public static partial class Zero2W
{
    public static class Pwm
    {
        public static class Pin
        {
            /// <summary>PWM0 channel 0, routed to GPIO 12 at ALT0. Header pin 32.</summary>
            public static readonly int Pin12 = 12;

            /// <summary>PWM0 channel 1, routed to GPIO 13 at ALT0. Header pin 33.</summary>
            public static readonly int Pin13 = 13;

            /// <summary>PWM0 channel 0, routed to GPIO 18 at ALT5. Header pin 12. Alternative pin for channel 0.</summary>
            public static readonly int Pin18 = 18;

            /// <summary>PWM0 channel 1, routed to GPIO 19 at ALT5. Header pin 35. Alternative pin for channel 1.</summary>
            public static readonly int Pin19 = 19;
        }
    }
}
