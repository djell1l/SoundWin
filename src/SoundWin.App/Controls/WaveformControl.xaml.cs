using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SoundWin.Core.Models;

namespace SoundWin.App.Controls;

public sealed partial class WaveformControl : UserControl
{
    private static readonly SolidColorBrush ActiveBrush = new(ColorHelper.FromArgb(255, 255, 85, 0)); // #FF5500
    private static readonly SolidColorBrush InactiveBrush = new(ColorHelper.FromArgb(255, 80, 80, 85));
    private static readonly SolidColorBrush HoverBrush = new(ColorHelper.FromArgb(255, 255, 120, 50));

    private readonly List<Rectangle> _bars = new();
    private bool _isDragging;

    public static readonly DependencyProperty WaveformProperty =
        DependencyProperty.Register(nameof(Waveform), typeof(WaveformData), typeof(WaveformControl),
            new PropertyMetadata(null, OnWaveformChanged));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(WaveformControl),
            new PropertyMetadata(0.0, OnProgressChanged));

    public WaveformData? Waveform
    {
        get => (WaveformData?)GetValue(WaveformProperty);
        set => SetValue(WaveformProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public event EventHandler<double>? SeekRequested;

    public WaveformControl()
    {
        InitializeComponent();
        try
        {
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        }
        catch { }
    }

    private static void OnWaveformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl ctrl)
        {
            ctrl.RenderWaveform();
        }
    }

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl ctrl)
        {
            ctrl.UpdateProgressColor();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderWaveform();
    }

    private void RenderWaveform()
    {
        WaveCanvas.Children.Clear();
        _bars.Clear();

        var width = ActualWidth;
        var height = ActualHeight;

        if (width <= 0 || height <= 0) return;

        const double barWidth = 2.0;
        const double gap = 1.5;
        const double step = barWidth + gap;

        int barCount = (int)(width / step);
        if (barCount <= 0) return;

        var samples = Waveform?.Samples;
        bool hasRealData = samples != null && samples.Length > 0;

        for (int i = 0; i < barCount; i++)
        {
            float normHeight;
            if (hasRealData)
            {
                int sampleIdx = (int)((double)i / barCount * samples!.Length);
                sampleIdx = Math.Clamp(sampleIdx, 0, samples.Length - 1);
                normHeight = samples[sampleIdx];
            }
            else
            {
                // Subtle aesthetic placeholder wave
                normHeight = 0.25f + 0.15f * (float)Math.Sin(i * 0.15);
            }

            double barH = Math.Max(3.0, normHeight * height);
            double top = (height - barH) / 2.0;
            double left = i * step;

            var rect = new Rectangle
            {
                Width = barWidth,
                Height = barH,
                RadiusX = 1.0,
                RadiusY = 1.0,
                Fill = InactiveBrush
            };

            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top);

            WaveCanvas.Children.Add(rect);
            _bars.Add(rect);
        }

        UpdateProgressColor();
    }

    private void UpdateProgressColor()
    {
        if (_bars.Count == 0) return;

        int activeCount = (int)(_bars.Count * Math.Clamp(Progress, 0.0, 1.0));

        for (int i = 0; i < _bars.Count; i++)
        {
            _bars[i].Fill = i <= activeCount ? ActiveBrush : InactiveBrush;
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = true;
        CapturePointer(e.Pointer);
        HandlePointerProgress(e);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            HandlePointerProgress(e);
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleasePointerCapture(e.Pointer);
            HandlePointerProgress(e);
        }
    }

    private void HandlePointerProgress(PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        var ratio = Math.Clamp(pt.Position.X / ActualWidth, 0.0, 1.0);
        Progress = ratio;
        SeekRequested?.Invoke(this, ratio);
    }
}
