using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using GHIElectronics.Endpoint.Devices.Display;
using GHIElectronics.Endpoint.Drivers.Avalonia.Input.Properties;
using Iot.Device.Graphics;
using SkiaSharp;

namespace GHIElectronics.Endpoint.Drivers.Avalonia.Input {
    internal class OnScreenKeyboard {

        SKPaint colorWhiteFill = new SKPaint() { Style = SKPaintStyle.Fill, Color = SKColors.White };
        SKPaint colorBlackFill = new SKPaint() { Style = SKPaintStyle.Fill, Color = SKColors.Black };
        public int Width { get; internal set; }
        public int Height { get; internal set; }

        SKBitmap bitmap;
        KeyboardView active;

        public string InitialText = string.Empty;
        public string SourceText { get; set; } = string.Empty;

        private DisplayController displayController;

        private byte[] data565;
        private float scaleX;
        private float scaleY;
        private float offsetX;
        private float offsetY;

        private object lockObj;

        private bool show = true;
        private bool doRefresh = true;

        SKCanvas canvas;

        private float scaleXY;

        const float IMG_RESOURCE_WIDTH = 800;
        const float IMG_RESOURCE_HEIGHT = 320;
        public OnScreenKeyboard(DisplayController displayController) {

            this.lockObj = new object();

            bitmap = new SKBitmap(displayController.Configuration.Width, displayController.Configuration.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);

            this.displayController = displayController;

            this.scaleXY = displayController.Configuration.Width / IMG_RESOURCE_WIDTH;

            this.Width = (int)(IMG_RESOURCE_WIDTH * this.scaleXY); 
            this.Height = (int)(IMG_RESOURCE_HEIGHT * this.scaleXY);




            this.ShowView(KeyboardViewId.Lowercase);

            this.TouchPoint = new TouchPoint();
            this.doRefresh = true;
        }

        internal TouchPoint TouchPoint { get; set; }
        public void UpdateTouchPoint(int x, int y, TouchEvent ev) {
            this.TouchPoint.X = x;
            this.TouchPoint.Y = y;
            this.TouchPoint.Evt = ev;
            
        }

        public void Show() {

            this.show = true;
            //this.inputText = string.Empty;
            //this.SourceText = string.Empty;
            this.doRefresh = true;

            canvas = new SKCanvas(bitmap);

            //canvas.Scale(this.scaleXY);

            while (show) {
                if (this.TouchPoint.Evt == TouchEvent.Released) {

                    lock (this.lockObj) {
                        this.TouchPoint.Evt = TouchEvent.None;
                    }

                    this.HandleKeys(this.TouchPoint);

                    this.doRefresh = true;
                }

                if (this.doRefresh) {
                    canvas.Clear(SKColors.Black);
                    DrawInputTextBox(this.InitialText);
                    canvas.DrawBitmap(active.Image, this.offsetX, this.offsetY);                    

                    this.data565 = bitmap.Copy(SKColorType.Rgb565).Bytes;

                    this.doRefresh = false;

                    
                }

                if (this.data565 != null)
                    this.displayController.Flush(this.data565, 0, 0);

                Thread.Sleep(10);
            }
        }

        private void DrawInputTextBox(string text) {
            SKFont sKFont = new SKFont();
            sKFont.Size = 35 * scaleXY;

            SKTextBlob textBlob;
            // the rectangle
            var rect = SKRect.Create(0, 0, displayController.Configuration.Width, displayController.Configuration.Height - this.Height);
            canvas.DrawRect(rect, colorWhiteFill);

            // draw fill
            if (text != string.Empty) {
                textBlob = SKTextBlob.Create(text, sKFont);

                canvas.DrawText(textBlob, 2, 2 + sKFont.Size, colorBlackFill);
            }
        }

        private void ShowView(KeyboardViewId id) {
            this.CreateView(id);

            this.scaleX = this.scaleXY;
            this.scaleY = this.scaleXY; 
            this.offsetX = 0;
            this.offsetY = this.displayController.Configuration.Height - this.Height;// this.input.Height;

        }

        private void CreateView(KeyboardViewId id) {
            var hf = (int)(40 * this.scaleXY);
            var sz = (int)(80 * this.scaleXY);
            var szh = (int)(120 * this.scaleXY);

            //var hf = 24;
            //var sz = 48;
            //var szh = 72;

            var full = new[] { sz, sz, sz, sz, sz, sz, sz, sz, sz, sz };
            SKBitmap image = null;
            SKImageInfo info = new SKImageInfo((int)this.Width, (int)this.Height);

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

        private void Backspace() { if (this.InitialText.Length > 0) this.InitialText = this.InitialText.Substring(0, this.InitialText.Length - 1); }
        private void Append(char c) {
            this.InitialText += c;

           
        }

        private new void Close() {
            this.SourceText = this.InitialText;
            this.show = false;
            
        }

        private new void Cancel() {
            this.show = false;
            
        }

        public void HandleKeys(TouchPoint e) {
            var x = (int)((e.X - this.offsetX) );
            var y = (int)((e.Y - this.offsetY) );

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
