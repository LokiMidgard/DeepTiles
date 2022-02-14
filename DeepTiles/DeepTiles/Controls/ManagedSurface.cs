﻿
// From WinUI Sample APp
using Microsoft.UI.Composition;

using System.Diagnostics;

using Windows.Foundation;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;
using System.Numerics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using System.Drawing;
using Size = Windows.Foundation.Size;
using Color = Windows.UI.Color;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DeepTiles.Controls;

public class ManagedSurface
{
    private CompositionDrawingSurface _surface;
    private IContentDrawer _drawer;
    private CompositionSurfaceBrush _brush;

    public ManagedSurface(CompositionDrawingSurface surface)
    {
        Debug.Assert(surface != null);
        _surface = surface;

        ImageLoader.Instance.RegisterSurface(this);
    }

    public void Dispose()
    {
        if (_surface != null)
        {
            _surface.Dispose();
            _surface = null;
        }

        if (_brush != null)
        {
            _brush.Dispose();
            _brush = null;
        }

        _drawer = null;

        ImageLoader.Instance.UnregisterSurface(this);
    }

    public CompositionDrawingSurface Surface
    {
        get { return _surface; }
    }

    public CompositionSurfaceBrush Brush
    {
        get
        {
            if (_brush == null)
            {
                _brush = _surface.Compositor.CreateSurfaceBrush(_surface);
            }

            return _brush;
        }
    }

    public Size Size
    {
        get
        {
            return (_surface != null) ? _surface.Size : Size.Empty;
        }
    }

    public async Task Draw(CompositionGraphicsDevice device, Object drawingLock, IContentDrawer drawer)
    {
        Debug.Assert(_surface != null);

        _drawer = drawer;
        await _drawer.Draw(device, drawingLock, _surface, _surface.Size);
    }

    public async void OnDeviceReplaced(object? sender, object e)
    {
        DeviceReplacedEventArgs args = (DeviceReplacedEventArgs)e;
        await ReloadContent(args.GraphicsDevce, args.DrawingLock);
    }

    private async Task ReloadContent(CompositionGraphicsDevice device, Object drawingLock)
    {
        await _drawer.Draw(device, drawingLock, _surface, _surface.Size);
    }
}

public interface IContentDrawer
{
    Task Draw(CompositionGraphicsDevice device, Object drawingLock, CompositionDrawingSurface surface, Size size);
}

public delegate void LoadTimeEffectHandler(CompositionDrawingSurface surface, CanvasBitmap bitmap, CompositionGraphicsDevice device);


public class DeviceReplacedEventArgs : EventArgs
{
    internal DeviceReplacedEventArgs(CompositionGraphicsDevice device, Object drawingLock)
    {
        GraphicsDevce = device;
        DrawingLock = drawingLock;
    }

    public CompositionGraphicsDevice GraphicsDevce { get; set; }
    public Object DrawingLock { get; set; }
}

public class ImageLoader
{
    private static bool _intialized;
    private static ImageLoader _imageLoader;


    private Compositor _compositor;
    private CanvasDevice _canvasDevice;
    private CompositionGraphicsDevice _graphicsDevice;
    private Object _drawingLock;
    private event EventHandler<Object> _deviceReplacedEvent;

    public ImageLoader(Compositor compositor)
    {
        Debug.Assert(compositor != null && _compositor == null);

        _compositor = compositor;
        _drawingLock = new object();


        _canvasDevice = new CanvasDevice();
        _canvasDevice.DeviceLost += DeviceLost;


        _graphicsDevice = (CompositionGraphicsDevice)CanvasComposition.CreateCompositionGraphicsDevice(_compositor, _canvasDevice);
        _graphicsDevice.RenderingDeviceReplaced += RenderingDeviceReplaced;
    }

    public void Dispose()
    {
        lock (_drawingLock)
        {
            _compositor = null;

            if (_canvasDevice != null)
            {
                _canvasDevice.DeviceLost -= DeviceLost;
                _canvasDevice.Dispose();
                _canvasDevice = null;
            }

            if (_graphicsDevice != null)
            {
                _graphicsDevice.RenderingDeviceReplaced -= RenderingDeviceReplaced;
                _graphicsDevice.Dispose();
                _graphicsDevice = null;
            }
        }
    }

    static public void Initialize(Compositor compositor)
    {
        Debug.Assert(!_intialized);

        if (!_intialized)
        {
            _imageLoader = new ImageLoader(compositor);
            _intialized = true;
        }
    }

    static public ImageLoader Instance
    {
        get
        {
            Debug.Assert(_intialized);
            return _imageLoader;
        }
    }

    public void RegisterSurface(ManagedSurface surface)
    {
        _deviceReplacedEvent += surface.OnDeviceReplaced;
    }

    public void UnregisterSurface(ManagedSurface surface)
    {
        _deviceReplacedEvent -= surface.OnDeviceReplaced;
    }


    private void RaiseDeviceReplacedEvent()
    {
        _deviceReplacedEvent?.Invoke(this, new DeviceReplacedEventArgs(_graphicsDevice, _drawingLock));
    }

