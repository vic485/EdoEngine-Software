using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.UI.Xaml.Media.Imaging;
using Newtonsoft.Json;
using SharpDX;

namespace EdoEngine
{
    public class Device
    {
        private byte[] _backBuffer;
        private WriteableBitmap _bitmap;

        public Device(WriteableBitmap bitmap)
        {
            _bitmap = bitmap;
            // The back buffer size is equal to the number of pixels to draw on screen
            // (width * height) * 4 (RGBA values)
            _backBuffer = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        }

        // Called to clear the back buffer with a specific color
        public void Clear(byte r, byte g, byte b, byte a)
        {
            for (var i = 0; i < _backBuffer.Length; i += 4)
            {
                // BGRA is used by windows
                _backBuffer[i] = b;
                _backBuffer[i + 1] = g;
                _backBuffer[i + 2] = r;
                _backBuffer[i + 3] = a;
            }
        }

        // When ready, flush back buffer to front buffer
        public void Present()
        {
            using var stream = _bitmap.PixelBuffer.AsStream();
            // Writing our byte[] back buffer into the writeable bitmap stream
            stream.Write(_backBuffer, 0, _backBuffer.Length);

            _bitmap.Invalidate();
        }

        // Put a pixel on screen at specific X,Y coordinates
        public void PutPixel(int x, int y, Color4 color)
        {
            // Since we have a 1D array for the back buffer, we need to know the equivalent cell in 1D, based on the 2D
            // coordinates on screen.
            var index = (x + y * _bitmap.PixelWidth) * 4;

            _backBuffer[index] = (byte) (color.Blue * 255);
            _backBuffer[index + 1] = (byte) (color.Green * 255);
            _backBuffer[index + 2] = (byte) (color.Red * 255);
            _backBuffer[index + 3] = (byte) (color.Alpha * 255);
        }

        // Project takes some 3D coordinates and transforms them in 2D coordinates using the transformation matrix
        public Vector2 Project(Vector3 coord, Matrix transMat)
        {
            // transform coordinates
            var point = Vector3.TransformCoordinate(coord, transMat);
            // Transformed coordinates will be based on coordinate system starting at the center of the screen.
            // Drawing on screen normally starts from top left. We need to transform them again to have x:0, y:0 at
            // top left.
            var x = point.X * _bitmap.PixelWidth + _bitmap.PixelWidth / 2.0f;
            var y = -point.Y * _bitmap.PixelHeight + _bitmap.PixelHeight / 2.0f;

            return new Vector2(x, y);
        }

        // Calls PutPixel with clipping operation beforehand
        public void DrawPoint(Vector2 point)
        {
            // Clip what's visible on screen
            if (point.X >= 0 && point.Y >= 0 && point.X < _bitmap.PixelWidth && point.Y < _bitmap.PixelHeight)
            {
                // Draw yellow point
                PutPixel((int) point.X, (int) point.Y, new Color4(1.0f, 1.0f, 0.0f, 1.0f));
            }
        }

        // Draws a line between two points using Bresenham's line algorithm
        public void DrawLine(Vector2 point1, Vector2 point2)
        {
            var x1 = (int) point1.X;
            var y1 = (int) point1.Y;
            var x2 = (int) point2.X;
            var y2 = (int) point2.Y;

            var dx = Math.Abs(x2 - x1);
            var dy = Math.Abs(y2 - y1);
            var sx = x1 < x2 ? 1 : -1;
            var sy = y1 < y2 ? 1 : -1;
            var err = dx - dy;

            while (true)
            {
                DrawPoint(new Vector2(x1, y1));

                if (x1 == x2 && y1 == y2)
                    break;

                var e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x1 += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y1 += sy;
                }
            }
        }

        // The main method of the engine that re-computes each vertex projection during each frame
        public void Render(Camera camera, params Mesh[] meshes)
        {
            var viewMatrix = Matrix.LookAtLH(camera.Position, camera.Target, Vector3.UnitY);
            var projMatrix =
                Matrix.PerspectiveFovRH(0.78f, (float) _bitmap.PixelWidth / _bitmap.PixelHeight, 0.01f, 1.0f);

            foreach (var mesh in meshes)
            {
                // Beware to apply rotation before translation
                var worldMatrix = Matrix.RotationYawPitchRoll(mesh.Rotation.Y, mesh.Rotation.X, mesh.Rotation.Z) *
                                  Matrix.Translation(mesh.Position);
                var transformationMatrix = worldMatrix * viewMatrix * projMatrix;

                /*foreach (var vertex in mesh.Vertices)
                {
                    // First project the 3D coordinates to 2D space
                    var point = Project(vertex, transformationMatrix);
                    // Then draw on screen
                    DrawPoint(point);
                }*/

                foreach (var face in mesh.Faces)
                {
                    var vertexA = mesh.Vertices[face.A];
                    var vertexB = mesh.Vertices[face.B];
                    var vertexC = mesh.Vertices[face.C];

                    var pixelA = Project(vertexA, transformationMatrix);
                    var pixelB = Project(vertexB, transformationMatrix);
                    var pixelC = Project(vertexC, transformationMatrix);

                    DrawLine(pixelA, pixelB);
                    DrawLine(pixelB, pixelC);
                    DrawLine(pixelC, pixelA);
                }
            }
        }

        // Asynchronous Json model loading
        public static async Task<Mesh[]> LoadJsonFileAsync(string fileName)
        {
            var meshes = new List<Mesh>();
            var file = await Package.Current.InstalledLocation.GetFileAsync(fileName);
            var data = await FileIO.ReadTextAsync(file);
            dynamic jsonObj = JsonConvert.DeserializeObject(data);

            for (var meshIndex = 0; meshIndex < jsonObj.meshes.Count; meshIndex++)
            {
                var vertArray = jsonObj.meshes[meshIndex].vertices;
                var indArray = jsonObj.meshes[meshIndex].indices;

                var uvCount = jsonObj.meshes[meshIndex].uvCount.Value;
                var vertexStep = (int) uvCount switch
                {
                    0 => 6,
                    1 => 8,
                    2 => 10,
                    _ => 1
                };

                var vertexCount = vertArray.Count / vertexStep;
                var faceCount = indArray.Count / 3;
                var mesh = new Mesh(jsonObj.meshes[meshIndex].name.Value, vertexCount, faceCount);

                // First fill the vertex array of our mesh
                for (var i = 0; i < vertexCount; i++)
                {
                    var x = (float) vertArray[i * vertexStep].Value;
                    var y = (float) vertArray[i * vertexStep + 1].Value;
                    var z = (float) vertArray[i * vertexStep + 2].Value;
                    mesh.Vertices[i] = new Vector3(x, y, z);
                }

                // Fill face array
                for (var i = 0; i < faceCount; i++)
                {
                    var a = (int) indArray[i * 3].Value;
                    var b = (int) indArray[i * 3 + 1].Value;
                    var c = (int) indArray[i * 3 + 2].Value;
                    mesh.Faces[i] = new Face {A = a, B = b, C = c};
                }

                var position = jsonObj.meshes[meshIndex].position;
                mesh.Position = new Vector3((float) position[0].Value, (float) position[1].Value,
                    (float) position[2].Value);
                meshes.Add(mesh);
            }

            return meshes.ToArray();
        }
    }
}
