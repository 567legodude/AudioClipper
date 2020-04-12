using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Clipper
{
    // Handle recording of an audio source
    public class CaptureHandler : IDisposable
    {

        public readonly MMDevice Device;
        private WasapiCapture _capture;
        private WasapiOut _renderFix;
        
        private byte[] _buffer;
        private int _index;
        private bool _fullBuffer = false;

        private Stream _dest;
        private PauseAction _action = PauseAction.None;
        private TaskCompletionSource<bool> _task;

        public event Action<Exception> OnFailure;

        public CaptureHandler(MMDevice device, int seconds)
        {
            // Pick capture class based on device type
            Device = device;
            if (device.DataFlow == DataFlow.Render)
            {
                _capture = new WasapiLoopbackCapture();
                _renderFix = new WasapiOut(device, AudioClientShareMode.Shared, true, 200);
            }
            else
            {
                _capture = new WasapiCapture(device);
            }
            // Create audio buffer for the requested amount of time
            _buffer = new byte[_capture.WaveFormat.AverageBytesPerSecond * seconds];
            _index = 0;

            _capture.DataAvailable += OnAudioAvailable;
            _capture.RecordingStopped += OnStop;
        }

        // Write new audio data to the circular buffer
        private void OnAudioAvailable(object sender, WaveInEventArgs args)
        {
            WriteCircular(args.Buffer, 0, _index, args.BytesRecorded);
            _index = (_index + args.BytesRecorded) % _buffer.Length;
        }

        private void WriteCircular(byte[] data, int dataStart, int bufferStart, int len)
        {
            while (true)
            {
                if (bufferStart + len <= _buffer.Length)
                {
                    // Data fits in the buffer
                    Array.Copy(data, dataStart, _buffer, bufferStart, len);
                }
                else
                {
                    // Data overruns the buffer, remainder starts at beginning
                    _fullBuffer = true;
                    var write = _buffer.Length - bufferStart;
                    Array.Copy(data, dataStart, _buffer, bufferStart, write);
                    dataStart += write;
                    bufferStart = 0;
                    len -= write;
                    continue;
                }
                break;
            }
        }

        private void OnStop(object sender, StoppedEventArgs args)
        {
            if (args.Exception != null)
            {
                // Notify program of stop caused by exception
                _renderFix?.Stop();
                OnFailure?.Invoke(args.Exception);
                return;
            }
            if (_action == PauseAction.Clip)
            {
                // Write circular buffer to output stream
                using (_dest)
                {
                    if (_fullBuffer)
                    {
                        _dest.Write(_buffer, _index, _buffer.Length - _index);
                    }
                    if (_index > 0)
                    {
                        _dest.Write(_buffer, 0, _index);
                    }
                    _dest.Flush();
                }
                _dest = null;
            }
            _action = PauseAction.None;
            _task.SetResult(true);
        }

        public void Dispose()
        {
            _capture.Dispose();
            _renderFix?.Dispose();
            Device.Dispose();
        }

        public WaveFormat Format => _capture.WaveFormat;

        // Start capture
        public void Activate()
        {
            _fullBuffer = false;
            _index = 0;
            _capture.StartRecording();
            if (_renderFix != null)
            {
                // Audio event does not fire if no audio is being played
                // so play silence into output devices.
                if (_renderFix.PlaybackState != PlaybackState.Paused)
                {
                    _renderFix.Init(new SilenceProvider(_capture.WaveFormat));
                }
                _renderFix.Play();
            }
        }

        // Write buffer to output
        public Task Clip(Stream dest)
        {
            _dest = dest;
            _action = PauseAction.Clip;
            _task = new TaskCompletionSource<bool>();
            _capture.StopRecording();
            _renderFix?.Pause();
            return _task.Task;
        }

        public Task Stop()
        {
            if (_capture.CaptureState != CaptureState.Capturing) return Task.CompletedTask;
            _action = PauseAction.Stop;
            _task = new TaskCompletionSource<bool>();
            _capture.StopRecording();
            _renderFix?.Stop();
            return _task.Task;
        }

        private enum PauseAction
        {
            None,
            Clip,
            Stop
        }

    }
}