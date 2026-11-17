// Thin internal wrappers around /usr/sbin/endpoint-pinmux (the C helper
// shipped with the Pi firmware image) and /sbin/modprobe. Keeps each
// peripheral verb class (Zero2W.I2c, Zero2W.Spi, ...) from re-implementing
// the Process.Start boilerplate; one place to change if we ever move off
// exec (e.g. to a Unix socket).

using System.Diagnostics;
using System.Globalization;

namespace GHIElectronics.Endpoint.Drivers.RaspberryPi;

internal static class Pinmux
{
    private const string PinmuxPath = "/usr/sbin/endpoint-pinmux";
    private const string ModprobePath = "/sbin/modprobe";

    public static void Save(int pin)              => Run(PinmuxPath, "save", pin.ToString(CultureInfo.InvariantCulture));
    public static void Restore(int pin)           => Run(PinmuxPath, "restore", pin.ToString(CultureInfo.InvariantCulture));
    public static void Set(int pin, int function) => Run(PinmuxPath, "set", pin.ToString(CultureInfo.InvariantCulture), function.ToString(CultureInfo.InvariantCulture));

    public static void Modprobe(string module)    => Run(ModprobePath, module);

    private static void Run(string path, params string[] args)
    {
        var psi = new ProcessStartInfo(path) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new System.InvalidOperationException($"cannot start {path}");
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new System.InvalidOperationException($"{path} {string.Join(' ', args)} failed (exit {p.ExitCode}): {err}");
    }
}
