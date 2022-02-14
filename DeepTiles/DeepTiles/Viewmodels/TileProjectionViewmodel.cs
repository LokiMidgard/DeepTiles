using DeepTiles.Models;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Media3D;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeepTiles.Viewmodels
{
    internal class TileProjectionViewmodel : DependencyObject
    {


        public TileModel Tile
        {
            get { return (TileModel)GetValue(TileProperty); }
            set { SetValue(TileProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Tile.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty TileProperty =
            DependencyProperty.Register("Tile", typeof(TileModel), typeof(TileProjectionViewmodel), new PropertyMetadata(null, Update));

        private static void Update(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var me = (TileProjectionViewmodel)d;

            var newValue = e.NewValue as TileModel;
            var oldValue = e.OldValue as TileModel;
            if (oldValue is not null)
            {
                (oldValue.Fragments as INotifyCollectionChanged).CollectionChanged -= me.TileProjectionViewmodel_CollectionChanged;
                foreach (var fragment in oldValue.Fragments)
                {
                    fragment.PropertyChanged += me.Fragment_PropertyChanged;
                }
            }
            if (newValue is not null)
            {
                (newValue.Fragments as INotifyCollectionChanged).CollectionChanged += me.TileProjectionViewmodel_CollectionChanged;
                foreach (var fragment in newValue.Fragments)
                {
                    fragment.PropertyChanged += me.Fragment_PropertyChanged;
                }
            }

            me.UpdateFragments();
        }

        private void Fragment_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            UpdateFragments();
        }

        private void TileProjectionViewmodel_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateFragments();
        }

        private ObservableCollection<TileProjectionFragmentViewmodel> fragments = new ObservableCollection<TileProjectionFragmentViewmodel>();
        public ReadOnlyObservableCollection<TileProjectionFragmentViewmodel> Fragments { get; }


        public TileProjectionViewmodel()
        {
            Fragments = new ReadOnlyObservableCollection<TileProjectionFragmentViewmodel>(this.fragments);

        }


        private void UpdateFragments()
        {
            fragments.Clear();

            if (Tile is null)
                return;

            for (int i = 0; i < Tile.Fragments.Count; i++)
            {
                this.fragments.Add(new TileProjectionFragmentViewmodel(this.Tile, i));
            }
        }

    }

    internal class TileProjectionFragmentViewmodel : DependencyObject
    {

        public TileProjectionFragmentViewmodel(TileModel Tile, int fragmentIndex)
        {
            var fragment = Tile.Fragments[fragmentIndex];

            var bmp = new Bitmap(Tile.Image.ImageX);


            for (int y = 0; y < Tile.FragmentMask.Height; y++)
                for (int x = 0; x < Tile.FragmentMask.Width; x++)
                {
                    if (Tile.FragmentMask[x, y] == fragmentIndex)
                    {
                    }
                    else
                    {
                        bmp.SetPixel(x, y, Color.Transparent);
                    }
                }

            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                ms.Seek(0, SeekOrigin.Begin);

                var bitmapImage = new BitmapImage();

                bitmapImage.SetSource(ms.AsRandomAccessStream());

                ImageSource = bitmapImage;
            }

            this.Transform = new CompositeTransform3D()
            {
                RotationX = fragment.Angle,
                TranslateY = fragment.Offset * 128,
                TranslateZ = fragment.Offset * 128,
                CenterY = 128

            };







        }



        public ImageSource ImageSource { get; }

        public Transform3D Transform { get; }



    }

}
