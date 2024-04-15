using GHIElectronics.Endpoint.Devices.Display;
using GHIElectronics.Endpoint.Drivers.VirtualKeyboard.Properties;
using SkiaSharp;

namespace GHIElectronics.Endpoint.Drivers.VirtualKeyboard {
    internal enum KeyboardEvent {
        None = 0,
        Pressed = 500,
        Released,
        Move,
    }
    internal class TouchPoint {
        internal int X { get; set; }
        internal int Y { get; set; }
        internal KeyboardEvent Evt { get; set; }
        internal ulong Timestamp { get; set; }
        internal TouchPoint() => this.Evt = KeyboardEvent.None;



        internal void Clear() {
            this.Evt = KeyboardEvent.None;
            this.X = 0;
            this.Y = 0;
            this.Timestamp = 0;
        }
    }

    public class VirtualKeyboard {
        SKPaint colorWhiteFill = new SKPaint() { Style = SKPaintStyle.Fill, Color = SKColors.White };
        SKPaint colorBlackFill = new SKPaint() { Style = SKPaintStyle.Fill, Color = SKColors.Black };
        public int ActualWidth { get; internal set; }
        public int ActualHeight { get; internal set; }

        SKBitmap bitmap;
        KeyboardView active;

        private string initialText = string.Empty;
        public string Text { get; private set; } = string.Empty;

        public void Clear() => this.initialText = string.Empty;

        private DisplayController displayController;

        private byte[] data565;

        private float offsetX;
        private float offsetY;

        private object lockObj;

        private bool show = false;
        private bool doRefresh = true;

        SKCanvas canvas;

        private float scaleXY;

        const float IMG_RESOURCE_WIDTH = 800;
        const float IMG_RESOURCE_HEIGHT = 320;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public VirtualKeyboard(DisplayController display, string initialText = "") {

            this.Width = display.Configuration.Width;
            this.Height = display.Configuration.Height;
            this.displayController = display;
            this.initialText = initialText;

            this.Initialize();
        }

        //public VirtualKeyboard(int width, int height, string initialText = "") {
        //    this.Width = width;
        //    this.Height = height;
           
        //    this.initialText = initialText;

        //    this.Initialize();
        //}

        private void Initialize() {
            this.lockObj = new object();

            this.bitmap = new SKBitmap(this.Width, this.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);

            this.scaleXY = this.Width / IMG_RESOURCE_WIDTH;

            this.ActualWidth = (int)(IMG_RESOURCE_WIDTH * this.scaleXY);
            this.ActualHeight = (int)(IMG_RESOURCE_HEIGHT * this.scaleXY);

            this.ShowView(KeyboardViewId.Lowercase);

            this.TouchPoint = new TouchPoint();
            this.doRefresh = true;
        }


        internal TouchPoint TouchPoint { get; set; }
        public void UpdateKey(int x, int y) {
            this.TouchPoint.X = x;
            this.TouchPoint.Y = y;    

            lock (this.lockObj) {
                this.TouchPoint.Evt = KeyboardEvent.Released;
            }
        }

        public bool IsEnabled => this.show;

        public delegate void OnShowEventHandler();
        public event OnShowEventHandler OnShow;
        public void Show() {

            this.show = true;
            this.doRefresh = true;
            this.canvas = new SKCanvas(this.bitmap);

            OnShow?.Invoke();

            while (this.show) {
                try {
                    if (this.TouchPoint.Evt != KeyboardEvent.None) {

                        lock (this.lockObj) {
                            this.TouchPoint.Evt = KeyboardEvent.None;
                        }

                        this.HandleKeys(this.TouchPoint);

                        this.doRefresh = true;
                    }

                    if (this.doRefresh && this.show) {
                        this.canvas.Clear(SKColors.Black);
                        this.DrawInputTextBox(this.initialText);
                        this.canvas.DrawBitmap(this.active.Image, this.offsetX, this.offsetY);

                        this.data565 = this.bitmap.Copy(SKColorType.Rgb565).Bytes;

                        this.doRefresh = false;

                        if (this.displayController != null && this.data565 != null)
                            this.displayController.Flush(this.data565, 0, this.data565.Length, 0, 0, this.bitmap.Width, this.bitmap.Height, this.bitmap.Width);
                    }
                }
                catch {

                }

                Thread.Sleep(50);
            }

            this.OnClose?.Invoke();

        }

