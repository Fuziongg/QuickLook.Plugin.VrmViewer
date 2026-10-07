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

using QuickLook.Common.Plugin;
using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace QuickLook.Plugin.VrmViewer;

public sealed class Plugin : IViewer
{
    private VrmPanel _panel;

    public int Priority => 0;

    public void Init()
    {
    }

    public bool CanHandle(string path) =>
        !Directory.Exists(path) && path.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase);

    public void Prepare(string path, ContextObject context)
    {
        context.PreferredSize = new Size(760, 860);
    }

    public void View(string path, ContextObject context)
    {
        context.Title = Path.GetFileName(path);

        VrmMeta meta;
        try { meta = VrmMeta.From(Glb.ReadJsonOnly(path)); }
        catch { meta = new VrmMeta(); }

        var panel = _panel = new VrmPanel(meta, Path.GetFileNameWithoutExtension(path));
        context.ViewerContent = panel;
        context.IsBusy = true;

        var dispatcher = panel.Dispatcher;
        // big avatars take a few seconds to parse; keep the window responsive meanwhile
        var worker = new Thread(() =>
        {
            try
            {
                var model = ModelBuilder.Build(Glb.Load(path), out var triangles);
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_panel != panel) return;
                    panel.ShowModel(model, triangles);
                    context.IsBusy = false;
                }));
            }
            catch (Exception ex)
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_panel != panel) return;
                    panel.ShowError("Couldn't load this model.\n\n" + ex.Message);
                    context.IsBusy = false;
                }));
            }
        }) { IsBackground = true };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    public void Cleanup()
    {
        _panel?.StopSpin();
        _panel = null;
    }
}
