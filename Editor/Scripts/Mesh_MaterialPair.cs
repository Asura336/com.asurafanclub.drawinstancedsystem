using UnityEngine;

namespace Com.Rendering.Editor
{
    public readonly struct Mesh_MaterialPair
    {
        public readonly Mesh mesh;
        public readonly Material material;

        public Mesh_MaterialPair(Mesh mesh, Material material)
        {
            this.mesh = mesh;
            this.material = material;
        }

        public readonly void Deconstruct(out Mesh mesh, out Material material)
        {
            mesh = this.mesh;
            material = this.material;
        }

        public override int GetHashCode()
        {
            return InternalTools.CombineHash(mesh.GetHashCode(), material.GetHashCode());
        }

        public override string ToString() => $"({mesh.name}, {material.shader.name})";

        public static implicit operator Mesh_MaterialPair((Mesh mesh, Material material) t) => new Mesh_MaterialPair(t.mesh, t.material);
    }
}