    public ManagedSurface LoadFromUri(Uri uri)
    {
        return LoadFromUri(uri, Size.Empty);
    }

    public ManagedSurface LoadFromUri(Uri uri, Size size)
    {
        return LoadFromUri(uri, Size.Empty, null);
    }

    public ManagedSurface LoadFromUri(Uri uri, Size size, LoadTimeEffectHandler? handler)
    {
        ManagedSurface surface = new ManagedSurface(CreateSurface(size));
        var ignored = surface.Draw(_graphicsDevice, _drawingLock, new BitmapDrawer(uri, handler));

        return surface;
    }

    public ManagedSurface LoadFromFile(StorageFile file, Size size, LoadTimeEffectHandler? handler)
    {
        ManagedSurface surface = new ManagedSurface(CreateSurface(size));

        var ignored = surface.Draw(_graphicsDevice, _drawingLock, new BitmapDrawer(file, handler));

        return surface;
    }
 public IAsyncOperation<ManagedSurface> LoadFromImageAsync(Image image)
    {
        return LoadFromImageAsyncWorker(image, Size.Empty, null).AsAsyncOperation<ManagedSurface>();
    }

    public IAsyncOperation<ManagedSurface> LoadFromImageAsync(Image image, Size size)
    {
        return LoadFromImageAsyncWorker(image, size, null).AsAsyncOperation<ManagedSurface>();
    }

    public IAsyncOperation<ManagedSurface> LoadFromImageAsync(Image image, Size size, LoadTimeEffectHandler? handler)
    {
        return LoadFromImageAsyncWorker(image, size, handler).AsAsyncOperation<ManagedSurface>();
    }

    private async Task<ManagedSurface> LoadFromImageAsyncWorker(Image image, Size size, LoadTimeEffectHandler? handler)
    {
        ManagedSurface surface = new(CreateSurface(size));
        await surface.Draw(_graphicsDevice, _drawingLock, new BitmapDrawer(image, handler));

        return surface;
    }

    public IAsyncOperation<ManagedSurface> LoadFromUriAsync(Uri uri)
    {
        return LoadFromUriAsyncWorker(uri, Size.Empty, null).AsAsyncOperation<ManagedSurface>();
    }

   

    public IAsyncOperation<ManagedSurface> LoadFromUriAsync(Uri uri, Size size)
    {
        return LoadFromUriAsyncWorker(uri, size, null).AsAsyncOperation<ManagedSurface>();
    }

    public IAsyncOperation<ManagedSurface> LoadFromUriAsync(Uri uri, Size size, LoadTimeEffectHandler? handler)
    {
        return LoadFromUriAsyncWorker(uri, size, handler).AsAsyncOperation<ManagedSurface>();
    }

    public ManagedSurface LoadCircle(float radius, Color color)
    {
        ManagedSurface surface = new ManagedSurface(CreateSurface(new Size(radius * 2, radius * 2)));
        var ignored = surface.Draw(_graphicsDevice, _drawingLock, new CircleDrawer(radius, color));

        return surface;
    }

    public ManagedSurface LoadText(string text, Size size, CanvasTextFormat textFormat, Color textColor, Color bgColor)
    {
        ManagedSurface surface = new ManagedSurface(CreateSurface(size));
        var ignored = surface.Draw(_graphicsDevice, _drawingLock, new TextDrawer(text, textFormat, textColor, bgColor));

        return surface;
    }

    private async Task<ManagedSurface> LoadFromUriAsyncWorker(Uri uri, Size size, LoadTimeEffectHandler? handler)
    {
        ManagedSurface surface = new ManagedSurface(CreateSurface(size));
        await surface.Draw(_graphicsDevice, _drawingLock, new BitmapDrawer(uri, handler));

        return surface;
    }

    private CompositionDrawingSurface CreateSurface(Size size)
    {
        Size surfaceSize = size;
        if (surfaceSize.IsEmpty)
        {
            //
            // We start out with a size of 0,0 for the surface, because we don't know
            // the size of the image at this time. We resize the surface later.
            //
            surfaceSize = default(Size);
        }

        var surface = _graphicsDevice.CreateDrawingSurface(surfaceSize, Microsoft.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, Microsoft.Graphics.DirectX.DirectXAlphaMode.Premultiplied);

        return surface;
    }

#if SampleNative_TODO
        private void DeviceRemoved(DeviceLostHelper sender, object args)
        {
            _canvasDevice.RaiseDeviceLost();
        }
#endif

    private void DeviceLost(CanvasDevice sender, object args)
    {
#if !USING_CSWINRT  // TODO: Define IDirect3DDevice for Win32
        sender.DeviceLost -= DeviceLost;
#endif

        _canvasDevice = new CanvasDevice();
#if !USING_CSWINRT  // TODO: Define IDirect3DDevice for Win32
        _canvasDevice.DeviceLost += DeviceLost;
#endif

#if SampleNative_TODO
            _deviceLostHelper.WatchDevice(_canvasDevice);
#endif

        CanvasComposition.SetCanvasDevice(_graphicsDevice, _canvasDevice);
    }

