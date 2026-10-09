// Copyright © 2026 Fuziongg
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace QuickLook.Plugin.VrmViewer;

// Turns the glTF scene into a frozen WPF Model3DGroup, so it can be built off the UI thread.
internal sealed class ModelBuilder
{
    private const int MaxTextureSize = 2048;

    private readonly Glb _glb;
    private readonly Dictionary<int, (BitmapSource Bmp, double Coverage)> _images = new();
    private readonly Dictionary<int, (Material Mat, double Order, bool DoubleSided)> _materials = new();
    private readonly List<(GeometryModel3D Model, double Order)> _parts = new();
    private readonly Dictionary<int, Matrix3D> _world = new();
    private readonly List<(int Node, int Mesh)> _meshNodes = new();

    public int Triangles { get; private set; }

    private ModelBuilder(Glb glb) => _glb = glb;

    public static Model3DGroup Build(Glb glb, out int triangles)
    {
        var b = new ModelBuilder(glb);
        var scenes = glb.Arr("scenes");
        var scene = scenes.Count > 0 ? (Dictionary<string, object>)scenes[J.Int(glb.Root, "scene", 0)] : null;
        var roots = J.Nums(scene, "nodes") ?? Array.Empty<double>();
        foreach (var n in roots) b.Node((int)n, Matrix3D.Identity);
        // all world matrices must be known before skinning, joints can come after the mesh in the tree
        foreach (var (node, mesh) in b._meshNodes) b.Mesh(mesh, node);

        var group = new Model3DGroup();
        foreach (var p in b._parts.OrderBy(p => p.Order)) group.Children.Add(p.Model);
        group.Freeze();
        triangles = b.Triangles;
        return group;
    }

    private void Node(int index, Matrix3D parent)
    {
        var node = _glb.Item("nodes", index);
        var world = Local(node) * parent;
        _world[index] = world;
        if (node.TryGetValue("mesh", out var mesh)) _meshNodes.Add((index, (int)(double)mesh));

        foreach (var c in J.Nums(node, "children") ?? Array.Empty<double>())
            Node((int)c, world);
    }

    private static Matrix3D Local(Dictionary<string, object> node)
    {
        var m = J.Nums(node, "matrix");
        if (m != null)
            return new Matrix3D(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7],
                m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);

