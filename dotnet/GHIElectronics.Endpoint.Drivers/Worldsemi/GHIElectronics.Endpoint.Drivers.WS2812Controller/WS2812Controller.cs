using GHIElectronics.Endpoint.Devices.DigitalSignal;

namespace GHIElectronics.Endpoint.Drivers.WS2812Controller
{
    public class WS2812Controller
    {
        private readonly byte[] data;
        private readonly uint numLeds;
        private const uint RESETPULSE = 10 * 50 * 2;
        private const uint MULTIPLER = 50U;
        private const uint BIT_ONE = 850 / MULTIPLER;
        private const uint BIT_ZERO = 400 / MULTIPLER;

        DigitalSignalController signal;

        public uint NumLeds => this.numLeds;
        public WS2812Controller(int pin, uint numLeds)
        {

            this.data = new byte[numLeds * 3];
            this.numLeds = numLeds;
            this.signal = new DigitalSignalController(pin);

        }
        public void SetColor(uint index, byte red, byte green, byte blue)
        {
            this.data[index * 3 + 0] = green;
            this.data[index * 3 + 1] = red;
            this.data[index * 3 + 2] = blue;
        }
        public void Flush() => this.Flush(true);

        public bool CanFlush => this.signal.CanGeneratePulse;
        public void Flush(bool reset)
        {                        
            var resetbyte = 0;

            if (reset)
            {
                resetbyte = 1;
            }
            var bits_timing = new uint[this.data.Length * 8 * 2 + resetbyte];

            var idx = 0;

            if (reset)
                bits_timing[idx++] = 100;// reset

            for (var i = 0; i < this.data.Length; i++)
            {
                for (var b = 7; b >= 0; b--)
                {
                    if ((this.data[i] & (1 << b)) > 0)
                    {
                        bits_timing[idx++] = BIT_ONE;
                        bits_timing[idx++] = BIT_ZERO;
                    }
                    else
                    {
                        bits_timing[idx++] = BIT_ZERO;
                        bits_timing[idx++] = BIT_ONE;
                    }
                }
            }
           
            this.signal.Generate(bits_timing, 0, bits_timing.Length, MULTIPLER, 1);
        }

        public void Clear()
        {
            for (var i = 0U; i < this.numLeds; i++)
                this.SetColor(i, 0x00, 0x00, 0x00);
        }

        public void Reset()
        {
            var resetData = new uint[] { RESETPULSE / MULTIPLER, 1 };

            this.signal.Generate(resetData, 0, resetData.Length, MULTIPLER, 1);
        }
    }
}
