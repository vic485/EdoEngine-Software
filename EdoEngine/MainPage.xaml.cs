using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using SharpDX;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace EdoEngine
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private Device _device;
        private Mesh _mesh = new Mesh("Cube", 8, 12);
        private Camera _camera = new Camera();

        public MainPage()
        {
            this.InitializeComponent();
        }

        private void MainPage_OnLoaded(object sender, RoutedEventArgs e)
        {
            // Set back buffer resolution here
            var bitmap = new WriteableBitmap(640, 480);

            _device = new Device(bitmap);

            // XAML image control
            frontBuffer.Source = bitmap;

            _mesh.Vertices[0] = new Vector3(-1, 1, 1);
            _mesh.Vertices[1] = new Vector3(1, 1, 1);
            _mesh.Vertices[2] = new Vector3(-1, -1, 1);
            _mesh.Vertices[3] = new Vector3(1, -1, 1);
            _mesh.Vertices[4] = new Vector3(-1, 1, -1);
            _mesh.Vertices[5] = new Vector3(1, 1, -1);
            _mesh.Vertices[6] = new Vector3(1, -1, -1);
            _mesh.Vertices[7] = new Vector3(-1, -1, -1);

            _mesh.Faces[0] = new Face {A = 0, B = 1, C = 2};
            _mesh.Faces[1] = new Face {A = 1, B = 2, C = 3};
            _mesh.Faces[2] = new Face {A = 1, B = 3, C = 6};
            _mesh.Faces[3] = new Face {A = 1, B = 5, C = 6};
            _mesh.Faces[4] = new Face {A = 0, B = 1, C = 4};
            _mesh.Faces[5] = new Face {A = 1, B = 4, C = 5};

            _mesh.Faces[6] = new Face {A = 2, B = 3, C = 7};
            _mesh.Faces[7] = new Face {A = 3, B = 6, C = 7};
            _mesh.Faces[8] = new Face {A = 0, B = 2, C = 7};
            _mesh.Faces[9] = new Face {A = 0, B = 4, C = 7};
            _mesh.Faces[10] = new Face {A = 4, B = 5, C = 6};
            _mesh.Faces[11] = new Face {A = 4, B = 6, C = 7};

            _camera.Position = new Vector3(0, 0, 10f);
            _camera.Target = Vector3.Zero;

            // Register to the XAML rendering loop
            CompositionTarget.Rendering += Rendering;
        }

        // Render loop
        private void Rendering(object sender, object e)
        {
            _device.Clear(0, 0, 0, 255);

            // Rotate the cube slightly during each rendered frame
            _mesh.Rotation = new Vector3(_mesh.Rotation.X + 0.01f, _mesh.Rotation.Y + 0.01f, _mesh.Rotation.Z);

            // Doing the various matrix operations
            _device.Render(_camera, _mesh);
            // Flush the buffers
            _device.Present();
        }
    }
}
