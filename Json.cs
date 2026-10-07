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
using System.Globalization;
using System.Text;

namespace QuickLook.Plugin.VrmViewer;

// Minimal JSON reader: objects become Dictionary<string, object>, arrays List<object>, numbers double.
// JavaScriptSerializer would parse floats as decimal and choke on tiny exponents found in some exporters.
internal sealed class Json
{
    private readonly string _s;
    private int _i;

    private Json(string s) => _s = s;

    public static object Parse(string s) => new Json(s).Value();

    private object Value()
    {
        Ws();
        switch (_s[_i])
        {
            case '{': return Obj();
            case '[': return Arr();
            case '"': return Str();
            case 't': _i += 4; return true;
            case 'f': _i += 5; return false;
            case 'n': _i += 4; return null;
            default: return Num();
        }
    }

    private Dictionary<string, object> Obj()
    {
        var d = new Dictionary<string, object>();
        _i++;
        Ws();
        if (_s[_i] == '}') { _i++; return d; }
        while (true)
        {
            Ws();
            var k = Str();
            Ws();
            _i++; // ':'
            d[k] = Value();
            Ws();
            if (_s[_i++] == '}') return d;
        }
    }

    private List<object> Arr()
    {
        var l = new List<object>();
        _i++;
        Ws();
        if (_s[_i] == ']') { _i++; return l; }
        while (true)
        {
            l.Add(Value());
            Ws();
            if (_s[_i++] == ']') return l;
        }
    }

    private string Str()
    {
        _i++;
        var sb = new StringBuilder();
        while (true)
        {
            var c = _s[_i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            c = _s[_i++];
            switch (c)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    sb.Append((char)Convert.ToInt32(_s.Substring(_i, 4), 16));
                    _i += 4;
                    break;
                default: sb.Append(c); break;
            }
        }
    }

    private double Num()
    {
        var start = _i;
        while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
        return double.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private void Ws()
    {
        while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
    }
}
