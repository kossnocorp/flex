using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.MediaProperties;
using Windows.Graphics.Capture;
using Windows.Storage;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using Windows.Storage.Pickers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ScreenRecorderLib;
using System.IO;
using System.Collections.ObjectModel;

namespace Flex
{
    public sealed partial class MainWindow : Window
    {
        private MediaCapture _mediaCapture;
        private MediaPlayer _mediaPlayer;
        private bool _devicesInitialized;
        private bool _isRecording = false;
        private Recorder _screenRecorder;
        private List<RecordableDisplay> _displays;
        private RecordableDisplay _selectedDisplay;
        private int _selectedDisplayWidth;
        private int _selectedDisplayHeight;

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
                PopulateDisplayList();

                PopulateAudioDevices();

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
        }

        private async void WebcamComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Debug.WriteLine("WebcamComboBox_SelectionChanged called");
            await InitializeMediaCaptureAsync();
        }

        private async Task InitializeMediaCaptureAsync()
        {
            if (WebcamComboBox.SelectedItem is not DeviceInformation selectedVideoDevice)
            {
                return;
            }

            Debug.WriteLine($"InitializeMediaCaptureAsync started for video device: {selectedVideoDevice.Name}");

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
                StreamingCaptureMode = StreamingCaptureMode.Video
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
            var savePicker = new FileSavePicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);
            savePicker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            savePicker.FileTypeChoices.Add("MP4 files", new List<string>() { ".mp4" });
            savePicker.SuggestedFileName = "ScreenCapture";

            StorageFile file = await savePicker.PickSaveFileAsync();
            if (file != null)
            {
                string videoPath = file.Path;
                Debug.WriteLine($"Starting recording to: {videoPath}");

                try
                {
                    // Delete the file if it already exists
                    if (File.Exists(videoPath))
                    {
                        Debug.WriteLine($"File already exists: {videoPath}. Deleting...");
                        File.Delete(videoPath);
                    }

                    //var sources = new List<RecordingSourceBase>();
                    ////You can add main monitor by a static MainMonitor property
                    //var monitor1 = new DisplayRecordingSource(DisplayRecordingSource.MainMonitor);
                    //////Or any monitor by device name
                    ////var monitor2 = new DisplayRecordingSource(@"\\.\DISPLAY2");
                    ////sources.Add(monitor1);
                    ////sources.Add(monitor2);
                    ////Or you can all system monitors with the static Recorder.GetDisplays() function.
                    //sources.AddRange(Recorder.GetDisplays());

                    //var sources = new List<RecordingSourceBase>();
                    //if (_selectedDisplay != null)
                    //{
                    //    sources.Add(new DisplayRecordingSource(_selectedDisplay));
                    //}
                    //else
                    //{
                    //    sources.AddRange(Recorder.GetDisplays().Select(d => new DisplayRecordingSource(d)));
                    //}

                    var sources = new List<RecordingSourceBase>();
                    sources.Add(new DisplayRecordingSource(_selectedDisplay));

                    //double scaleFactor = 1.5; // 150% scaling
                    //int width = (int)(_selectedDisplayWidth / scaleFactor);
                    //int height = (int)(_selectedDisplayHeight / scaleFactor);

                    int width = _selectedDisplayWidth;
                    int height = _selectedDisplayHeight;

                    //// Create the ScreenRect with the effective dimensions
                    //var rect = new ScreenRect(0, 0, effectiveWidth, effectiveHeight);

                    var output = new ScreenSize(width / 2, height / 2);
                    var rect = new ScreenRect(0, 0, width, height);

                    // Configure recording options
                    RecorderOptions options = new RecorderOptions
                    {
                        SourceOptions = new SourceOptions
                        {
                            RecordingSources = sources
                        },
                        OutputOptions = new OutputOptions
                        {
                            RecorderMode = RecorderMode.Video,
                            //This sets a custom size of the video output, in pixels.
                            OutputFrameSize = output,
                            //Stretch controls how the resizing is done, if the new aspect ratio differs.
                            Stretch = StretchMode.Uniform,
                            //SourceRect allows you to crop the output.
                            SourceRect = rect
                        },
                        AudioOptions = GetAudioOptions(),
                        VideoEncoderOptions = new VideoEncoderOptions
                        {
                            Bitrate = 8000 * 1000,
                            Framerate = 60,
                            IsFixedFramerate = true,
                            //Currently supported are H264VideoEncoder and H265VideoEncoder
                            Encoder = new H264VideoEncoder
                            {
                                BitrateMode = H264BitrateControlMode.CBR,
                                EncoderProfile = H264Profile.Main,
                            },
                            //Fragmented Mp4 allows playback to start at arbitrary positions inside a video stream,
                            //instead of requiring to read the headers at the start of the stream.
                            IsFragmentedMp4Enabled = true,
                            //If throttling is disabled, out of memory exceptions may eventually crash the program,
                            //depending on encoder settings and system specifications.
                            IsThrottlingDisabled = false,
                            //Hardware encoding is enabled by default.
                            IsHardwareEncodingEnabled = true,
                            //Low latency mode provides faster encoding, but can reduce quality.
                            IsLowLatencyEnabled = false,
                            //Fast start writes the mp4 header at the beginning of the file, to facilitate streaming.
                            IsMp4FastStartEnabled = false
                        },
                        //MouseOptions = new MouseOptions
                        //{
                        //    //Displays a colored dot under the mouse cursor when the left mouse button is pressed.	
                        //    IsMouseClicksDetected = true,
                        //    MouseLeftClickDetectionColor = "#FFFF00",
                        //    MouseRightClickDetectionColor = "#FFFF00",
                        //    MouseClickDetectionRadius = 30,
                        //    MouseClickDetectionDuration = 100,
                        //    IsMousePointerEnabled = true,
                        //    /* Polling checks every millisecond if a mouse button is pressed.
                        //       Hook is more accurate, but may affect mouse performance as every mouse update must be processed.*/
                        //    MouseClickDetectionMode = MouseDetectionMode.Hook
                        //},
                        //OverlayOptions = new OverLayOptions
                        //{
                        //    //Populate and pass a list of recording overlays.
                        //    Overlays = new List<RecordingOverlayBase>()
                        //},
                        //SnapshotOptions = new SnapshotOptions
                        //{
                        //    //Take a snapshot of the video output at the given interval
                        //    SnapshotsWithVideo = false,
                        //    SnapshotsIntervalMillis = 1000,
                        //    SnapshotFormat = ImageFormat.PNG,
                        //    //Optional path to the directory to store snapshots in
                        //    //If not configured, snapshots are stored in the same folder as video output.
                        //    SnapshotsDirectory = ""
                        //},
                        //LogOptions = new LogOptions
                        //{
                        //    //This enabled logging in release builds.
                        //    IsLogEnabled = true,
                        //    //If this path is configured, logs are redirected to this file.
                        //    LogFilePath = "recorder.log",
                        //    LogSeverityLevel = ScreenRecorderLib.LogLevel.Debug
                        //}
                    };

                    Debug.WriteLine($"Audio Input Device set to: {options.AudioOptions.AudioInputDevice}");
                    Debug.WriteLine($"InputVolume: {options.AudioOptions.InputVolume}");
                    Debug.WriteLine($"OutputVolume: {options.AudioOptions.OutputVolume}");


                    Debug.WriteLine("Recorder options:");
                    Debug.WriteLine($"Audio Enabled: {options.AudioOptions.IsAudioEnabled}");
                    Debug.WriteLine($"Output Device Enabled: {options.AudioOptions.IsOutputDeviceEnabled}");
                    Debug.WriteLine($"Input Device Enabled: {options.AudioOptions.IsInputDeviceEnabled}");
                    Debug.WriteLine($"Framerate: {options.VideoEncoderOptions.Framerate}");
                    Debug.WriteLine($"Is Fixed Framerate: {options.VideoEncoderOptions.IsFixedFramerate}");
                    Debug.WriteLine($"Bitrate: {options.VideoEncoderOptions.Bitrate}");
                    //Debug.WriteLine($"Encoder Profile: {options.VideoEncoderOptions.Encoder.EncoderProfile}");
                    Debug.WriteLine($"Bitrate Mode: {((H264VideoEncoder)options.VideoEncoderOptions.Encoder).BitrateMode}");

                    Debug.WriteLine($"Recorder options configured: {Newtonsoft.Json.JsonConvert.SerializeObject(options)}");



                    // Initialize ScreenRecorderLib
                    _screenRecorder = Recorder.CreateRecorder(options);
                    _screenRecorder.OnRecordingComplete += ScreenRecorder_OnRecordingComplete;
                    _screenRecorder.OnRecordingFailed += ScreenRecorder_OnRecordingFailed;
                    _screenRecorder.OnStatusChanged += ScreenRecorder_OnStatusChanged;



                    // Start recording
                    try
                    {
                        Debug.WriteLine("Calling Record method...");
                        _screenRecorder.Record(videoPath);
                        Debug.WriteLine("Record method called successfully");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Exception during Record call: {ex.GetType().Name} - {ex.Message}");
                        Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                        if (ex.InnerException != null)
                        {
                            Debug.WriteLine($"Inner Exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                        }
                        throw; // Re-throw the exception to be caught by the outer try-catch block
                    }

                    Debug.WriteLine("Waiting for recording to start...");
                    for (int i = 0; i < 50; i++) // Increased from 10 to 50
                    {
                        await Task.Delay(100);
                        if (_screenRecorder.Status == RecorderStatus.Recording)
                        {
                            Debug.WriteLine("Recording started successfully.");
                            _isRecording = true;
                            RecordButton.Content = "Stop Recording";
                            return;
                        }
                    }

                    Debug.WriteLine("Recording did not start within the expected time.");
                    throw new TimeoutException("Recording did not start within the expected time.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Exception during recording start: {ex.GetType().Name} - {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Debug.WriteLine($"Inner Exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                    }
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                }
            }
            else
            {
                Debug.WriteLine("File selection was cancelled");
            }
        }

        private async Task StopRecordingAsync()
        {
            if (_screenRecorder != null && _isRecording)
            {
                Debug.WriteLine("Stopping recording...");
                await Task.Run(() => _screenRecorder.Stop());
                Debug.WriteLine("Stop method called");
                _isRecording = false;
                RecordButton.Content = "Start Recording";
            }
            else
            {
                Debug.WriteLine("No active recording to stop");
            }
        }

        private void ScreenRecorder_OnRecordingComplete(object sender, RecordingCompleteEventArgs e)
        {
            Debug.WriteLine($"Recording completed: {e.FilePath}");
            //Debug.WriteLine($"Recording duration: {e.Duration}");
            //Debug.WriteLine($"File size: {e.FileSize} bytes");
        }

        private void ScreenRecorder_OnRecordingFailed(object sender, RecordingFailedEventArgs e)
        {
            Debug.WriteLine($"Recording failed: {e.Error}");
            //if (e.Exception != null)
            //{
            //    Debug.WriteLine($"Exception details: {e.Exception.GetType().Name} - {e.Exception.Message}");
            //    if (e.Exception.InnerException != null)
            //    {
            //        Debug.WriteLine($"Inner Exception: {e.Exception.InnerException.GetType().Name} - {e.Exception.InnerException.Message}");
            //    }
            //    Debug.WriteLine($"Stack Trace: {e.Exception.StackTrace}");
            //}
        }

        private void ScreenRecorder_OnStatusChanged(object sender, RecordingStatusEventArgs e)
        {
            Debug.WriteLine($"Recording status changed: {e.Status}");
            if (e.Status == RecorderStatus.Recording)
            {
                Debug.WriteLine("Recording has started successfully");
            }
            else if (e.Status == RecorderStatus.Idle)
            {
                Debug.WriteLine("Recording has stopped");
            }
        }

        private void PopulateDisplayList()
        {
            _displays = Recorder.GetDisplays().ToList();
            foreach (var display in _displays)
            {
                DisplayComboBox.Items.Add(display.DeviceName);
            }
            if (DisplayComboBox.Items.Count > 0)
            {
                DisplayComboBox.SelectedIndex = 0;
            }
        }

        private void DisplayComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DisplayComboBox.SelectedIndex >= 0)
            {
                _selectedDisplay = _displays[DisplayComboBox.SelectedIndex];
                var (width, height) = GetDisplaySize(_selectedDisplay.DeviceName);
                _selectedDisplayWidth = width;
                _selectedDisplayHeight = height;
                Debug.WriteLine($"Selected display: {_selectedDisplay.DeviceName}, Size: {width}x{height}");
            }
        }

        [DllImport("user32.dll")]
        static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [StructLayout(LayoutKind.Sequential)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        private (int width, int height) GetDisplaySize(string deviceName)
        {
            DEVMODE devMode = new DEVMODE();
            devMode.dmSize = (short)Marshal.SizeOf(devMode);
            if (EnumDisplaySettings(deviceName, -1, ref devMode))
            {
                return (devMode.dmPelsWidth, devMode.dmPelsHeight);
            }
            return (0, 0); // Return 0,0 if we couldn't get the display settings
        }

        ObservableCollection<AudioDevice> audioInputDevices = new ObservableCollection<AudioDevice>();

        ObservableCollection<AudioDevice> audioOutputDevices = new ObservableCollection<AudioDevice>();

        private void PopulateAudioDevices()
        {
            var emptyDevice = new AudioDevice { DeviceName = "", FriendlyName = "Disabled" };

            // Input

            audioInputDevices.Add(emptyDevice);

            List<AudioDevice> inputDevices = Recorder.GetSystemAudioDevices(AudioDeviceSource.InputDevices);
            inputDevices.ForEach(d => audioInputDevices.Add(d));

            AudioDevice defaultInputAudioDevice = inputDevices.FirstOrDefault();

            // Select first or default device
            AudioInputComboBox.SelectedItem = defaultInputAudioDevice;

            // Output

            audioOutputDevices.Add(emptyDevice);

            List<AudioDevice> outputDevices = Recorder.GetSystemAudioDevices(AudioDeviceSource.OutputDevices);
            outputDevices.ForEach(d => audioOutputDevices.Add(d));

            // Select empty
            AudioOutputComboBox.SelectedItem = emptyDevice;
        }

        private AudioOptions GetAudioOptions()
        {
            var audioInputDevice = AudioInputComboBox.SelectedItem as AudioDevice;
            var audioOutputDevice = AudioOutputComboBox.SelectedItem as AudioDevice;

            return new AudioOptions
            {
                //Bitrate = AudioBitrate.bitrate_128kbps,
                //Channels = AudioChannels.Stereo,
                IsAudioEnabled = true,
                // Audio ouput capturing
                IsOutputDeviceEnabled = audioOutputDevice.DeviceName != "",
                AudioOutputDevice = audioOutputDevice.DeviceName,
                // Audio input capturing
                IsInputDeviceEnabled = audioInputDevice.DeviceName != "",
                AudioInputDevice = audioInputDevice.DeviceName
            };
        }
    }
}