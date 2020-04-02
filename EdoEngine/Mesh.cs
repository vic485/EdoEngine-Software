using SharpDX;

namespace EdoEngine
{
    public class Mesh
    {
        public string Name { get; set; }
        public Vector3[] Vertices { get; private set; }
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }

        public Mesh(string name, int vertexCount)
        {
            Vertices = new Vector3[vertexCount];
            Name = name;
        }
    }
}
