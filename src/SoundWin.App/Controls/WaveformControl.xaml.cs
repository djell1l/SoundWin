using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using SoundWin.Core.Models;

namespace SoundWin.App.Controls;

public sealed partial class WaveformControl : UserControl
{
    private static readonly SolidColorBrush ActiveBrush = new(ColorHelper.FromArgb(255, 255, 85, 0)); // #FF5500
    private static readonly SolidColorBrush InactiveBrush = new(ColorHelper.FromArgb(255, 80, 80, 85));
    private static readonly SolidColorBrush CommentPinBrush = new(ColorHelper.FromArgb(255, 255, 120, 50));

    private readonly List<Rectangle> _bars = new();
    private bool _isDragging;

    public static readonly DependencyProperty WaveformProperty =
        DependencyProperty.Register(nameof(Waveform), typeof(WaveformData), typeof(WaveformControl),
            new PropertyMetadata(null, OnWaveformChanged));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(WaveformControl),
            new PropertyMetadata(0.0, OnProgressChanged));

    public static readonly DependencyProperty DurationMsProperty =
        DependencyProperty.Register(nameof(DurationMs), typeof(double), typeof(WaveformControl),
            new PropertyMetadata(0.0, OnDurationOrCommentsChanged));

    public static readonly DependencyProperty CommentsProperty =
        DependencyProperty.Register(nameof(Comments), typeof(IEnumerable<TrackComment>), typeof(WaveformControl),
            new PropertyMetadata(null, OnDurationOrCommentsChanged));

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

    public double DurationMs
    {
        get => (double)GetValue(DurationMsProperty);
        set => SetValue(DurationMsProperty, value);
    }

    public IEnumerable<TrackComment>? Comments
    {
        get => (IEnumerable<TrackComment>?)GetValue(CommentsProperty);
        set => SetValue(CommentsProperty, value);
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
            ctrl.RunOnUi(() => ctrl.RenderWaveform());
        }
    }

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl ctrl)
        {
            ctrl.RunOnUi(() => ctrl.UpdateProgressColor());
        }
    }

    private static void OnDurationOrCommentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl ctrl)
        {
            ctrl.RunOnUi(() => ctrl.RenderCommentMarkers());
        }
    }

    private void RunOnUi(Action action)
    {
        if (DispatcherQueue != null && !DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => action());
        }
        else
        {
            action();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderWaveform();
        RenderCommentMarkers();
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

    private void RenderCommentMarkers()
    {
        CommentsCanvas.Children.Clear();

        if (Comments == null || DurationMs <= 0 || ActualWidth <= 0) return;

        foreach (var comment in Comments)
        {
            double ratio = Math.Clamp(comment.TimestampMs / DurationMs, 0.0, 1.0);
            double x = ratio * ActualWidth;

            // Small circle marker with avatar preview in tooltip
            var marker = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                BorderBrush = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(1),
                Background = CommentPinBrush,
                Tag = comment
            };

            // Avatar image inside circle if available
            if (!string.IsNullOrEmpty(comment.AvatarUrl))
            {
                marker.Child = new Image
                {
                    Source = new BitmapImage(new Uri(comment.AvatarUrl)),
                    Stretch = Stretch.UniformToFill
                };
            }

            // Rich interactive Tooltip
            var tooltipStack = new StackPanel { Spacing = 4, Padding = new Thickness(4) };
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            headerStack.Children.Add(new TextBlock { Text = comment.Username, FontWeight = Microsoft.UI.Text.FontWeights.Bold, FontSize = 12 });
            headerStack.Children.Add(new TextBlock { Text = $"à {comment.FormattedTimestamp}", Opacity = 0.7, FontSize = 11 });
            tooltipStack.Children.Add(headerStack);
            tooltipStack.Children.Add(new TextBlock { Text = comment.Body, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 260 });

            ToolTipService.SetToolTip(marker, tooltipStack);

            Canvas.SetLeft(marker, Math.Max(0, x - 7));
            Canvas.SetTop(marker, ActualHeight - 15);

            CommentsCanvas.Children.Add(marker);
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