        private void DrawInputTextBox(string text) {
            var sKFont = new SKFont {
                Size = 35 * this.scaleXY
            };

            SKTextBlob textBlob;
            // the rectangle
            var rect = SKRect.Create(0, 0, this.Width, this.Height - this.ActualHeight);
            this.canvas.DrawRect(rect, this.colorWhiteFill);

            // draw fill
            if (text != string.Empty) {
                textBlob = SKTextBlob.Create(text, sKFont);

                this.canvas.DrawText(textBlob, 2, 2 + sKFont.Size, this.colorBlackFill);
            }
        }

        private void ShowView(KeyboardViewId id) {
            this.CreateView(id);


            this.offsetX = 0;
            this.offsetY = this.Height - this.ActualHeight;// this.input.Height;

        }

        private void CreateView(KeyboardViewId id) {
            var hf = (int)(40 * this.scaleXY);
            var sz = (int)(80 * this.scaleXY);
            var szh = (int)(120 * this.scaleXY);


            var full = new[] { sz, sz, sz, sz, sz, sz, sz, sz, sz, sz };
            SKBitmap image = null;
            var info = new SKImageInfo((int)this.ActualWidth, (int)this.ActualHeight);

            this.active = new KeyboardView { RowHeight = sz };

            switch (id) {
                case KeyboardViewId.Lowercase:

                    var img1 = Resources.Keyboard_Lowercase;
                    var info1 = new SKImageInfo((int)IMG_RESOURCE_WIDTH, (int)IMG_RESOURCE_HEIGHT);
                    var image1 = SKBitmap.Decode(img1, info1);
                    image = image1.Resize(info, SKFilterQuality.Low);

                    this.active.RowColumnOffset = new[] { 0, hf, 0, 0 };
                    this.active.ColumnWidth = new[] {
                        full,
                        new[] { sz, sz, sz, sz, sz, sz, sz, sz, sz },
                        new[] { szh, sz, sz, sz, sz, sz, sz, sz, szh },
                        new[] { szh, sz, sz * 4, sz, sz, szh }
                    };
                    this.active.Keys = new[] {
                        new[] { 'q', 'w', 'e', 'r', 't', 'y', 'u', 'i', 'o', 'p' },
                        new[] { 'a', 's', 'd', 'f', 'g', 'h', 'j', 'k', 'l' },
                        new[] { '\0', 'z', 'x', 'c', 'v', 'b', 'n', 'm', '\0' },
                        new[] { '\0', ',', ' ', '.', '\0', '\0' }
                    };
                    this.active.SpecialKeys = new[] {
                        null,
                        null,
                        new Action[] { () => this.ShowView(KeyboardViewId.Uppercase), null, null, null, null, null, null, null, () => this.Backspace() },
                        new Action[] { () => this.ShowView(KeyboardViewId.Numbers), null, null, null, () => this.Cancel(), () => this.Close() }
                    };
                    break;

                case KeyboardViewId.Uppercase:
                    var img2 = Resources.Keyboard_Uppercase;
                    var info2 = new SKImageInfo((int)IMG_RESOURCE_WIDTH, (int)IMG_RESOURCE_HEIGHT);
                    var image2 = SKBitmap.Decode(img2, info2);
                    image = image2.Resize(info, SKFilterQuality.Low);

                    this.active.RowColumnOffset = new[] { 0, hf, 0, 0 };
                    this.active.ColumnWidth = new[] {
                        full,
                        new[] { sz, sz, sz, sz, sz, sz, sz, sz, sz },
                        new[] { szh, sz, sz, sz, sz, sz, sz, sz, szh },
                        new[] { szh, sz, sz * 4, sz, sz, szh }
                    };
                    this.active.Keys = new[] {
                        new[] { 'Q', 'W', 'E', 'R', 'T', 'Y', 'U', 'I', 'O', 'P' },
                        new[] { 'A', 'S', 'D', 'F', 'G', 'H', 'J', 'K', 'L' },
                        new[] { '\0', 'Z', 'X', 'C', 'V', 'B', 'N', 'M', '\0' },
                        new[] { '\0', ',', ' ', '.', '\0','\0' }
                    };
                    this.active.SpecialKeys = new[] {
                        null,
                        null,
                        new Action[] { () => this.ShowView(KeyboardViewId.Lowercase), null, null, null, null, null, null, null, () => this.Backspace() },
                        new Action[] { () => this.ShowView(KeyboardViewId.Numbers), null, null, null, () => this.Cancel(), () => this.Close() }
                    };
                    break;

                case KeyboardViewId.Numbers:
                    var img3 = Resources.Keyboard_Numbers;
                    var info3 = new SKImageInfo((int)IMG_RESOURCE_WIDTH, (int)IMG_RESOURCE_HEIGHT);
                    var image3 = SKBitmap.Decode(img3, info3);
                    image = image3.Resize(info, SKFilterQuality.Low);

                    this.active.RowColumnOffset = new[] { 0, 0, 0, 0 };
                    this.active.ColumnWidth = new[] {
                        full,
                        full,
                        new[] { szh, sz, sz, sz, sz, sz, sz, sz, szh },
                        new[] { szh, sz, sz * 4, sz, sz, szh }
                    };
                    this.active.Keys = new[] {
                        new[] { '1', '2', '3', '4', '5', '6', '7', '8', '9', '0' },
                        new[] { '@', '#', '$', '%', '&', '*', '-', '+', '(', ')' },
                        new[] { '\0', '!', '"', '\'', ':', ';', '/', '?', '\0' },
                        new[] { '\0', ',', ' ', '.', '\0','\0' }
                    };
                    this.active.SpecialKeys = new[] {
                        null,
                        null,
                        new Action[] { () => this.ShowView(KeyboardViewId.Symbols), null, null, null, null, null, null, null, () => this.Backspace() },
                        new Action[] { () => this.ShowView(KeyboardViewId.Lowercase), null, null, null, () => this.Cancel(), () => this.Close() }
                    };
                    break;

                case KeyboardViewId.Symbols:
                    var img4 = Resources.Keyboard_Symbols;
                    var info4 = new SKImageInfo((int)IMG_RESOURCE_WIDTH, (int)IMG_RESOURCE_HEIGHT);
                    var image4 = SKBitmap.Decode(img4, info4);

                    image = image4.Resize(info, SKFilterQuality.Low);

                    this.active.RowColumnOffset = new[] { 0, 0, 0, 0 };
                    this.active.ColumnWidth = new[] {
                        full,
                        full,
                        new[] { szh, sz, sz, sz, sz, sz, sz, sz, szh },
                        new[] { szh, sz, sz * 4, sz, sz, szh }
                    };
                    this.active.Keys = new[] {
                        new[] { '~', '`', '|', '•', '√', 'π', '÷', '×', '{', '}' },
                        new[] { '\t', '£', '¢', '€', 'º', '^', '_', '=', '[', ']' },
                        new[] { '\0', '™', '®', '©', '¶', '\\', '<', '>', '\0' },
                        new[] { '\0', ',', ' ', '.', '\0','\0' }
                    };
                    this.active.SpecialKeys = new[] {
                        null,
                        null,
                        new Action[] { () => this.ShowView(KeyboardViewId.Numbers), null, null, null, null, null, null, null, () => this.Backspace() },
                        new Action[] { () => this.ShowView(KeyboardViewId.Lowercase), null, null, null, () => this.Cancel(),() => this.Close() }
                    };

                    break;
            }

            this.active.Image = image;
        }