        var result = Matrix3D.Identity;
        var s = J.Nums(node, "scale");
        var r = J.Nums(node, "rotation");
        var t = J.Nums(node, "translation");
        if (s != null) result.Scale(new Vector3D(s[0], s[1], s[2]));
        if (r != null) result.Rotate(new Quaternion(r[0], r[1], r[2], r[3]));
        if (t != null) result.Translate(new Vector3D(t[0], t[1], t[2]));
        return result;
    }

    private void Mesh(int index, int nodeIndex)
    {
        var node = _glb.Item("nodes", nodeIndex);
        var transform = _world[nodeIndex];
        var joints = node.ContainsKey("skin") ? JointMatrices(J.Int(node, "skin", 0)) : null;
        var mesh = _glb.Item("meshes", index);
        foreach (Dictionary<string, object> prim in (List<object>)mesh["primitives"])
        {
            if (J.Int(prim, "mode", 4) != 4) continue;
            var attrs = J.Obj(prim, "attributes");
            if (attrs == null || !attrs.ContainsKey("POSITION")) continue;

            var pos = _glb.Floats(J.Int(attrs, "POSITION", 0), out _);
            var normals = attrs.ContainsKey("NORMAL") ? _glb.Floats(J.Int(attrs, "NORMAL", 0), out _) : null;
            var mat = Material(J.Int(prim, "material", -1), out var uvSet, out var uvTransform);
            var uvs = attrs.ContainsKey("TEXCOORD_" + uvSet) ? _glb.Floats(J.Int(attrs, "TEXCOORD_" + uvSet, 0), out _) : null;
            var vertexCount = pos.Length / 3;
            var indices = prim.ContainsKey("indices") ? _glb.Indices(J.Int(prim, "indices", 0)) : Sequence(vertexCount);

            float[] jointIds = null, weights = null;
            if (joints != null && attrs.ContainsKey("JOINTS_0") && attrs.ContainsKey("WEIGHTS_0"))
            {
                jointIds = _glb.Floats(J.Int(attrs, "JOINTS_0", 0), out _);
                weights = _glb.Floats(J.Int(attrs, "WEIGHTS_0", 0), out _);
            }

            var geo = new MeshGeometry3D();
            var points = new Point3DCollection(vertexCount);
            var nc = normals != null ? new Vector3DCollection(vertexCount) : null;
            for (var i = 0; i < vertexCount; i++)
            {
                var m = jointIds != null ? Skin(joints, jointIds, weights, i) : transform;
                points.Add(m.Transform(new Point3D(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2])));
                if (nc != null)
                {
                    var n = m.Transform(new Vector3D(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2]));
                    n.Normalize();
                    nc.Add(n);
                }
            }
            geo.Positions = points;
            if (nc != null) geo.Normals = nc;

            if (uvs != null)
            {
                var tc = new PointCollection(vertexCount);
                for (var i = 0; i < vertexCount; i++)
                    tc.Add(new Point(uvs[i * 2] * uvTransform[2] + uvTransform[0], uvs[i * 2 + 1] * uvTransform[3] + uvTransform[1]));
                geo.TextureCoordinates = tc;
            }

            var ic = new Int32Collection(indices.Length);
            foreach (var i in indices) ic.Add(i);
            geo.TriangleIndices = ic;
            Triangles += indices.Length / 3;

            var model = new GeometryModel3D(geo, mat.Mat);
            if (mat.DoubleSided) model.BackMaterial = mat.Mat;
            _parts.Add((model, mat.Order));
        }
    }

    // rest pose of a skinned mesh: jointWorld * inverseBind per joint, blended by the vertex weights
    private Matrix3D[] JointMatrices(int skinIndex)
    {
        var skin = _glb.Item("skins", skinIndex);
        var ids = J.Nums(skin, "joints") ?? Array.Empty<double>();
        var inv = skin.ContainsKey("inverseBindMatrices") ? _glb.Floats(J.Int(skin, "inverseBindMatrices", 0), out _) : null;
        var result = new Matrix3D[ids.Length];
        for (var j = 0; j < ids.Length; j++)
        {
            var ib = Matrix3D.Identity;
            if (inv != null)
            {
                var o = j * 16;
                ib = new Matrix3D(inv[o], inv[o + 1], inv[o + 2], inv[o + 3], inv[o + 4], inv[o + 5], inv[o + 6], inv[o + 7],
                    inv[o + 8], inv[o + 9], inv[o + 10], inv[o + 11], inv[o + 12], inv[o + 13], inv[o + 14], inv[o + 15]);
            }
            result[j] = ib * (_world.TryGetValue((int)ids[j], out var w) ? w : Matrix3D.Identity);
        }
        return result;
    }

    private static Matrix3D Skin(Matrix3D[] joints, float[] ids, float[] weights, int vertex)
    {
        double m11 = 0, m12 = 0, m13 = 0, m21 = 0, m22 = 0, m23 = 0, m31 = 0, m32 = 0, m33 = 0, ox = 0, oy = 0, oz = 0, total = 0;
        for (var k = 0; k < 4; k++)
        {
            var w = weights[vertex * 4 + k];
            var j = (int)ids[vertex * 4 + k];
            if (w <= 0 || j >= joints.Length) continue;
            var m = joints[j];
            m11 += w * m.M11; m12 += w * m.M12; m13 += w * m.M13;
            m21 += w * m.M21; m22 += w * m.M22; m23 += w * m.M23;
            m31 += w * m.M31; m32 += w * m.M32; m33 += w * m.M33;
            ox += w * m.OffsetX; oy += w * m.OffsetY; oz += w * m.OffsetZ;
            total += w;
        }
        if (total <= 0) return Matrix3D.Identity;
        return new Matrix3D(m11 / total, m12 / total, m13 / total, 0, m21 / total, m22 / total, m23 / total, 0,
            m31 / total, m32 / total, m33 / total, 0, ox / total, oy / total, oz / total, 1);
    }

    private static int[] Sequence(int n)
    {
        var r = new int[n];
        for (var i = 0; i < n; i++) r[i] = i;
        return r;
    }

    // uvTransform = offsetU, offsetV, scaleU, scaleV (KHR_texture_transform, rotation ignored)
    private (Material Mat, double Order, bool DoubleSided) Material(int index, out int uvSet, out double[] uvTransform)
    {
        uvSet = 0;
        uvTransform = new double[] { 0, 0, 1, 1 };
        if (index < 0) return (Solid(Colors.LightGray), 0, false);

        var m = _glb.Item("materials", index);
        var pbr = J.Obj(m, "pbrMetallicRoughness");
        var texInfo = J.Obj(pbr, "baseColorTexture");
        if (texInfo != null)
        {
            uvSet = J.Int(texInfo, "texCoord", 0);
            var tt = J.Obj(J.Obj(texInfo, "extensions"), "KHR_texture_transform");
            var off = J.Nums(tt, "offset");
            var sc = J.Nums(tt, "scale");
            if (off != null) { uvTransform[0] = off[0]; uvTransform[1] = off[1]; }
            if (sc != null) { uvTransform[2] = sc[0]; uvTransform[3] = sc[1]; }
        }

        if (_materials.TryGetValue(index, out var cached)) return cached;

        var alpha = J.Str(m, "alphaMode") ?? "OPAQUE";
        var doubleSided = m.TryGetValue("doubleSided", out var ds) && ds is true;

        Material mat = null;
        double coverage = 1;
        if (texInfo != null)
        {
            var (img, cov) = Image(J.Int(texInfo, "index", -1));
            if (img != null)
            {
                coverage = cov;
                // glTF says OPAQUE ignores the texture's alpha; WPF would blend it anyway
                if (alpha == "OPAQUE" && cov < 1)
                {
                    img = new FormatConvertedBitmap(img, PixelFormats.Bgr32, null, 0);
                    img.Freeze();
                }
                var brush = new ImageBrush(img)
                {
                    // texture coordinates are absolute 0..1, not relative to the mesh's UV bounds
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, 1, 1),
                    TileMode = TileMode.Tile,
                    Stretch = Stretch.Fill,
                };
                brush.Freeze();
                mat = new DiffuseMaterial(brush);
                mat.Freeze();
            }
        }

        if (mat == null)
        {
            var f = J.Nums(pbr, "baseColorFactor") ?? new double[] { 1, 1, 1, 1 };
            mat = Solid(Color.FromArgb((byte)(f[3] * 255), Srgb(f[0]), Srgb(f[1]), Srgb(f[2])));
        }

        // WPF writes depth even for fully transparent pixels and has no alpha test, so a see-through
        // part drawn first hides whatever is behind it (e.g. invisible tights hiding the legs).
        // Draw opaque parts first, then cut-out ones from most to least covered, then blended ones.
        var order = alpha == "OPAQUE" ? 0 : (alpha == "BLEND" ? 2 : 1) + (1 - coverage);
        return _materials[index] = (mat, order, doubleSided);
    }

    private static byte Srgb(double linear) => (byte)Math.Round(Math.Pow(Math.Max(0, Math.Min(1, linear)), 1 / 2.2) * 255);

    private static Material Solid(Color c)
    {
        var m = new DiffuseMaterial(new SolidColorBrush(c));
        m.Freeze();
        return m;
    }

    private (BitmapSource Bmp, double Coverage) Image(int texture)
    {
        if (texture < 0) return (null, 1);
        var source = J.Int(_glb.Item("textures", texture), "source", -1);
        if (source < 0) return (null, 1);
        if (_images.TryGetValue(source, out var cached)) return cached;

        BitmapSource bmp = null;
        double coverage = 1;
        try
        {
            var img = _glb.Item("images", source);
            var view = _glb.View(J.Int(img, "bufferView", -1));
            using var ms = new MemoryStream(view.Array, view.Offset, view.Count, false);
            var frame = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            bmp = frame;
            var max = Math.Max(frame.PixelWidth, frame.PixelHeight);
            if (max > MaxTextureSize)
            {
                var s = (double)MaxTextureSize / max;
                bmp = new TransformedBitmap(frame, new ScaleTransform(s, s));
            }
            bmp.Freeze();
            coverage = Coverage(frame);
        }
        catch
        {
            // a broken or unsupported texture (e.g. KTX2) just leaves the part untextured
        }

        return _images[source] = (bmp, coverage);
    }

    // share of the texture that's mostly opaque, sampled on a 64px thumbnail
    private static double Coverage(BitmapSource frame)
    {
        var small = new TransformedBitmap(frame, new ScaleTransform(64.0 / frame.PixelWidth, 64.0 / frame.PixelHeight));
        var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
        var px = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(px, bgra.PixelWidth * 4, 0);
        var opaque = 0;
        for (var i = 3; i < px.Length; i += 4)
            if (px[i] >= 128) opaque++;
        return (double)opaque / (px.Length / 4);
    }
}
