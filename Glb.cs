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
using System.Text;

namespace QuickLook.Plugin.VrmViewer;

internal sealed class Glb
{
    public Dictionary<string, object> Root { get; }
    private readonly byte[] _file;
    private readonly int _binStart;

    private Glb(Dictionary<string, object> root, byte[] file, int binStart)
    {
        Root = root;
        _file = file;
        _binStart = binStart;
    }

    public static Glb Load(string path)
    {
        var b = File.ReadAllBytes(path);
        if (b.Length < 20 || BitConverter.ToUInt32(b, 0) != 0x46546C67) // "glTF"
            throw new InvalidDataException("Not a binary glTF file.");

        var jsonLen = BitConverter.ToInt32(b, 12);
        var json = Encoding.UTF8.GetString(b, 20, jsonLen);
        var binStart = 20 + jsonLen + 8;
        return new Glb((Dictionary<string, object>)Json.Parse(json), b, binStart);
    }

    public static Dictionary<string, object> ReadJsonOnly(string path)
    {
        using var fs = File.OpenRead(path);
        var head = new byte[20];
        if (fs.Read(head, 0, 20) != 20 || BitConverter.ToUInt32(head, 0) != 0x46546C67)
            throw new InvalidDataException("Not a binary glTF file.");
        var json = new byte[BitConverter.ToInt32(head, 12)];
        fs.Read(json, 0, json.Length);
        return (Dictionary<string, object>)Json.Parse(Encoding.UTF8.GetString(json));
    }

    public List<object> Arr(string key) => Root.TryGetValue(key, out var v) ? v as List<object> ?? new() : new();

    public Dictionary<string, object> Item(string key, int index) => (Dictionary<string, object>)Arr(key)[index];

    public ArraySegment<byte> View(int bufferView)
    {
        var bv = Item("bufferViews", bufferView);
        var offset = J.Int(bv, "byteOffset", 0);
        return new ArraySegment<byte>(_file, _binStart + offset, J.Int(bv, "byteLength", 0));
    }

    public float[] Floats(int accessor, out int comps)
    {
        var a = Item("accessors", accessor);
        comps = Components((string)a["type"]);
        var count = J.Int(a, "count", 0);
        var result = new float[count * comps];
        if (!a.ContainsKey("bufferView")) return result;

        var type = J.Int(a, "componentType", 5126);
        var normalized = a.TryGetValue("normalized", out var n) && n is true;
        var bv = Item("bufferViews", J.Int(a, "bufferView", 0));
        var size = ComponentSize(type);
        var stride = J.Int(bv, "byteStride", comps * size);
        var start = _binStart + J.Int(bv, "byteOffset", 0) + J.Int(a, "byteOffset", 0);

        for (var i = 0; i < count; i++)
        {
            var p = start + i * stride;
            for (var c = 0; c < comps; c++, p += size)
            {
                result[i * comps + c] = type switch
                {
                    5126 => BitConverter.ToSingle(_file, p),
                    5123 => normalized ? BitConverter.ToUInt16(_file, p) / 65535f : BitConverter.ToUInt16(_file, p),
                    5121 => normalized ? _file[p] / 255f : _file[p],
                    5122 => normalized ? Math.Max(BitConverter.ToInt16(_file, p) / 32767f, -1f) : BitConverter.ToInt16(_file, p),
                    5120 => normalized ? Math.Max((sbyte)_file[p] / 127f, -1f) : (sbyte)_file[p],
                    _ => BitConverter.ToUInt32(_file, p),
                };
            }
        }
        return result;
    }

    public int[] Indices(int accessor)
    {
        var a = Item("accessors", accessor);
        var count = J.Int(a, "count", 0);
        var type = J.Int(a, "componentType", 5125);
        var bv = Item("bufferViews", J.Int(a, "bufferView", 0));
        var size = ComponentSize(type);
        var start = _binStart + J.Int(bv, "byteOffset", 0) + J.Int(a, "byteOffset", 0);

        var result = new int[count];
        for (var i = 0; i < count; i++)
        {
            var p = start + i * size;
            result[i] = type switch
            {
                5121 => _file[p],
                5123 => BitConverter.ToUInt16(_file, p),
                _ => (int)BitConverter.ToUInt32(_file, p),
            };
        }
        return result;
    }

    private static int Components(string type) => type switch
    {
        "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 1,
    };

    private static int ComponentSize(int type) => type switch
    {
        5120 or 5121 => 1, 5122 or 5123 => 2, _ => 4,
    };
}

// helpers for the untyped JSON tree
internal static class J
{
    public static Dictionary<string, object> Obj(Dictionary<string, object> d, string key) =>
        d != null && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

    public static int Int(Dictionary<string, object> d, string key, int fallback) =>
        d != null && d.TryGetValue(key, out var v) && v is double n ? (int)n : fallback;

    public static double[] Nums(Dictionary<string, object> d, string key) =>
        d != null && d.TryGetValue(key, out var v) && v is List<object> l ? l.Select(Convert.ToDouble).ToArray() : null;

    public static string Str(Dictionary<string, object> d, string key) =>
        d != null && d.TryGetValue(key, out var v) ? v as string : null;
}
