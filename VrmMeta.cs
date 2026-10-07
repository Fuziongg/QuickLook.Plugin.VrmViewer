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

using System.Collections.Generic;
using System.Linq;

namespace QuickLook.Plugin.VrmViewer;

internal sealed class VrmMeta
{
    public string Name, Author, License, Spec;

    // VRM 0.x models face -Z, VRM 1.0 models face +Z
    public bool FacesMinusZ;

    public static VrmMeta From(Dictionary<string, object> root)
    {
        var ext = J.Obj(root, "extensions");
        var v1 = J.Obj(J.Obj(ext, "VRMC_vrm"), "meta");
        if (v1 != null)
        {
            var authors = v1.TryGetValue("authors", out var a) && a is List<object> l
                ? string.Join(", ", l.OfType<string>().Where(Useful))
                : null;
            return new VrmMeta
            {
                Name = Clean(J.Str(v1, "name")),
                Author = Clean(authors),
                License = LicenseV1(J.Str(v1, "licenseUrl"), J.Str(v1, "otherLicenseUrl")),
                Spec = "VRM 1.0",
            };
        }

        var v0 = J.Obj(J.Obj(ext, "VRM"), "meta");
        return new VrmMeta
        {
            Name = Clean(J.Str(v0, "title")),
            Author = Clean(J.Str(v0, "author")),
            License = LicenseV0(J.Str(v0, "licenseName"), J.Str(v0, "otherLicenseUrl")),
            Spec = v0 != null ? "VRM 0.x" : "glTF",
            FacesMinusZ = v0 != null,
        };
    }

    private static string LicenseV0(string name, string other)
    {
        if (!Useful(name)) return Clean(other);
        if (name == "Other") return Clean(other) ?? "Other";
        // CC_BY_NC_SA -> CC BY-NC-SA, Redistribution_Prohibited -> Redistribution Prohibited
        if (name.StartsWith("CC_")) return "CC " + name.Substring(3).Replace('_', '-');
        return name.Replace('_', ' ');
    }

    private static string LicenseV1(string url, string other)
    {
        if (Useful(url) && url.Contains("vrm.dev/licenses/1.0")) return "VRM Public License 1.0";
        return Clean(url) ?? Clean(other);
    }

    // exporters often write "undefined" or "" for empty fields
    private static bool Useful(string s) => !string.IsNullOrWhiteSpace(s) && s != "undefined" && s != "null";

    private static string Clean(string s) => Useful(s) ? s.Trim() : null;
}
