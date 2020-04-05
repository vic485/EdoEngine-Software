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
        private readonly float[] _depthBuffer;
        private WriteableBitmap _bitmap;
        private readonly int _renderWidth;
        private readonly int _renderHeight;

        public Device(WriteableBitmap bitmap)
        {
            _bitmap = bitmap;
            _renderWidth = bitmap.PixelWidth;
            _renderHeight = bitmap.PixelHeight;

            // The back buffer size is equal to the number of pixels to draw on screen
            // (width * height) * 4 (RGBA values)
            _backBuffer = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            _depthBuffer = new float[bitmap.PixelWidth * bitmap.PixelHeight];
        }

        // Called to clear the back buffer with a specific color
        public void Clear(byte r, byte g, byte b, byte a)
        {
            // Clear back buffer
            for (var i = 0; i < _backBuffer.Length; i += 4)
            {
                // BGRA is used by windows
                _backBuffer[i] = b;
                _backBuffer[i + 1] = g;
                _backBuffer[i + 2] = r;
                _backBuffer[i + 3] = a;
            }

            // Clear depth buffer
            for (var i = 0; i < _depthBuffer.Length; i++)
            {
                _depthBuffer[i] = float.MaxValue;
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
        public void PutPixel(int x, int y, float z, Color4 color)
        {
            // Since we have a 1D array for the back buffer, we need to know the equivalent cell in 1D, based on the 2D
            // coordinates on screen.
            var index = x + y * _renderWidth;
            var index4 = index * 4;

            if (_depthBuffer[index] < z)
            {
                return; // Discard
            }

            _depthBuffer[index] = z;

            _backBuffer[index4] = (byte) (color.Blue * 255);
            _backBuffer[index4 + 1] = (byte) (color.Green * 255);
            _backBuffer[index4 + 2] = (byte) (color.Red * 255);
            _backBuffer[index4 + 3] = (byte) (color.Alpha * 255);
        }

        // Project takes some 3D coordinates and transforms them in 2D coordinates using the transformation matrix
        public Vector3 Project(Vector3 coord, Matrix transMat)
        {
            // transform coordinates
            var point = Vector3.TransformCoordinate(coord, transMat);
            // Transformed coordinates will be based on coordinate system starting at the center of the screen.
            // Drawing on screen normally starts from top left. We need to transform them again to have x:0, y:0 at
            // top left.
            var x = point.X * _bitmap.PixelWidth + _bitmap.PixelWidth / 2.0f;
            var y = -point.Y * _bitmap.PixelHeight + _bitmap.PixelHeight / 2.0f;

            return new Vector3(x, y, point.Z);
        }

        // Calls PutPixel with clipping operation beforehand
        public void DrawPoint(Vector3 point, Color4 color)
        {
            // Clip what's visible on screen
            if (point.X >= 0 && point.Y >= 0 && point.X < _bitmap.PixelWidth && point.Y < _bitmap.PixelHeight)
            {
                // Draw yellow point
                PutPixel((int) point.X, (int) point.Y, point.Z, color);
            }
        }

        // Draws a line between two points using Bresenham's line algorithm
        /*public void DrawLine(Vector2 point1, Vector2 point2)
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
        }*/

        // Clamping values to keep them between min and max values
        private float Clamp(float value, float min = 0, float max = 1)
        {
            return Math.Max(min, Math.Min(value, max));
        }

        // Interpolating the value between 2 vertices, min is the starting point, max is the ending point, and gradient
        // is the percentage between the 2 points.
        private float Interpolate(float min, float max, float gradient)
        {
            return min + (max - min) * Clamp(gradient);
        }

        // Drawing a line between 2 points from left to right.
        // papb -> pcpd -- pa, pb, pc, pd must then be sorted before
        private void ProcessScanLine(int y, Vector3 pa, Vector3 pb, Vector3 pc, Vector3 pd, Color4 color)
        {
            // Thanks to current Y, we can compute the gradient to compute other values like the starting X (sx) and
            // ending X (ex) to draw between.
            // if pa.Y == pb.Y or pc.Y == pd.Y then gradient is forced to 1
            var gradient1 = pa.Y != pb.Y ? (y - pa.Y) / (pb.Y - pa.Y) : 1;
            var gradient2 = pc.Y != pd.Y ? (y - pc.Y) / (pd.Y - pc.Y) : 1;

            var sx = (int) Interpolate(pa.X, pb.X, gradient1);
            var ex = (int) Interpolate(pc.X, pd.X, gradient2);

            // Starting and ending Z
            var sz = Interpolate(pa.Z, pb.Z, gradient1);
            var ez = Interpolate(pc.Z, pd.Z, gradient2);

            // Drawing a line from left (sx) to right (ex)
            for (var x = sx; x < ex; x++)
            {
                var gradient = (x - sx) / (float) (ex - sx);

                var z = Interpolate(sz, ez, gradient);
                DrawPoint(new Vector3(x, y, z), color);
            }
        }

        public void DrawTriangle(Vector3 p1, Vector3 p2, Vector3 p3, Color4 color)
        {
            // Sorting the points in order to always have this order on screen: p1, p2, p3, with p1 always up (lowest
            // possible Y) then p2 between p1 & p3
            if (p1.Y > p2.Y)
            {
                var temp = p2;
                p2 = p1;
                p1 = temp;
            }

            if (p2.Y > p3.Y)
            {
                var temp = p2;
                p2 = p3;
                p3 = temp;
            }

            if (p1.Y > p2.Y)
            {
                var temp = p2;
                p2 = p1;
                p1 = temp;
            }

            // Inverse slopes
            float dP1P2, dP1P3;

            // https://en.wikipedia.org/wiki/Slope
            // Computing inverse slopes
            if (p2.Y - p1.Y > 0)
                dP1P2 = (p2.X - p1.X) / (p2.Y - p1.Y);
            else
                dP1P2 = 0;

            if (p3.Y - p1.Y > 0)
                dP1P3 = (p3.X - p1.X) / (p3.Y - p1.Y);
            else
                dP1P3 = 0;

            // First case where triangles are
            // P1
            // | P2
            // P3
            if (dP1P2 > dP1P3)
            {
                for (var y = (int) p1.Y; y <= (int) p3.Y; y++)
                {
                    if (y < p2.Y)
                    {
                        ProcessScanLine(y, p1, p3, p1, p2, color);
                    }
                    else
                    {
                        ProcessScanLine(y, p1, p3, p2, p3, color);
                    }
                }
            }

            // Second case where triangles are
            //    P1
            // P2 |
            //    P3
            else
            {
                for (var y = (int) p1.Y; y <= (int) p3.Y; y++)
                {
                    if (y < p2.Y)
                    {
                        ProcessScanLine(y, p1, p2, p1, p3, color);
                    }
                    else
                    {
                        ProcessScanLine(y, p2, p3, p1, p3, color);
                    }
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

                var faceIndex = 0;
                foreach (var face in mesh.Faces)
                {
                    var vertexA = mesh.Vertices[face.A];
                    var vertexB = mesh.Vertices[face.B];
                    var vertexC = mesh.Vertices[face.C];

                    var pixelA = Project(vertexA, transformationMatrix);
                    var pixelB = Project(vertexB, transformationMatrix);
                    var pixelC = Project(vertexC, transformationMatrix);

                    var color = 0.25f + faceIndex % mesh.Faces.Length * 0.75f / mesh.Faces.Length;
                    DrawTriangle(pixelA, pixelB, pixelC, new Color4(color, color, color, 1));
                    faceIndex++;
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
