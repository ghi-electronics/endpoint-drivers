// Pi Zero 2 W PWM support. Pay-for-use: the PWM output pin stays free until
// Initialize(channel) muxes it to its PWM alt function; UnInitialize restores it.
//
// BCM2837 has one hardware PWM block with two channels. Each channel can be
// routed to one of two GPIOs:
//
//   PWM0 channel 0: GPIO 12 (ALT0) or GPIO 18 (ALT5)
//   PWM0 channel 1: GPIO 13 (ALT0) or GPIO 19 (ALT5)
//
// All pin numbers below are BCM GPIO numbers (the ones labelled on Pi pinout
// diagrams). The Linux pwmchip is 0, Linux channel numbers mirror the hardware.
//
// Usage:
//     Zero2W.Pwm.Initialize(Zero2W.Pwm.Pwm0Chan0Gpio12);
//     using var pwm = PwmChannel.Create(chip: 0, channel: 0, frequency: 1000, dutyCyclePercentage: 0.5);
//     pwm.Start();
//     ...
//     Zero2W.Pwm.UnInitialize(Zero2W.Pwm.Pwm0Chan0Gpio12);
//
// Microsoft's System.Device.Pwm.PwmChannel writes to the standard
// /sys/class/pwm/pwmchipN/pwmN interface; our Initialize is only about
// muxing the output pin.

using System.Collections.Generic;

namespace GHIElectronics.Endpoint.Drivers.RaspberryPi;

public static partial class Zero2W
{
    public static class Pwm
    {
        /// <summary>One PWM output: the GPIO + its alt function + the Linux pwmchip/channel ids.</summary>
        public record Channel(string Name, int Pin, int PinFunction, int ChipId, int ChannelId);

        /// <summary>PWM0 channel 0 routed to GPIO 12 (ALT0).</summary>
        public static readonly Channel Pwm0Chan0Gpio12 = new("pwm0-ch0-gpio12", Pin: 12, PinFunction: 4, ChipId: 0, ChannelId: 0);

        /// <summary>PWM0 channel 0 routed to GPIO 18 (ALT5). Alternative pin for the same channel.</summary>
        public static readonly Channel Pwm0Chan0Gpio18 = new("pwm0-ch0-gpio18", Pin: 18, PinFunction: 2, ChipId: 0, ChannelId: 0);

        /// <summary>PWM0 channel 1 routed to GPIO 13 (ALT0).</summary>
        public static readonly Channel Pwm0Chan1Gpio13 = new("pwm0-ch1-gpio13", Pin: 13, PinFunction: 4, ChipId: 0, ChannelId: 1);

        /// <summary>PWM0 channel 1 routed to GPIO 19 (ALT5). Alternative pin for the same channel.</summary>
        public static readonly Channel Pwm0Chan1Gpio19 = new("pwm0-ch1-gpio19", Pin: 19, PinFunction: 2, ChipId: 0, ChannelId: 1);

        private const string KernelModule = "pwm-bcm2835";

        private static readonly Dictionary<int, uint> _saved = new();

        public static void Initialize(Channel channel)
        {
            _saved[channel.Pin] = Pinmux.Get(channel.Pin);
            Pinmux.Modprobe(KernelModule);
            Pinmux.Set(channel.Pin, (uint)channel.PinFunction);
        }

        public static void UnInitialize(Channel channel)
        {
            if (_saved.Remove(channel.Pin, out var function)) Pinmux.Set(channel.Pin, function);
        }
    }
}