        private void Backspace() { if (this.initialText.Length > 0) this.initialText = this.initialText.Substring(0, this.initialText.Length - 1); }
        private void Append(char c) {
            this.initialText += c; ;
        }

        private void Close() {
            this.Text = this.initialText;
            this.show = false;
            

        }

        public delegate void OnCloseEventHandler();
        public event OnCloseEventHandler OnClose;


        private void Cancel() {
            this.show = false; ;

        }

        private void HandleKeys(TouchPoint e) {

            var x = (int)((e.X - this.offsetX));
            var y = (int)((e.Y - this.offsetY));

            var row = y / this.active.RowHeight;
            var column = 0;
            var columnWidth = this.active.ColumnWidth[row];
            var total = this.active.RowColumnOffset[row];

            while (column < columnWidth.Length && total < x)
                total += columnWidth[column++];

            if (--column < 0 || total < x)
                return;

            if (this.active.SpecialKeys?[row]?[column] is Action a) {
                a();
            }
            else {
                this.Append(this.active.Keys[row][column]);
            }
        }
        private enum KeyboardViewId {
            Lowercase,
            Uppercase,
            Numbers,
            Symbols
        }
        private class KeyboardView {
            public SKBitmap Image { get; set; }
            public int RowHeight { get; set; }
            public int[] RowColumnOffset { get; set; }
            public int[][] ColumnWidth { get; set; }
            public char[][] Keys { get; set; }
            public Action[][] SpecialKeys { get; set; }
        }
    }
}
