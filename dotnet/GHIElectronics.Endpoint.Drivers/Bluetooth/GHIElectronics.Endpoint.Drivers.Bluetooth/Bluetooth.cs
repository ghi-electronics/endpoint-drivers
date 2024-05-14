using System.Runtime.CompilerServices;
using System.Threading;
using GHIElectronics.Endpoint.Core;
using UnitsNet;

namespace GHIElectronics.Endpoint.Drivers.Bluetooth {
    public static class Bluetooth {
        public static string GetDeviceMacAddress() => macAddress;
        public static string GetDeviceName() => devName;
        public static string GetDeviceManufacturer() => devManufacturer;

        private static string macAddress = string.Empty;
        private static string devName = string.Empty;
        private static string devManufacturer = string.Empty;
        private static bool initialized = false;

        public static void Initialize() {
            if (initialized)
                return;

            var script1 = new Script("modprobe", "./", "btusb");

            script1.Start();

            Thread.Sleep(100);

            var script2 = new Script("bt_bluetoothd.sh", "./", "start");

            script2.Start();

            Thread.Sleep(100);

            var script3 = new Script("hciconfig", "./", $"hci0 up");
            script3.Start();

            Thread.Sleep(500);

            var script4 = new Script("hciconfig", "./", "-a");
            script4.Start();

            var to = 0;
            while (script4.Output == null || script4.Output.Length == 0) {
                Thread.Sleep(100);

                script4.Start();

                to++;

                if (to == 10) {
                    throw new Exception("Could not initialize bluetooth device");
                }



            }

            if (script4.Output != null && script4.Output.Length > 0) {
                var lines = script4.Output.Split(new char[] { '\n' });

                foreach (var line in lines) {
                    if (line.Contains("Address")) {
                        macAddress = line.Substring(13, 17);
                    }
                    if (line.Contains("Name")) {
                        devName = line.Substring(7, line.Length - 7 - 1);
                    }
                    if (line.Contains("Manufacturer")) {
                        devManufacturer = line.Substring(15, line.Length - 15);
                    }
                }
            }

            if (macAddress == string.Empty) {
                throw new Exception("Could not initialize bluetooth device");
            }

            initialized = true;
        }

        public static void Power(bool on) {
            if (!initialized) {
                throw new Exception("Bluetooth device is not initialized. Call Bluetooth.Initialize() for for this method");
            }

            var value = on ? "on" : "off";
            var script = new Script("bluetoothctl", "./", $"power {value}");
            script.Start();
            Thread.Sleep(1000);

        }

        public static void Discoverable(bool on) {
            if (!initialized) {
                throw new Exception("Bluetooth device is not initialized. Call Bluetooth.Initialize() for for this method");
            }

            var value = on ? "on" : "off";
            var script = new Script("bluetoothctl", "./", $"discoverable {value}");

            script.Start();
            Thread.Sleep(1000);

        }

        public static void Pairable(bool on) {
            if (!initialized) {
                throw new Exception("Bluetooth device is not initialized. Call Bluetooth.Initialize() for for this method");
            }
            var value = on ? "on" : "off";
            var script = new Script("bluetoothctl", "./", $"pairable {value}");

            script.Start();
            Thread.Sleep(1000);
        }


        public delegate void ScanEventHandler(string info);
        public static event ScanEventHandler? OnScanEvent;


        public static bool Scan(TimeSpan timeout, string? mac = null) {
            if (!initialized) {
                throw new Exception("Bluetooth device is not initialized. Call Bluetooth.Initialize() for for this method");
            }

            if (mac != null && mac.Length == 17) {
                var scipt = new Script("bluetoothctl", "./", $"remove {mac}");
                scipt.Start();
            }


            var script2 = new Script("bt-adapter", "./", "-d");

            var found = false;
            var detect1 = 0;

            script2.OutputDataRecivedEvent += (a, b) => {

                OnScanEvent?.Invoke(b);

                if (detect1 ==1) {
                    //detect1++;

                    if(b.Contains("RSSI")) {
                        detect1 = 2;

                    }
                }

                if (detect1 == 2) {
                    a.Stop();

                    found = true;
                }
                if (mac != null && mac.Length == 17) {
                    if (b.Contains(mac)) {
                        //a.Stop();


                        //found = true;

                        detect1 = 1;
                    }
                }


            };

            var t = new Thread(() => {

                script2.Start();
            });

            t.Start();

            while (!script2.Busy) {
                Thread.Sleep(10);
            }

            var expire = DateTime.Now.Add(timeout);

            while (DateTime.Now < expire && script2.Busy) {
                Thread.Sleep(100);
            }

            Thread.Sleep(1000);
            script2.Stop();

            return found;
        }

        public static bool Unpair(string mac) {
            if (!initialized) {
                throw new Exception("Bluetooth device is not initialized. Call Bluetooth.Initialize() for for this method");
            }

            if (mac != null && mac.Length == 17) {
                var scipt = new Script("bluetoothctl", "./", "devices");
                scipt.Start();

                if (scipt.Output.Contains(mac)) {
                    scipt = new Script("bluetoothctl", "./", $"remove {mac}");

                    scipt.Start();

                    if (scipt.Output.Contains("Device has been removed"))
                        return true;
                }
            }

            return false;
        }

        public delegate void PairEventHandler(string info);
        public static event PairEventHandler? OnPairEvent;
        public static bool Pair(string mac) {
            if (!initialized) {
                throw new Exception("Bluetooth device is not initialized. Call Bluetooth.Initialize() for for this method");
            }

            var script = new Script("bt_pair.sh", "./", mac);
            var ret = false;
            if (OnPairEvent != null) {

                script.OutputDataRecivedEvent += (s, e) => {
                    OnPairEvent.Invoke(e);

                    if (e.Contains("Done"))
                        ret = true;


                };
            }

            script.Start();

            if (OnPairEvent == null) {
                if (script.Output.Contains("Done"))
                    return true;
            }

            return ret;
        }

        public static string[] DevicePaired() {
            if (!initialized) {
                throw new Exception("Bluetooth device is not initialized. Call Bluetooth.Initialize() for for this method");
            }

            var script = new Script("bluetoothctl", "./", "devices");

            script.Start();

            var list = new List<string>();

            if (script.Output != null && script.Output.Length > 0) {

                var output = script.Output.Split(new char[] { '\n' });

                foreach (var o in output) {
                    if (o.Length >= 17) // macaddress
                    {
                        list.Add(o);
                    }

                }
            }

            return list.ToArray();
        }



    }
}
