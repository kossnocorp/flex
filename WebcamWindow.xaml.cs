using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Windows.Media.Playback;
using Windows.Devices.Enumeration;
using System.Threading.Tasks;
using Windows.Graphics;
using System;
using System.Diagnostics;
using Microsoft.UI.Input;

namespace Flex
{
    public sealed partial class WebcamWindow : Window
    {
        MediaPlayer _player;
        Webcam webcam = new Webcam();
        WindowSizing _sizing;
        DispatcherTimer _timer;
        TimeSpan _recordingTime;
        public event EventHandler StopRecordingRequested;

        public WebcamWindow()
        {
            this.InitializeComponent();
            this.ConfigureWindow();
        }

        public async Task SetDevice(DeviceInformation device)
        {
            this.EnsurePlayer();
            _player.Source = await webcam.SetDevice(device);
            _player.Play();
        }

        void EnsurePlayer()
        {
            if (_player != null)
                return;
            _player = new MediaPlayer();
            WebcamFeed.SetMediaPlayer(_player);
        }

        void ConfigureWindow()
        {
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(WebcamContainer);

            var window = AppWindow.GetFromWindowId(WindowInterop.GetWindowId(this));
            window.Resize(new SizeInt32(600, 600));

            _sizing = new WindowSizing(this, (300, 1200), (1, 1));

            var presenter = window.Presenter as OverlappedPresenter;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsMaximizable = false;

            WindowInterop.StayOnTop(this);

            this.Activated += WebcamWindow_Activated;
        }

        private void WebcamWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            // Remove the event handler to ensure it only runs once
            this.Activated -= WebcamWindow_Activated;

            // Set up the event handlers for interactive area
            StopButton.Loaded += (s, e) => SetInteractiveArea();
            this.SizeChanged += (s, e) => SetInteractiveArea();

            // Initial setup of interactive area
            SetInteractiveArea();
        }

        private void SetInteractiveArea()
        {
            if (WebcamContainer.XamlRoot == null)
                return;

            var dpiScale = WebcamContainer.XamlRoot.RasterizationScale;

            var transform = StopButton.TransformToVisual(null);
            var buttonRect = transform.TransformBounds(
                new Windows.Foundation.Rect(0, 0, StopButton.ActualWidth, StopButton.ActualHeight)
            );

            // Increase the interactive area size
            var interactiveArea = new RectInt32(
                _X: (int)((buttonRect.X - 20) * dpiScale),
                _Y: (int)((buttonRect.Y - 20) * dpiScale),
                _Width: (int)((buttonRect.Width + 40) * dpiScale),
                _Height: (int)((buttonRect.Height + 40) * dpiScale)
            );

            var appWindow = AppWindow.GetFromWindowId(WindowInterop.GetWindowId(this));
            var inputNonClientPointerSource = InputNonClientPointerSource.GetForWindowId(
                appWindow.Id
            );
            inputNonClientPointerSource.SetRegionRects(
                NonClientRegionKind.Passthrough,
                new[] { interactiveArea }
            );
        }

        void InitializeTimer()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
        }

        void Timer_Tick(object sender, object e)
        {
            _recordingTime = _recordingTime.Add(TimeSpan.FromSeconds(1));
            UpdateTimerDisplay();
        }

        void UpdateTimerDisplay()
        {
            var timeFormat = _recordingTime.TotalMinutes >= 60 ? @"hh\:mm\:ss" : @"mm\:ss";
            TimerText.Text = _recordingTime.ToString(timeFormat);
        }

        public void StartRecording()
        {
            if (_timer == null)
            {
                InitializeTimer();
            }

            _recordingTime = TimeSpan.Zero;
            UpdateTimerDisplay();
            TimerBorder.Visibility = Visibility.Visible;
            StopButton.Visibility = Visibility.Visible;
            _timer.Start();
        }

        public void StopRecording()
        {
            if (_timer != null)
                _timer.Stop();
            TimerBorder.Visibility = Visibility.Collapsed;
            StopButton.Visibility = Visibility.Collapsed;
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("StopButton_Click event fired");
            StopRecordingRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
