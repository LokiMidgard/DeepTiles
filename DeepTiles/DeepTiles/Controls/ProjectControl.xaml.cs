using DeepTiles.Models;

using ExpressionBuilder;

using Microsoft.UI.Composition;
using Microsoft.UI.Composition.Interactions;
using Microsoft.UI.Composition.Scenes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;

using Windows.Foundation;
using Windows.Foundation.Collections;

using EF = ExpressionBuilder.ExpressionFunctions;
using Image = System.Drawing.Image;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DeepTiles.Controls
{
    internal sealed partial class ProjectControl : UserControl
    {
        private Visual _rootContainer;
        private Compositor _compositor;
        private ContainerVisual _worldContainer;



        public TileModel? Tile
        {
            get { return (TileModel?)GetValue(TileProperty); }
            set { SetValue(TileProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Tile.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty TileProperty =
            DependencyProperty.Register("Tile", typeof(TileModel), typeof(ProjectControl), new PropertyMetadata(null, TileChanged));

        private Bitmap[] fragmentLayers = Array.Empty<Bitmap>();
        private ExpressionNode _opacityExpression;
        private ContainerVisual cameraVisual;
        private readonly Dictionary<Bitmap, SpriteHandler> surfaces = new();
        private readonly Dictionary<Bitmap, TileFragment> fragments = new();
        private readonly Color transparancy;

        private static void TileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var me = (ProjectControl)d;
            var newValue = e.NewValue as TileModel;
            var oldValue = e.OldValue as TileModel;

            if (newValue == oldValue)
                return;

            if (oldValue is not null)
            {
                // cleanup old images
                me.RemoveFragmentLayerImage(me.fragmentLayers);
                for (int fragmentLayerIndex = 0; fragmentLayerIndex < me.fragmentLayers.Length; fragmentLayerIndex++)
                    me.fragmentLayers[fragmentLayerIndex].Dispose();

                oldValue.FragmentMask.MaskChanged += me.FragmentMaskChanged;
                (oldValue.Fragments as INotifyCollectionChanged).CollectionChanged -= me.FragmentCollectionChanged;
            }
            if (newValue is not null)
            {
                Array.Resize(ref me.fragmentLayers, newValue.Fragments.Count);
                for (int fragmentIndex = 0; fragmentIndex < newValue.Fragments.Count; fragmentIndex++)
                {
                    var original = AsBitmap(newValue.Image.ImageX);
                    var bmp = new Bitmap(original.Width, original.Height);
                    me.fragmentLayers[fragmentIndex] = bmp;
                    me.fragments[bmp] = me.Tile.Fragments[fragmentIndex];
                    for (int y = 0; y < newValue.FragmentMask.Height; y++)
                        for (int x = 0; x < newValue.FragmentMask.Width; x++)
                            if (newValue.FragmentMask[x, y] == fragmentIndex)
                            {
                                bmp.SetPixel(x, y, original.GetPixel(x, y));
                            }
                }
                newValue.FragmentMask.MaskChanged += me.FragmentMaskChanged;
                (newValue.Fragments as INotifyCollectionChanged).CollectionChanged += me.FragmentCollectionChanged;

                me.UpdateFragmentLayerImage(me.fragmentLayers);

            }
        }


        private void FragmentCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (Tile is null)
                return;
            if (e.Action != NotifyCollectionChangedAction.Add)
                throw new NotImplementedException("This was not yet implemented. -_-");


            if (e.NewItems is not null)
            {
                var oldSize = fragmentLayers.Length;
                int newSize = oldSize + e.NewItems.Count;
                Array.Resize(ref fragmentLayers, newSize);

                // shift items when inserted not at end.
                for (int i = oldSize - 1; i > e.NewStartingIndex; i--)
                    fragmentLayers[i] = fragmentLayers[i + e.NewItems.Count];

                // inert new items 
                for (int i = 0; i < e.NewItems.Count; i++)
                {
                    Bitmap original = AsBitmap(Tile.Image.ImageX);
                    var bmp = new Bitmap(original.Width, original.Height);
                    int fragmentIndex = i + e.NewStartingIndex;
                    this.fragments[bmp] = this.Tile.Fragments[fragmentIndex];
                    fragmentLayers[fragmentIndex] = bmp;
                    for (int y = 0; y < Tile.FragmentMask.Height; y++)
                        for (int x = 0; x < Tile.FragmentMask.Width; x++)
                            if (Tile.FragmentMask[x, y] == fragmentIndex)
                            {
                                bmp.SetPixel(x, y, original.GetPixel(x, y));
                            }
                }
                UpdateFragmentLayerImage(fragmentLayers[e.NewStartingIndex..]);

            }
        }

        private void UpdateFragmentLayerImage(params Bitmap[] fragmentImages) => UpdateFragmentLayerImage(fragmentImages.AsMemory());
        private void RemoveFragmentLayerImage(params Bitmap[] fragmentImages) => RemoveFragmentLayerImage(fragmentImages.AsMemory());
        private void RemoveFragmentLayerImage(ReadOnlyMemory<Bitmap> fragmentImages)
        {
            for (int i = 0; i < fragmentImages.Length; i++)
            {
                if (this.surfaces.TryGetValue(fragmentImages.Span[i], out var t))
                {
                    t.Dispose();
                    this.surfaces.Remove(fragmentImages.Span[i]);
                }
            }
        }
        private class SpriteHandler : IDisposable
        {
            private bool disposedValue;
            private ManagedSurface? surface;
            private SpriteVisual? sprite;
            private readonly ProjectControl projectControl;
            internal TileFragment TileFragment { get; private set; }

            public CancellationToken CancellationToken => cancellationTokenSource.Token;
            private CancellationTokenSource cancellationTokenSource = new();

            public SpriteHandler(ProjectControl projectControl, TileFragment tileFragment)
            {
                this.projectControl = projectControl;
                this.TileFragment = tileFragment;
                this.TileFragment.PropertyChanged += TileFragment_PropertyChanged;
            }

            private void TileFragment_PropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                this.UpdateTileLayout();
            }

            public SpriteVisual? Sprite { get => sprite; private set => sprite = value; }

            [DisallowNull]
            public ManagedSurface? Surface
            {
                get => surface; set
                {
                    if (!disposedValue)
                    {
                        if (projectControl.Tile is null)
                            return;
                        surface = value;

                        this.Sprite = projectControl.AddImage(surface.Brush);
                        this.Sprite.AnchorPoint = new Vector2(0.5f, 1);

                        UpdateTileLayout();

                    }
                    else
                        value.Dispose();
                }
            }

            private void UpdateTileLayout()
            {
                if (projectControl.Tile is null || this.Sprite is null)
                    return;

                // stretsch tile
                //var lerp = MathF.Abs(TileFragment.Angle - 45) / 45f;
                //var currentStretch = ((MathF.Sqrt(2)-1) * lerp) +1;
                var alpha = MathF.PI * 2 * this.TileFragment.Angle / 360;
                var gamma = MathF.PI / 4;
                var betta = MathF.PI - alpha - gamma;
                var b = MathF.Sqrt(2);

                var a = MathF.Sin(alpha) * b / MathF.Sin(betta);
                var currentStretch = MathF.Sin(gamma) * a / MathF.Sin(alpha);
                if (this.TileFragment.Angle == 0)
                    currentStretch = MathF.Sqrt(2);

                this.Sprite.Scale = new Vector3(1, currentStretch, currentStretch);


                var flatTIleLength = this.projectControl.Tile.Height * MathF.Sqrt(2);
                this.Sprite.Offset = new Vector3(0, flatTIleLength / 2 + TileFragment.Offset * projectControl.Tile.Height * currentStretch, TileFragment.Offset * projectControl.Tile.Height * currentStretch);
                this.Sprite.RotationAngleInDegrees = -TileFragment.Angle;
                this.Sprite.RotationAxis = Vector3.UnitX;

            }

            protected virtual void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        cancellationTokenSource.Cancel();
                        this.TileFragment.PropertyChanged -= TileFragment_PropertyChanged;
                        if (Sprite is not null)
                            projectControl._worldContainer.Children.Remove(Sprite);
                        Sprite?.Dispose();
                        Surface?.Dispose();
                    }

                    disposedValue = true;
                }
            }


            public void Dispose()
            {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }
        private async void UpdateFragmentLayerImage(ReadOnlyMemory<Bitmap> fragmentImages)
        {
            for (int i = 0; i < fragmentImages.Length; i++)
            {
                Bitmap bmp = fragmentImages.Span[i];
                if (this.surfaces.TryGetValue(bmp, out var t))
                    t.Dispose();
            
                var handler = new SpriteHandler(this, this.fragments[bmp]);
                this.surfaces[bmp] = handler;
                handler.Surface = await ImageLoader.Instance.LoadFromImageAsync(bmp, new Windows.Foundation.Size(32, 32));
            }
        }
        private static Bitmap AsBitmap(Image image)
        {
            if (image is Bitmap bmp)
                return bmp;
            return new Bitmap(image);
        }
        private void FragmentMaskChanged(object? sender, MaskChangedEventArgs e)
        {
            if (Tile is null)
                return;



            var original = AsBitmap(Tile.Image.ImageX);
            var bmp = this.fragmentLayers[e.FragmentIndex];
            if (Tile.FragmentMask[e.X, e.Y] == e.FragmentIndex)
            {

                bmp.SetPixel(e.X, e.Y, original.GetPixel(e.X, e.Y));
            }
            else
            {
                var locekd = bmp.LockBits(new Rectangle(e.X, e.Y, 1, 1), System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                unsafe
                {
                    uint* p = (uint*)locekd.Scan0.ToPointer();
                    *p = 0;

                }
                //bmp.SetPixel(e.X, e.Y, this.transparancy);
                bmp.UnlockBits(locekd);
            }
            UpdateFragmentLayerImage(bmp);
        }

        //private float yaw;
        //private float pitch;
        //private float roll;

        private void UpdateCamera()
        {
            if (cameraVisual is null)
                return;
            var camareaTransfrom = Matrix4x4.CreateFromYawPitchRoll((float)this.Yaw.Value, (float)this.Pitch.Value, (float)this.Roll.Value);
            float cameraDistance = (float)this.Distance.Value;
            var perspectiveMatrix = new Matrix4x4(
                    1, 0, 0, 0,
                    0, 1, 0, 0,
                    0, 0, 1, cameraDistance == 0 ? 0 : 1 / -300,
                    0, 0, -300, 1);
            //cameraVisual.TransformMatrix = perspectiveMatrix * camareaTransfrom;
            cameraVisual.TransformMatrix =
            //Matrix4x4.CreateLookAt(
            //    //cameraPosition: new Vector3((float)this.Yaw.Value, (float)this.Pitch.Value, (float)this.Distance.Value),
            //    cameraPosition: new Vector3(3,3,3),
            //    cameraTarget: new Vector3(),
            //    cameraUpVector: new Vector3(0, 1, 0))
            //Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2, 1, 0.001f, 128)
            //Matrix4x4.CreatePerspective(48, 48, 0.001f, 48)
            //perspectiveMatrix
            Matrix4x4.CreateFromYawPitchRoll((float)this.Yaw.Value * 2, (float)this.Pitch.Value * 2, (float)this.Roll.Value * 4)
            *
            Matrix4x4.CreateLookAt(new Vector3(0, 128, 128), new Vector3(0, 0, 0), new Vector3(0, 1, 0))
            *
            Matrix4x4.CreateOrthographic(cameraDistance, cameraDistance, 0.001f, 248)
            ;


        }

        public ProjectControl()
        {
            this.InitializeComponent();
            this.Loaded += ProjectControl_Loaded;
            var bmp = new Bitmap(1, 1);
            this.transparancy = Color.Blue;

        }

        private async void ProjectControl_Loaded(object sender, RoutedEventArgs e)
        {

            // Configure Worldview

            _rootContainer = ElementCompositionPreview.GetElementVisual(root);
            _rootContainer.Size = new Vector2((float)root.ActualWidth, (float)root.ActualHeight);
            _compositor = _rootContainer.Compositor;




            cameraVisual = _compositor.CreateContainerVisual();
            cameraVisual.Offset = new Vector3(_rootContainer.Size.X * 0.5f,
                                                   _rootContainer.Size.Y * 0.5f,
                                                   0);
            UpdateCamera();



            ElementCompositionPreview.SetElementChildVisual(root, cameraVisual);

            //
            // Create a container to contain the world content.
            //

            _worldContainer = _compositor.CreateContainerVisual();

            //_worldContainer.Offset = new Vector3(0, 0, -900);
            cameraVisual.Children.InsertAtTop(_worldContainer);



            for (int i = 0; i < fragmentLayers.Length; i++)
            {
                var handler = new SpriteHandler(this, this.Tile.Fragments[i]);
                this.surfaces[fragmentLayers[i]] = handler;
                handler.Surface = await ImageLoader.Instance.LoadFromImageAsync(fragmentLayers[i], new Windows.Foundation.Size(32, 32));
            }

            var planeLine = ImageLoader.Instance.LoadLine(148, Windows.UI.Color.FromArgb(255, 0, 0, 255));
            for (int i = 0; i < 4; i++)
            {
                var sprite = AddImage(planeLine.Brush, true);

                sprite.Offset = new Vector3(i * 32 - 48, 0, 0);
            }
            for (int i = 0; i < 4; i++)
            {
                var sprite = AddImage(planeLine.Brush, true);
                sprite.Offset = new Vector3(0, MathF.Sqrt(2) * (i * 32 - 48), 0);
                sprite.RotationAngleInDegrees = 90;
            }
            {

                var xline = ImageLoader.Instance.LoadLine(16, Windows.UI.Color.FromArgb(255, 255, 0, 0));
                var sprite = AddImage(xline.Brush);
                sprite.CenterPoint = new Vector3(0, 0, 0);
                sprite.TransformMatrix = Matrix4x4.Identity
                    * Matrix4x4.CreateRotationZ(MathF.PI / 2, new Vector3())
                    //* Matrix4x4.CreateWorld(new Vector3(0,8,0), new Vector3(0,0,1), new Vector3(0,1,0))
                    ;
                var yline = ImageLoader.Instance.LoadLine(16, Windows.UI.Color.FromArgb(255, 0, 255, 0));
                sprite = AddImage(yline.Brush);
                sprite.CenterPoint = new Vector3(0, 0, 0);
                sprite.TransformMatrix = Matrix4x4.Identity
                    //* Matrix4x4.CreateWorld(new Vector3(0,8,0), new Vector3(0,0,1), new Vector3(0,1,0))
                    ;
                var zline = ImageLoader.Instance.LoadLine(16, Windows.UI.Color.FromArgb(255, 0, 0, 255));
                sprite = AddImage(zline.Brush);
                sprite.CenterPoint = new Vector3(0, 0, 0);
                sprite.TransformMatrix = Matrix4x4.Identity
                    * Matrix4x4.CreateRotationX(MathF.PI / 2, new Vector3())
                    //* Matrix4x4.CreateWorld(new Vector3(0,8,0), new Vector3(0,0,1), new Vector3(0,1,0))        
                    ;





                var rect = ImageLoader.Instance.LoadRectangle(new Windows.Foundation.Size(32, 32), Windows.UI.Color.FromArgb(255, 255, 0, 0));
                sprite = AddImage(rect.Brush);
                sprite.TransformMatrix = Matrix4x4.Identity
                    * Matrix4x4.CreateTranslation(new Vector3(-16, -16, 128))
                    * Matrix4x4.CreateRotationX(-MathF.PI / 4)
                    ;


                var shortLentgh = MathF.Sqrt(MathF.Pow(Vector3.Transform(new Vector3(16, 16, 128), Matrix4x4.CreateRotationX(-MathF.PI / 4)).Z, 2) * 2);
                var longLentgh = MathF.Sqrt(MathF.Pow(Vector3.Transform(new Vector3(-16, -16, 128), Matrix4x4.CreateRotationX(-MathF.PI / 4)).Z, 2) * 2);



                var line = ImageLoader.Instance.LoadLine(128, Windows.UI.Color.FromArgb(255, 255, 0, 255));
                sprite = AddImage(line.Brush);
                sprite.CenterPoint = new Vector3(0, 0, 0);
                
                sprite.TransformMatrix = Matrix4x4.Identity
                    * Matrix4x4.CreateRotationX(MathF.PI / 2)
                    * Matrix4x4.CreateTranslation(new Vector3(+16, +16, 0))
                    * Matrix4x4.CreateScale(new Vector3(1, 1, shortLentgh / 128))
                    * Matrix4x4.CreateTranslation(new Vector3(0, 0, 128 - shortLentgh))
                    * Matrix4x4.CreateRotationX(-MathF.PI / 4)
                ;
                sprite = AddImage(line.Brush);
                sprite.TransformMatrix = Matrix4x4.Identity
                    * Matrix4x4.CreateRotationY(-MathF.PI / 2)
                       * Matrix4x4.CreateRotationX(MathF.PI / 2)
                    * Matrix4x4.CreateTranslation(new Vector3(+16, +16, 0))
                    * Matrix4x4.CreateScale(new Vector3(1, 1, shortLentgh / 128))
                    * Matrix4x4.CreateTranslation(new Vector3(0, 0, 128 - shortLentgh))
                    * Matrix4x4.CreateRotationX(-MathF.PI / 4)
                    ;

                sprite = AddImage(line.Brush);
                sprite.TransformMatrix = Matrix4x4.Identity
                           * Matrix4x4.CreateRotationX(MathF.PI / 2)
                    * Matrix4x4.CreateTranslation(new Vector3(-16, +16, 0))
                    * Matrix4x4.CreateScale(new Vector3(1, 1, shortLentgh / 128))
                    * Matrix4x4.CreateTranslation(new Vector3(0, 0, 128 - shortLentgh))
                    * Matrix4x4.CreateRotationX(-MathF.PI / 4)
                    ;
                sprite = AddImage(line.Brush);
                sprite.TransformMatrix = Matrix4x4.Identity
                    * Matrix4x4.CreateRotationY(-MathF.PI / 2)
                            * Matrix4x4.CreateRotationX(MathF.PI / 2)
                    * Matrix4x4.CreateTranslation(new Vector3(-16, +16, 0))
                    * Matrix4x4.CreateScale(new Vector3(1, 1, shortLentgh / 128))
                    * Matrix4x4.CreateTranslation(new Vector3(0, 0, 128 - shortLentgh))
                    * Matrix4x4.CreateRotationX(-MathF.PI / 4)
                    ;


                sprite = AddImage(line.Brush);
                sprite.TransformMatrix = Matrix4x4.Identity
                   * Matrix4x4.CreateRotationX(MathF.PI / 2)
                    * Matrix4x4.CreateTranslation(new Vector3(+16, -16, 0))
                    * Matrix4x4.CreateScale(new Vector3(1, 1, longLentgh / 128))
                    * Matrix4x4.CreateTranslation(new Vector3(0, 0, 128 - longLentgh))
                    * Matrix4x4.CreateRotationX(-MathF.PI / 4)
                    ;
                sprite = AddImage(line.Brush);
                sprite.TransformMatrix = Matrix4x4.Identity
                    * Matrix4x4.CreateRotationY(-MathF.PI / 2)
                      * Matrix4x4.CreateRotationX(MathF.PI / 2)
                    * Matrix4x4.CreateTranslation(new Vector3(+16, -16, 0))
                    * Matrix4x4.CreateScale(new Vector3(1, 1, longLentgh / 128))
                    * Matrix4x4.CreateTranslation(new Vector3(0, 0, 128 - longLentgh))
                    * Matrix4x4.CreateRotationX(-MathF.PI / 4)
                    ;



                sprite = AddImage(line.Brush);
                sprite.TransformMatrix = Matrix4x4.Identity
                 * Matrix4x4.CreateRotationX(MathF.PI / 2)
                    * Matrix4x4.CreateTranslation(new Vector3(-16, -16, 0))
                    * Matrix4x4.CreateScale(new Vector3(1, 1, longLentgh / 128))
                    * Matrix4x4.CreateTranslation(new Vector3(0, 0, 128 - longLentgh))
                    * Matrix4x4.CreateRotationX(-MathF.PI / 4)
                    ;
                sprite = AddImage(line.Brush);
                sprite.TransformMatrix = Matrix4x4.Identity
                    * Matrix4x4.CreateRotationY(-MathF.PI / 2)
                   * Matrix4x4.CreateRotationX(MathF.PI / 2)
                    * Matrix4x4.CreateTranslation(new Vector3(-16, -16, 0))
                    * Matrix4x4.CreateScale(new Vector3(1, 1, longLentgh / 128))
                    * Matrix4x4.CreateTranslation(new Vector3(0, 0, 128 - longLentgh))
                    * Matrix4x4.CreateRotationX(-MathF.PI / 4)
                    ;
            }



        }


        private SpriteVisual AddImage(CompositionSurfaceBrush imageBrush, bool center = false)
        {
            var sprite = _compositor.CreateSpriteVisual();

            var size = ((CompositionDrawingSurface)imageBrush.Surface).Size;
            //size.Width *= nodeInfo.Scale;
            //size.Height *= nodeInfo.Scale;

            sprite.Size = new Vector2((float)size.Width, (float)size.Height);
            if (center)
                sprite.AnchorPoint = new Vector2(0.5f, 0.5f);
            //sprite.Offset = nodeInfo.Offset;

            _worldContainer.Children.InsertAtBottom(sprite);


            //if (applyDistanceEffects)
            //{
            //    //
            //    // Use an ExpressionAnimation to fade the image out when it goes too close or 
            //    // too far away from the camera.
            //    //

            //    if (_opacityExpression == null)
            //    {
            //        var visualTarget = ExpressionBuilder.ExpressionValues.Target.CreateVisualTarget();
            //        var world = _worldContainer.GetReference();
            //        _opacityExpression = EF.Conditional(
            //                                    defaultOpacity * (visualTarget.Offset.Z + world.Offset.Z) > -200,
            //                                    1 - EF.Clamp(visualTarget.Offset.Z + world.Offset.Z, 0, 300) / 300,
            //                                    EF.Clamp(visualTarget.Offset.Z + world.Offset.Z + 1300, 0, 300) / 300);
            //    }

            //    sprite.StartAnimation("Opacity", _opacityExpression);


            //}
            sprite.Brush = imageBrush;

            return sprite;
        }

        private void JawPitchRoll_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            this.UpdateCamera();
        }
    }

}
