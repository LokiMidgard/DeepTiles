using Microsoft.UI.Xaml;

using Prism.Commands;

using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.UI.Xaml;

namespace DeepTiles.Models;


internal  class TilesetViewmodel : DependencyObject
{

    public bool IsLoading
    {
        get { return (bool)GetValue(IsLoadingProperty); }
        set { SetValue(IsLoadingProperty, value); }
    }

    // Using a DependencyProperty as the backing store for IsLoading.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.Register("IsLoading", typeof(bool), typeof(TilesetViewmodel), new PropertyMetadata(false));


    public TilesetViewmodel() : this(new TilesetModel())
    {

    }

    public TilesetViewmodel(TilesetModel model)
    {
        Model = model;

        this.ImportCommand = new DelegateCommand(async () =>
        {
            if (IsLoading)
                return;
            IsLoading = true;
            try
            {
                var image = await TileImage.FileImage.Pick();
                if (image is null)
                    return;
                Model.ImportTileset(image);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        });
    }

    public TilesetModel Model { get; }

    public ICommand ImportCommand { get; }

}
internal class TilesetModel
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int TileWidth { get; } = 32;
    public int TileHeight { get; } = 32;


    public ObservableCollection<TileBase> Tiles { get; } = new ObservableCollection<TileBase>();


    public void ImportTileset(TileImage tileImage)
    {

        

        var image = tileImage.ImageX;
        if (image.Width % TileWidth != 0 || image.Height % TileHeight != 0)
            throw new ArgumentOutOfRangeException(nameof(tileImage), $"Width must be a multiple of {TileWidth} was {image.Width}. Height must be a multiple of {TileHeight} was {image.Height}");

        for (int y = 0; y < image.Height; y += TileWidth)
            for (int x = 0; x < image.Width; x += TileWidth)
            {
                var tile = TileModel.FromImage(this, tileImage, new System.Drawing.Point(x, y));
                this.Tiles.Add(tile);
            }

    }

}