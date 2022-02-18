using System;
using System.Collections.Generic;
using System.Numerics;
using System.Drawing;
using Microsoft.UI.Xaml.Media;
using System.Windows.Input;
using Prism.Commands;
using System.Collections.ObjectModel;
//using AutoNotify;


namespace DeepTiles.Models;

internal abstract partial class TileImage
{

    private Image? image;
    public Image ImageX
    {
        get
        {
            if (image is null)
                this.image = this.GenerateImage();
            return this.image;
        }
    }

    ImageSource? source;
    public ImageSource Source
    {
        get
        {
            if (source is null)
                source = GenerateSource();
            return source;
        }
    }

    public ImageSource GenerateSource()
    {
        string imagePath = Path.GetFullPath(Path.Combine(Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path, "Tiles", $"{Guid.NewGuid()}.png"));
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        this.ImageX.Save(imagePath);


        var imgSrc = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage
        {
            UriSource = new Uri($"file://{imagePath}")
        };
        return imgSrc;

    }
    protected abstract Image GenerateImage();


    public partial class FileImage : TileImage
    {
        private Image bitmap;

        public FileImage(Image bitmap)
        {
            this.bitmap = bitmap;
        }

        protected override Image GenerateImage()
        {
            return this.bitmap;
        }


        public static TileImage FromStream(Stream stream)
        {

            var b = new Bitmap( Image.FromStream(stream));
            //b.MakeTransparent();
            return new FileImage(b);
        }
        public static async Task<TileImage?> Pick()
        {
            var window = App.Current.Window;

            var picker = window.FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            var pickerResult = await picker.PickSingleFileAsync("importTileset");
            if (pickerResult is null)
                return null;
            using var stream = await pickerResult.OpenReadAsync();
            return FromStream(stream.AsStreamForRead());
        }
    }

    public class SubImage : TileImage
    {
        private readonly TileImage baseImage;
        private readonly Rectangle crop;


        public SubImage(TileImage baseImage, Rectangle crop)
        {
            this.baseImage = baseImage;
            this.crop = crop;
        }

        protected override Image GenerateImage()
        {
            if (crop.X == 0 && crop.Y == 0 && crop.Width == baseImage.ImageX.Width && crop.Height == baseImage.ImageX.Height)
            {
                return baseImage.ImageX;
            }
            var newIMage = new Bitmap(crop.Width, crop.Height);
            using (var g = Graphics.FromImage(newIMage))
                g.DrawImage(baseImage.ImageX, new Rectangle(0, 0, crop.Width, crop.Height), crop, GraphicsUnit.Pixel);

            return newIMage;
        }
    }

}

internal class NineGridTile : TileBase
{
    public NineGridTile(TilesetModel parent) : base(parent) { }
    public ITile? TopLeft { get; set; }
    public ITile? TopRigt { get; set; }
    public ITile? BottomRigt { get; set; }
    public ITile? BottomLeft { get; set; }
    public ITile? Center { get; set; }
}

internal class BoxGridTile : TileBase
{

    public BoxGridTile(TilesetModel parent) : base(parent) { }
    public NineGridTile? Top { get; set; }
    public NineGridTile? Front { get; set; }
}

internal class TileBase : ITile
{
    public TilesetModel Parent { get; }
    public int Width => Parent.TileWidth;
    public int Height => Parent.TileHeight;

    public TileBase(TilesetModel parent)
    {
        this.Parent = parent;
    }


}

public class MaskChangedEventArgs
{
    public byte FragmentIndex { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
}
internal class FragmentMask
{
    private byte[,] mask;

    public FragmentMask(int width, int height)
    {
        this.mask = new byte[width, height];
    }

    public event EventHandler<MaskChangedEventArgs>? MaskChanged;

    public int Width => this.mask.GetLength(0);
    public int Height => this.mask.GetLength(1);

    public byte this[int x, int y]
    {
        get => mask[x, y];
        set
        {
            var old = mask[x, y];
            mask[x, y] = value;
            MaskChanged?.Invoke(this, new MaskChangedEventArgs() { FragmentIndex = old, X = x, Y = y });
            MaskChanged?.Invoke(this, new MaskChangedEventArgs() { FragmentIndex = value, X = x, Y = y });
        }
    }
}

internal class TileModel : TileBase
{

    public TileImage Image { get; }
    private TileModel(TilesetModel parent, TileImage image) : base(parent)
    {
        Image = image;
        this.FragmentMask = new FragmentMask(this.Width, this.Height);
        this.fragments = new ObservableCollection<TileFragment>();
        this.Fragments = new ReadOnlyObservableCollection<TileFragment>(fragments);
        this.AddFragmentCommand = new DelegateCommand(() =>
        {
            this.fragments.Add(new TileFragment(this,0));
        });
    }

    public FragmentMask FragmentMask { get; }


    private ObservableCollection<TileFragment> fragments;
    public ReadOnlyObservableCollection<TileFragment> Fragments { get; }

    public Passability Passability { get; set; } = new Passability.Any();


    public ICommand AddFragmentCommand { get; }


    public static TileModel FromImage(TilesetModel parent, TileImage image, Point position)
    {
        var model = new TileModel(parent, new TileImage.SubImage(image, new Rectangle(position, new Size(parent.TileWidth, parent.TileHeight)))); ;
        model.fragments.Add(new TileFragment(model,model.fragments.Count) { Angle = 0 });
        return model;
    }
}


internal abstract partial class Passability
{

    private Passability() { }

    internal class Any : Passability { }
    internal class Passibl : Passability
    {

    }

    internal class Blocked : Passability
    {

    }


    internal partial class DirectionsBlocked : Passability
    {
        public DirectionsBlocked()
        {

        }
        [SourceGenerators.AutoNotify]

        private Direction blocedDirections;
        [Flags]
        internal enum Direction
        {
            None = 0,
            Up = 1,
            Down = 2,
            Left = 4,
            Right = 8,
        }
    }

    internal class GeometryBased : Passability
    {

        public List<(byte Layer, float ThiknesTop, float ThiknesLeft, float ThiknesBottem, float ThiknesRight)> Thiknes { get; } = new();
    }


}

internal partial class TileFragment
{

    ///<Summary>
    /// The Offset how far from the camarea this fragment is, relative to tilesize.
    /// 0.0 is the Front and 1.0 the back of the Toxel for Horizontal tiles
    /// 0.0 is the Bottom and -11.0 the Top of the Toxel for flat tiles
    ///</Summary>
    ///
    [SourceGenerators.AutoNotify]
    private float offset;

    ///<Summary>
    /// The Angle in Degree. 0 Is a flat tile and 90 a horizontal. The rotation origin is the lower edge of the tile.
    ///</Summary>
    [SourceGenerators.AutoNotify]
    private float angle;


    private TileModel model;
    
    [SourceGenerators.AutoNotify]
    private int index;

    public TileFragment(TileModel model, int index)
    {
        this.model = model;
        this.index = index;
    }
}

