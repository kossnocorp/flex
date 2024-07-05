using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.MediaProperties;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Storage;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using Windows.Media.Transcoding;
using Windows.Storage.Pickers;
using Microsoft.UI.Windowing;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Flex
{
    public sealed partial class MainWindow : Window
    {
        private MediaCapture _mediaCapture;
        private MediaPlayer _mediaPlayer;
        private bool _devicesInitialized;
        private GraphicsCaptureSession _captureSession;
        private bool _isRecording = false;

        public MainWindow()
        {
            this.InitializeComponent();
            this.Activated += MainWindow_Activated;
            Debug.WriteLine("MainWindow constructor called");

            // Make window always on top
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        }

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;

        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            Debug.WriteLine("MainWindow_Activated called");
            if (!_devicesInitialized)
            {
                await PopulateDeviceListsAsync();
                _devicesInitialized = true;
            }
        }

        private async Task PopulateDeviceListsAsync()
        {
            Debug.WriteLine("PopulateDeviceListsAsync started");

            // Populate webcam list
            var videoDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            foreach (var device in videoDevices)
            {
                WebcamComboBox.Items.Add(device);
                Debug.WriteLine($"Added video device: {device.Name}");
            }

            if (WebcamComboBox.Items.Count > 0)
            {
                WebcamComboBox.SelectedIndex = 0;
                Debug.WriteLine("Set default selected webcam");
            }

            // Populate audio list
            var audioDevices = await DeviceInformation.FindAllAsync(DeviceClass.AudioCapture);
            foreach (var device in audioDevices)
            {
                AudioComboBox.Items.Add(device);
                Debug.WriteLine($"Added audio device: {device.Name}");
            }

            if (AudioComboBox.Items.Count > 0)
            {
                AudioComboBox.SelectedIndex = 0;
                Debug.WriteLine("Set default selected microphone");
            }

            Debug.WriteLine($"Found {WebcamComboBox.Items.Count} webcam devices and {AudioComboBox.Items.Count} audio devices");
        }

        private async void WebcamComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Debug.WriteLine("WebcamComboBox_SelectionChanged called");
            await InitializeMediaCaptureAsync();
        }

        private async void AudioComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Debug.WriteLine("AudioComboBox_SelectionChanged called");
            await InitializeMediaCaptureAsync();
        }

        private async Task InitializeMediaCaptureAsync()
        {
            if (WebcamComboBox.SelectedItem is not DeviceInformation selectedVideoDevice ||
                AudioComboBox.SelectedItem is not DeviceInformation selectedAudioDevice)
            {
                return;
            }

            Debug.WriteLine($"InitializeMediaCaptureAsync started for video device: {selectedVideoDevice.Name} and audio device: {selectedAudioDevice.Name}");

            if (_mediaCapture != null)
            {
                _mediaCapture.Dispose();
                _mediaCapture = null;
                Debug.WriteLine("Disposed existing MediaCapture");
            }

            _mediaCapture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = selectedVideoDevice.Id,
                AudioDeviceId = selectedAudioDevice.Id,
                StreamingCaptureMode = StreamingCaptureMode.AudioAndVideo
            };

            try
            {
                Debug.WriteLine("Initializing MediaCapture");
                await _mediaCapture.InitializeAsync(settings);
                Debug.WriteLine("MediaCapture initialized successfully");

                if (_mediaPlayer == null)
                {
                    _mediaPlayer = new MediaPlayer();
                    WebcamFeed.SetMediaPlayer(_mediaPlayer);
                    Debug.WriteLine("Created new MediaPlayer and set it to WebcamFeed");
                }

                var frameSource = _mediaCapture.FrameSources.FirstOrDefault(source =>
                    source.Value.Info.MediaStreamType == MediaStreamType.VideoPreview ||
                    source.Value.Info.MediaStreamType == MediaStreamType.VideoRecord).Value;

                if (frameSource != null)
                {
                    Debug.WriteLine($"Found frame source: {frameSource.Info.Id}");

                    // Find a suitable format
                    var preferredFormat = frameSource.SupportedFormats.FirstOrDefault(format =>
                        format.VideoFormat.Width == 640 &&
                        format.VideoFormat.Height == 480 &&
                        format.Subtype == MediaEncodingSubtypes.Nv12);

                    if (preferredFormat != null)
                    {
                        await frameSource.SetFormatAsync(preferredFormat);
                        Debug.WriteLine($"Set video format to: {preferredFormat.Subtype} {preferredFormat.VideoFormat.Width}x{preferredFormat.VideoFormat.Height}");
                    }
                    else
                    {
                        Debug.WriteLine("Preferred format not found. Using default format.");
                    }

                    _mediaPlayer.Source = MediaSource.CreateFromMediaFrameSource(frameSource);
                    Debug.WriteLine("Set MediaPlayer source from frame source");
                    _mediaPlayer.Play();
                    Debug.WriteLine("Started MediaPlayer playback");

                    // Log some information about the frame source
                    Debug.WriteLine($"Frame source info: {frameSource.Info.Id}, {frameSource.Info.MediaStreamType}");
                    Debug.WriteLine($"Current format: {frameSource.CurrentFormat.Subtype}");
                }
                else
                {
                    Debug.WriteLine("No suitable frame source found");
                    throw new Exception("No suitable video frame source found on this device.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in InitializeMediaCaptureAsync: {ex.GetType().Name} - {ex.Message}");
                if (ex.InnerException != null)
                {
                    Debug.WriteLine($"Inner Exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                }

                ContentDialog dialog = new ContentDialog
                {
                    XamlRoot = this.Content.XamlRoot,
                    Title = "Error",
                    Content = $"An error occurred: {ex.Message}",
                    CloseButtonText = "OK"
                };

                await dialog.ShowAsync();
            }

            // After successfully initializing MediaCapture, set up screen capture
            SetupScreenCapture();
        }


        private void SetupScreenCapture()
        {
            var picker = new GraphicsCapturePicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

            // We'll start the actual capture when the user clicks the record button
        }

        private async void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRecording)
            {
                await StartRecordingAsync();
            }
            else
            {
                await StopRecordingAsync();
            }
        }

        private async Task StartRecordingAsync()
        {
            var encodingProfile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);

            var savePicker = new FileSavePicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);
            savePicker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            savePicker.FileTypeChoices.Add("MP4 files", new List<string>() { ".mp4" });
            savePicker.SuggestedFileName = "ScreenCapture";

            StorageFile file = await savePicker.PickSaveFileAsync();
            if (file != null)
            {
                await _mediaCapture.StartRecordToStorageFileAsync(encodingProfile, file);
                _isRecording = true;
                RecordButton.Content = "Stop Recording";
            }
        }

        private async Task StopRecordingAsync()
        {
            await _mediaCapture.StopRecordAsync();
            _isRecording = false;
            RecordButton.Content = "Start Recording";
        }
    }
}