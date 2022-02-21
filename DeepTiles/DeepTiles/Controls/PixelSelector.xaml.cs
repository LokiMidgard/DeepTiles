using DeepTiles.Models;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;

using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DeepTiles.Controls
{
    internal sealed partial class PixelSelector : UserControl
    {



        public TileModel Tile
        {
            get { return (TileModel)GetValue(TileProperty); }
            set { SetValue(TileProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Tile.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty TileProperty =
            DependencyProperty.Register("Tile", typeof(TileModel), typeof(PixelSelector), new PropertyMetadata(null, Update));



        public int SelectedLayer
        {
            get { return (int)GetValue(SelectedLayerProperty); }
            set { SetValue(SelectedLayerProperty, value); }
        }

        // Using a DependencyProperty as the backing store for SelectedLayer.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty SelectedLayerProperty =
            DependencyProperty.Register("SelectedLayer", typeof(int), typeof(PixelSelector), new PropertyMetadata((byte)0, Update));
        private static void Update(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var me = (PixelSelector)d;
            me.UpdateAdoner();
        }





        private ImageSource AdonerSource
        {
            get { return (ImageSource)GetValue(AdonerSourceProperty); }
            set { SetValue(AdonerSourceProperty, value); }
        }

        // Using a DependencyProperty as the backing store for imageSource.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty AdonerSourceProperty =
            DependencyProperty.Register("AdonerSource", typeof(ImageSource), typeof(PixelSelector), new PropertyMetadata(null));


        private bool isOffsetCalculation;

        public PixelSelector()
        {
            this.InitializeComponent();
        }

        protected override void OnPointerMoved(PointerRoutedEventArgs e)
        {
            base.OnPointerMoved(e);
            if (this.Tile is null)
                return;
            if (!e.Pointer.IsInContact)
                return;
            var p = e.GetCurrentPoint(background);

            var x = (int)(p.Position.X / background.ActualWidth * Tile.Width);
            var y = (int)(p.Position.Y / background.ActualHeight * Tile.Height);

            if (x >= 0 && y >= 0 && x < this.Tile.FragmentMask.Width && y < this.Tile.FragmentMask.Height)
            {
                this.Tile.FragmentMask[(int)(p.Position.X / background.ActualWidth * Tile.Width), (int)(p.Position.Y / background.ActualHeight * Tile.Height)] = (byte)this.SelectedLayer;
                UpdateAdoner();
            }
        }

        protected override void OnPointerPressed(PointerRoutedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (this.Tile is null)
                return;

            var p = e.GetCurrentPoint(background);

            var x = (int)(p.Position.X / background.ActualWidth * Tile.Width);
            var y = (int)(p.Position.Y / background.ActualHeight * Tile.Height);

            if (x >= 0 && y >= 0 && x < this.Tile.FragmentMask.Width && y < this.Tile.FragmentMask.Height)
            {


                if (isOffsetCalculation)
                {
                    isOffsetCalculation = false;
                    calculateMode.Content = "Calculate Offset";
                    var fragment = this.Tile.Fragments[this.SelectedLayer];
                    if (fragment.Angle == 0)
                    {
                        fragment.Offset = (float)(p.Position.Y / background.ActualWidth);

                    }
                    else
                    {
                        fragment.Offset = -(1f - (float)(p.Position.Y / background.ActualWidth));
                    }
                }
                else
                {
                    this.Tile.FragmentMask[(int)(p.Position.X / background.ActualWidth * Tile.Width), (int)(p.Position.Y / background.ActualHeight * Tile.Height)] = (byte)this.SelectedLayer;
                    UpdateAdoner();
                }
            }
        }

        private void UpdateAdoner()
        {
            if (Tile is null)
                return;
            var bmp = new Bitmap(Tile.Width, Tile.Height);



            for (int y = 0; y < Tile.FragmentMask.Height; y++)
                for (int x = 0; x < Tile.FragmentMask.Width; x++)
                {
                    if (Tile.FragmentMask[x, y] == SelectedLayer)
                    {
                        bmp.SetPixel(x, y, Color.Red);
                    }
                    else
                    {
                        bmp.SetPixel(x, y, Color.Transparent);
                    }
                }

            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Bmp);
                ms.Seek(0, SeekOrigin.Begin);

                var bitmapImage = new BitmapImage();

                bitmapImage.SetSource(ms.AsRandomAccessStream());

                AdonerSource = bitmapImage;
            }



        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (isOffsetCalculation)
            {
                isOffsetCalculation = false;
                calculateMode.Content = "Calculate Offset";
            }
            else
            {
                isOffsetCalculation = true;
                var fragment = this.Tile.Fragments[this.SelectedLayer];
                if (fragment.Angle == 0)
                {
                    calculateMode.Content = "Click where the floor (would) hit the front";
                }
                else
                {
                    calculateMode.Content = "Click where the wall (would) hit the floor";
                }
            }
        }
    }
}
