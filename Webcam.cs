using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Core;

namespace Flex
{
    class Webcam
    {
        MediaCapture _capture;
        DeviceInformation _device;

        public async Task<MediaSource> SetDevice(DeviceInformation device)
        {
            _device = device;

            if (_capture != null)
            {
                _capture.Dispose();
                _capture = null;
            }

            _capture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = _device.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video
            };

            await _capture.InitializeAsync(settings);

            var source = _capture.FrameSources
                .FirstOrDefault(
                    source =>
                        source.Value.Info.MediaStreamType == MediaStreamType.VideoPreview
                        || source.Value.Info.MediaStreamType == MediaStreamType.VideoRecord
                )
                .Value;

            return MediaSource.CreateFromMediaFrameSource(source);
        }
    }
}
