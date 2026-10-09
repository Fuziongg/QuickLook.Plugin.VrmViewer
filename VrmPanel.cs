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

using HelixToolkit.Wpf;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace QuickLook.Plugin.VrmViewer;

internal sealed class VrmPanel : Grid
{
    private readonly HelixViewport3D _viewport;
    private readonly ModelVisual3D _modelVisual = new();
    private readonly AxisAngleRotation3D _spin = new(new Vector3D(0, 1, 0), 0);
    private readonly TextBlock _details;
    private readonly TextBlock _error;
    private readonly VrmMeta _meta;
    private Rect3D _frame = Rect3D.Empty;
    private bool _touched;

    public VrmPanel(VrmMeta meta, string fileName)
    {
        _meta = meta;
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _viewport = new HelixViewport3D
        {
            Background = Brushes.Transparent,
            ShowViewCube = false,
            ShowCoordinateSystem = false,
            ShowCameraTarget = false,
            IsHeadLightEnabled = false,
            ModelUpDirection = new Vector3D(0, 1, 0),
            RotateGesture = new MouseGesture(MouseAction.LeftClick),
            Camera = new PerspectiveCamera(new Point3D(0, 1, 4), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 30),
        };
        _viewport.Children.Add(new ModelVisual3D
        {
            Content = new Model3DGroup
            {
                Children =
                {
                    new AmbientLight(Color.FromRgb(205, 205, 205)),
                    new DirectionalLight(Color.FromRgb(80, 80, 80), new Vector3D(-0.4, -0.6, -1)),
                },
            },
        });
        _viewport.Children.Add(_modelVisual);
        // once the user grabs the model, stop turning it so it stays where they put it
        _viewport.PreviewMouseDown += (_, _) =>
        {
            _touched = true;
            StopSpin();
        };
        // the window often resizes right after the content arrives; refit until the user takes over
        _viewport.SizeChanged += (_, _) => Fit();
        Children.Add(_viewport);

        _error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24), Visibility = Visibility.Collapsed };
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WindowTextForeground");
        Children.Add(_error);

        var info = new StackPanel { Margin = new Thickness(20, 8, 20, 16) };
        SetRow(info, 1);
        info.Children.Add(Text(meta.Name ?? fileName, 20, FontWeights.SemiBold, 1));
        if (meta.Author != null) info.Children.Add(Text("by " + meta.Author, 13, FontWeights.Normal, 0.85));
        info.Children.Add(Text("License: " + (meta.License ?? "not specified"), 13, FontWeights.Normal, 0.85));
        _details = Text(meta.Spec, 11, FontWeights.Normal, 0.55);
        _details.Margin = new Thickness(0, 4, 0, 0);
        info.Children.Add(_details);
        Children.Add(info);
    }

    private static TextBlock Text(string s, double size, FontWeight weight, double opacity)
    {
        var t = new TextBlock
        {
            Text = s,
            FontSize = size,
            FontWeight = weight,
            Opacity = opacity,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = s,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, "WindowTextForeground");
        return t;
    }

    public void ShowModel(Model3DGroup model, int triangles)
    {
        var b = model.Bounds;
        var center = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 2);
        // a box that holds the model at any turntable angle, so the framing doesn't depend on when it's measured
        var r = Math.Sqrt(b.SizeX * b.SizeX + b.SizeZ * b.SizeZ) / 2;
        _frame = new Rect3D(center.X - r, b.Y, center.Z - r, 2 * r, b.SizeY, 2 * r);
        _modelVisual.Content = new Model3DGroup
        {
            Children = { model },
            Transform = new RotateTransform3D(_spin, center),
        };
        _details.Text = _meta.Spec + "  ·  " + triangles.ToString("N0") + " triangles";

        var start = _meta.FacesMinusZ ? 180 : 0;
        _spin.Angle = start;
        _spin.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(start, start + 360, TimeSpan.FromSeconds(14))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        });

        if (_viewport.IsLoaded) Fit();
        else _viewport.Loaded += (_, _) => Fit();
    }

    private void Fit()
    {
        if (_touched || _frame.IsEmpty || _viewport.ActualWidth < 1) return;
        _viewport.ZoomExtents(_frame);
    }

    public void ShowError(string message)
    {
        _viewport.Visibility = Visibility.Collapsed;
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }

    public void StopSpin()
    {
        var angle = _spin.Angle;
        _spin.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
        _spin.Angle = angle;
    }
}