    private void RenderingDeviceReplaced(CompositionGraphicsDevice sender, RenderingDeviceReplacedEventArgs args)
    {
        Task.Run(() =>
        {
            if (_deviceReplacedEvent != null)
            {
                RaiseDeviceReplacedEvent();
            }
        });
    }
}


internal class CircleDrawer : IContentDrawer
{
    private float _radius;
    private Color _color;

    public CircleDrawer(float radius, Color color)
    {
        _radius = radius;
        _color = color;
    }

    public float Radius
    {
        get { return _radius; }
    }

    public Color Color
    {
        get { return _color; }
    }

    public Task Draw(CompositionGraphicsDevice device, Object drawingLock, CompositionDrawingSurface surface, Size size)
    {
        using (var ds = CanvasComposition.CreateDrawingSession(surface))
        {
            ds.Clear(Microsoft.UI.Colors.Transparent);
            ds.FillCircle(new Vector2(_radius, _radius), _radius, _color);
        }
        return Task.CompletedTask;
    }
}

public class BitmapDrawer : IContentDrawer
{
    private readonly Uri? _uri;
    private readonly Image? _image;
    private LoadTimeEffectHandler? _handler;
    private readonly StorageFile? _file;

    public BitmapDrawer(Uri uri, LoadTimeEffectHandler? handler)
    {
        _uri = uri;
        _handler = handler;
    }

    public BitmapDrawer(StorageFile file, LoadTimeEffectHandler? handler)
    {
        _file = file;
        _handler = handler;
    }

    public BitmapDrawer(Image image, LoadTimeEffectHandler? handler)
    {
        _image = image;
        _handler = handler;
    }



    private async Task<SoftwareBitmap> LoadFromFile(StorageFile file)
    {
        SoftwareBitmap softwareBitmap;

        using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read))
        {
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

            softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied);
        }

        return softwareBitmap;
    }

    private async Task<SoftwareBitmap> LoadFromImage(Image image)
    {
        SoftwareBitmap softwareBitmap;

        using var memory = new MemoryStream();
        await Task.Run(() => image.Save(memory, System.Drawing.Imaging.ImageFormat.Png));
        memory.Seek(0, SeekOrigin.Begin);

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(memory.AsRandomAccessStream());
        softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied);

        return softwareBitmap;
    }

    public async Task Draw(CompositionGraphicsDevice device, Object drawingLock, CompositionDrawingSurface surface, Size size)
    {
        var canvasDevice = CanvasComposition.GetCanvasDevice(device);

        CanvasBitmap canvasBitmap;
        if (_file is not null)
        {
            SoftwareBitmap softwareBitmap = await LoadFromFile(_file);

            canvasBitmap = CanvasBitmap.CreateFromSoftwareBitmap(canvasDevice, softwareBitmap);
        }
        else if (_image is not null)
        {
            SoftwareBitmap softwareBitmap = await LoadFromImage(_image);

            canvasBitmap = CanvasBitmap.CreateFromSoftwareBitmap(canvasDevice, softwareBitmap);
        }
        else
        {
            canvasBitmap = await CanvasBitmap.LoadAsync(canvasDevice, _uri);
        }

        var bitmapSize = canvasBitmap.Size;

        //
        // Because the drawing is done asynchronously and multiple threads could
        // be trying to get access to the device/surface at the same time, we need
        // to do any device/surface work under a lock.
        //
        lock (drawingLock)
        {
            Size surfaceSize = size;
            if (surface.Size != size || surface.Size == new Size(0, 0))
            {
                // Resize the surface to the size of the image
                CanvasComposition.Resize(surface, bitmapSize);
                surfaceSize = bitmapSize;
            }

            // Allow the app to process the bitmap if requested
            if (_handler != null)
            {
                _handler(surface, canvasBitmap, device);
            }
            else
            {
                // Draw the image to the surface

                using (var session = CanvasComposition.CreateDrawingSession(surface))
                {
                    session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                    session.DrawImage(canvasBitmap, new Rect(0, 0, surfaceSize.Width, surfaceSize.Height), new Rect(0, 0, bitmapSize.Width, bitmapSize.Height));
                }
            }
        }
    }
}

internal class TextDrawer : IContentDrawer
{
    private string _text;
    private CanvasTextFormat _textFormat;
    private Color _textColor;
    private Color _backgroundColor;

    public TextDrawer(string text, CanvasTextFormat textFormat, Color textColor, Color bgColor)
    {
        _text = text;
        _textFormat = textFormat;
        _textColor = textColor;
        _backgroundColor = bgColor;
    }

    public Task Draw(CompositionGraphicsDevice device, Object drawingLock, CompositionDrawingSurface surface, Size size)
    {
        using (var ds = CanvasComposition.CreateDrawingSession(surface))
        {
            ds.Clear(_backgroundColor);
            ds.DrawText(_text, new Rect(0, 0, surface.Size.Width, surface.Size.Height), _textColor, _textFormat);
        }
        return Task.CompletedTask;
    }
}