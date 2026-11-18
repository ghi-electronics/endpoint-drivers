# GHIElectronics.Endpoint.Drivers.RaspberryPi

Raspberry Pi board support for Endpoint (Python). Mirrors the .NET driver API.

One package covers every Pi model this library knows (Zero 2 W today; more
added in later versions). Thin layer over the Linux kernel: each
peripheral's `Initialize(bus_or_channel)` muxes the pins by shelling out
to `/usr/sbin/endpoint-pinmux` (the C helper shipped in the Pi firmware
image) and loads the matching kernel module. `UnInitialize` restores the
pins to their pre-Initialize state.

```python
from GHIElectronics.Endpoint.Drivers.RaspberryPi import Zero2W

Zero2W.I2c.Initialize(Zero2W.I2c.I2c1)
# ... open /dev/i2c-1 with smbus2 / python-periphery / anything ...
Zero2W.I2c.UnInitialize(Zero2W.I2c.I2c1)
```

Users bring their own GPIO / I2C / SPI library (python-gpiod, smbus2,
spidev, ...). This package only handles pin muxing and kernel module
loading.

Part of the [GHI Electronics Endpoint drivers](https://github.com/ghi-electronics/endpoint-drivers).
See the repository README for the full driver list.
