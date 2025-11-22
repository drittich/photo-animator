using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PhotoAnimator.App.Controls
{
    /// <summary>
    /// A double-buffered image display control for rapidly swapping <see cref="BitmapSource"/> frames
    /// while minimizing flicker. It maintains two underlying Image elements; incoming frames are
    /// applied to the hidden buffer and then visibility is atomically swapped on the UI thread.
    /// This avoids tearing/flicker that can occur when updating a single Image.Source at higher
    /// playback rates (6–24 FPS). Frames are assumed to be already frozen upstream.
    /// </summary>
    public partial class DoubleBufferedImageControl : System.Windows.Controls.UserControl
    {
        public static readonly DependencyProperty CurrentFrameProperty =
            DependencyProperty.Register(
                nameof(CurrentFrame),
                typeof(BitmapSource),
                typeof(DoubleBufferedImageControl),
                new PropertyMetadata(null, OnFrameChanged));

        private bool _usingA = true;
        private readonly System.Windows.Controls.Image _imageA;
        private readonly System.Windows.Controls.Image _imageB;

        public DoubleBufferedImageControl()
        {
            InitializeComponent();
            _imageA = PART_ImageA;
            _imageB = PART_ImageB;
        }

        /// <summary>
        /// Gets or sets the current frame to display. Setting this property will perform a
        /// double-buffered swap to minimize flicker.
        /// </summary>
        public BitmapSource? CurrentFrame
        {
            get => (BitmapSource?)GetValue(CurrentFrameProperty);
            set => SetValue(CurrentFrameProperty, value);
        }

        /// <summary>
        /// Updates the displayed frame using the double-buffer logic. If called from a non-UI
        /// thread, the assignment is marshaled to the UI Dispatcher. Frames are assumed frozen.
        /// </summary>
        /// <param name="frame">The new frame (may be null to ignore).</param>
        public void UpdateFrame(BitmapSource? frame)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(() => UpdateFrame(frame), DispatcherPriority.Render);
                return;
            }

            if (frame is null)
                return;

            CurrentFrame = frame;
        }

        /// <summary>
        /// Attempts to get the currently displayed BitmapSource.
        /// </summary>
        public BitmapSource? TryGetDisplayed()
        {
            return _usingA ? _imageA.Source as BitmapSource : _imageB.Source as BitmapSource;
        }

        private static void OnFrameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DoubleBufferedImageControl control)
                return;

            if (!control.Dispatcher.CheckAccess())
            {
                control.Dispatcher.InvokeAsync(
                    () => OnFrameChanged(control, e),
                    DispatcherPriority.Render);
                return;
            }

            var newFrame = e.NewValue as BitmapSource;
            if (newFrame is null)
                return;

            // Determine target buffer (the one currently hidden)
            var target = control._usingA ? control._imageB : control._imageA;
            var other  = control._usingA ? control._imageA : control._imageB;

            target.Source = newFrame;
            target.Visibility = Visibility.Visible;
            other.Visibility = Visibility.Collapsed;

            control._usingA = !control._usingA;
        }
    }
}