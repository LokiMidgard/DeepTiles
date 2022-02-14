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
        private readonly Dictionary<Bitmap, (ManagedSurface surface, SpriteVisual sprite)> surfaces = new();

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
                foreach (var fragment in oldValue.Fragments)
                {
                    fragment.PropertyChanged -= me.FragmentMetadataChanged;
                }
            }
            if (newValue is not null)
            {
                Array.Resize(ref me.fragmentLayers, newValue.Fragments.Count);
                for (int fragmentIndex = 0; fragmentIndex < newValue.Fragments.Count; fragmentIndex++)
                {
                    var bmp = new Bitmap(newValue.Image.ImageX);
                    me.fragmentLayers[fragmentIndex] = bmp;
                    for (int y = 0; y < newValue.FragmentMask.Height; y++)
                        for (int x = 0; x < newValue.FragmentMask.Width; x++)
                            if (newValue.FragmentMask[x, y] != fragmentIndex)
                            {
                                bmp.SetPixel(x, y, Color.Transparent);
                            }
                }
                newValue.FragmentMask.MaskChanged += me.FragmentMaskChanged;
                (newValue.Fragments as INotifyCollectionChanged).CollectionChanged += me.FragmentCollectionChanged;

                foreach (var fragment in newValue.Fragments)
                {
                    fragment.PropertyChanged += me.FragmentMetadataChanged;
                }
                me.UpdateFragmentLayerImage(me.fragmentLayers);

            }
        }

        private void FragmentMetadataChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Offset or Angle chnanged Not Mask

            //var tileFragment = (TileFragment) sender;
            //var fragmentImage = this this.fragmentLayers[tileFragment.inde];
            //if (Tile.FragmentMask[e.X, e.Y] == e.FragmentIndex)
            //{
            //    fragmentImage.SetPixel(e.X, e.Y, tileImage.GetPixel(e.X, e.Y));
            //}
            //else
            //{
            //    fragmentImage.SetPixel(e.X, e.Y, Color.Transparent);
            //}
            //UpdateFragmentLayerImage(fragmentImage);

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
                    var bmp = new Bitmap(Tile.Image.ImageX);
                    int fragmentIndex = i + e.NewStartingIndex;
                    fragmentLayers[fragmentIndex] = bmp;
                    for (int y = 0; y < Tile.FragmentMask.Height; y++)
                        for (int x = 0; x < Tile.FragmentMask.Width; x++)
                            if (Tile.FragmentMask[x, y] != fragmentIndex)
                            {
                                bmp.SetPixel(x, y, Color.Transparent);
                            }
                    Tile.Fragments[fragmentIndex].PropertyChanged += FragmentMetadataChanged;
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
                    var (surface, sprite) = t;

                    _worldContainer.Children.Remove(sprite);
                    sprite.Dispose();
                    surface.Dispose();
                    this.surfaces.Remove(fragmentImages.Span[i]);
                }
            }
        }
        private async void UpdateFragmentLayerImage(ReadOnlyMemory<Bitmap> fragmentImages)
        {
            for (int i = 0; i < fragmentImages.Length; i++)
            {
                if (!this.surfaces.TryGetValue(fragmentImages.Span[i], out var t))
                {
                    var managedSurface = await ImageLoader.Instance.LoadFromImageAsync(fragmentLayers[i], new Windows.Foundation.Size(32, 32));
                    var sprite = AddImage(managedSurface.Brush);
                    this.surfaces[fragmentLayers[i]] = (managedSurface, sprite);
                }
                else
                {
                    var (surface, sprite) = t;

                    _worldContainer.Children.Remove(sprite);
                    sprite.Dispose();
                    surface.Dispose();

                    surface = await ImageLoader.Instance.LoadFromImageAsync(fragmentLayers[i], new Windows.Foundation.Size(32, 32));
                    sprite = AddImage(surface.Brush);
                    this.surfaces[fragmentLayers[i]] = (surface, sprite);
                }

            }
        }

        private void FragmentMaskChanged(object? sender, MaskChangedEventArgs e)
        {
            if (Tile is null)
                return;

            static Bitmap AsBitmap(Image image)
            {
                if (image is Bitmap bmp)
                    return bmp;
                return new Bitmap(image);
            }

            var tileImage = AsBitmap(Tile.Image.ImageX);
            var fragmentImage = this.fragmentLayers[e.FragmentIndex];
            if (Tile.FragmentMask[e.X, e.Y] == e.FragmentIndex)
            {
                fragmentImage.SetPixel(e.X, e.Y, tileImage.GetPixel(e.X, e.Y));
            }
            else
            {
                fragmentImage.SetPixel(e.X, e.Y, Color.Transparent);
            }
            UpdateFragmentLayerImage(fragmentImage);
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
            Matrix4x4.CreateOrthographic(cameraDistance, cameraDistance, 0.001f, 48)
            *
            Matrix4x4.CreateFromYawPitchRoll((float)this.Yaw.Value*2, (float)this.Pitch.Value * 2, (float)this.Roll.Value*4)
            *
            Matrix4x4.CreateLookAt(new Vector3(0, 0, cameraDistance),
            new Vector3(0, 0, 0), new Vector3(0, 1, 0))
            ;


        }

        public ProjectControl()
        {
            this.InitializeComponent();
            this.Loaded += ProjectControl_Loaded;
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
                var managedSurface = await ImageLoader.Instance.LoadFromImageAsync(fragmentLayers[i], new Windows.Foundation.Size(32, 32));
                var sprite = AddImage(managedSurface.Brush);
                this.surfaces[fragmentLayers[i]] = (managedSurface, sprite);
            }




        }


        private SpriteVisual AddImage(CompositionSurfaceBrush imageBrush, float defaultOpacity = 1.0f, bool applyDistanceEffects = true)
        {
            var sprite = _compositor.CreateSpriteVisual();

            var size = ((CompositionDrawingSurface)imageBrush.Surface).Size;
            //size.Width *= nodeInfo.Scale;
            //size.Height *= nodeInfo.Scale;

            sprite.Size = new Vector2((float)size.Width, (float)size.Height);
            sprite.AnchorPoint = new Vector2(0.5f, 0.5f);
            //sprite.Offset = nodeInfo.Offset;
            
            _worldContainer.Children.InsertAtTop(sprite);


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
