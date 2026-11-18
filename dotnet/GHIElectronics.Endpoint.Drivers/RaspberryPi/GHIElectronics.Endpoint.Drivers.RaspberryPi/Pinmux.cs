// Fork+exec wrapper around /usr/sbin/endpoint-pinmux, the C helper that
// writes SoC pinmux registers via /dev/mem. One binary per chip ships in
// the Buildroot image; this driver uses the bcm2837 build on Pi Zero 2 W.
// Keeps chip-specific register knowledge in one place (the C binary) so
// .NET, Python, and C/C++ don't each re-implement it. Same shape as
// GHIElectronics.Endpoint.Core.Internal.Pinmux.
//
// `function` is a chip-specific packed uint32 - see
// Endpoint-Firmware/pinmux/src/pinmux.h for each chip's layout. For
// BCM2837 (Pi): bits 0-2 = GPFSEL function (0=input, 1=output,
// 2..7 = alt5/alt4/alt0/alt1/alt2/alt3).
//
// Peripheral verb classes (Zero2W.I2c / Spi / Pwm) use Get before their
// first Set to snapshot the user's pre-Initialize state, then Set again
// in UnInitialize to put it back. The in-process state lives in the
// peripheral class; nothing persists across processes.

using System.Diagnostics;
using System.Globalization;

namespace GHIElectronics.Endpoint.Drivers.RaspberryPi;

internal static class Pinmux
{
    private const string PinmuxPath = "/usr/sbin/endpoint-pinmux";
    private const string ModprobePath = "/sbin/modprobe";

    /// <summary>Set the pin's function. Throws if the binary exits non-zero.</summary>
    public static void Set(int pin, uint function) =>
        Run(PinmuxPath,
            "set",
            pin.ToString(CultureInfo.InvariantCulture),
            "0x" + function.ToString("X", CultureInfo.InvariantCulture));

    /// <summary>Read the pin's current function (packed uint32, chip-specific layout).</summary>
    public static uint Get(int pin)
    {
        var stdout = RunCapture(PinmuxPath, "get", pin.ToString(CultureInfo.InvariantCulture));
        // Output format: "pin=<n> function=0x<hex> level=<0|1>\n"
        const string key = "function=";
        int i = stdout.IndexOf(key, System.StringComparison.Ordinal);
        if (i < 0) throw new System.InvalidOperationException(
            $"endpoint-pinmux get {pin}: unexpected output '{stdout.TrimEnd()}'");
        int start = i + key.Length;
        int end = start;
        while (end < stdout.Length && !char.IsWhiteSpace(stdout[end])) end++;
        var value = stdout.AsSpan(start, end - start);
        if (value.Length > 2 && value[0] == '0' && (value[1] == 'x' || value[1] == 'X'))
            return uint.Parse(value[2..], System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return uint.Parse(value, CultureInfo.InvariantCulture);
    }

    /// <summary>Load a kernel module via /sbin/modprobe (idempotent).</summary>
    public static void Modprobe(string module) => Run(ModprobePath, module);

    private static void Run(string path, params string[] args)
    {
        var psi = new ProcessStartInfo(path) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new System.InvalidOperationException($"cannot start {path}");
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new System.InvalidOperationException($"{path} {string.Join(' ', args)} failed (exit {p.ExitCode}): {err.TrimEnd()}");
    }

    private static string RunCapture(string path, params string[] args)
    {
        var psi = new ProcessStartInfo(path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new System.InvalidOperationException($"cannot start {path}");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new System.InvalidOperationException($"{path} {string.Join(' ', args)} failed (exit {p.ExitCode}): {stderr.TrimEnd()}");
        return stdout;
    }
}
