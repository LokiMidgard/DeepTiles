namespace DeepTiles.Models
{
    internal interface ITile
    {
        TilesetModel Parent { get; }
        int Width { get; }
        int Height { get; }
    }
}