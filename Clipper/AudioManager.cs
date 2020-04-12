using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using DispatcherPriority = System.Windows.Threading.DispatcherPriority;

namespace Clipper
{
	public class AudioManager
	{

		public readonly MainWindow Main;

		private readonly MMDeviceEnumerator _devices = new MMDeviceEnumerator();

		private string _outputFolder;
		private string _outputFormat;
		private int _clipLength;

		private List<CaptureHandler> _handlers = new List<CaptureHandler>();

		public readonly Dictionary<string, Func<string, WaveFormat, int, Stream>> AudioFormats =
			new Dictionary<string, Func<string, WaveFormat, int, Stream>>()
		{
			{"WAV", (path, format, bitRate) => new WaveFileWriter(path, format)},
			{"MP3", (path, format, bitRate) => new LameMP3FileWriter(path, format, bitRate)},
		};

		public AudioManager(MainWindow main)
		{
			Main = main;
		}

		private void SendStatus(string text, Brush color = null)
		{
			Main.UpdateStatus(text, color);
		}

		private void OnFailure(Exception e)
		{
			Main.Dispatcher?.BeginInvoke(DispatcherPriority.Normal, (Action) (() => {
				MessageBox.Show(Main, "There was an error during recording. Message: " + e.Message, "Error",
					MessageBoxButton.OK);
				Main.HandleStop();
			}));
		}

		private Stream GetStream(CaptureHandler handler, string outputFolder, string outputFormat, int index)
		{
			if (!Directory.Exists(outputFolder))
			{
				throw new ArgumentException("Output directory doesn't exist.");
			}
			var time = DateTime.Now;
			var file = $"{time.Day}-{time.Month}-{time.Year}_{time.Hour}-{time.Minute}-{time.Second}_{handler.Device.GetFileName()}";
			var path = Path.Combine(outputFolder, file.WithExtension(outputFormat));
			while (File.Exists(path))
			{
				path = Path.Combine(outputFolder, $"{file}_{index++}".WithExtension(outputFormat));
			}
			return AudioFormats[outputFormat](path, handler.Format, handler.Format.BitRate());
		}

		public static int ParseTime(string value)
		{
			if (int.TryParse(value, out var v) && v > 0) return v;
			return -1;
		}

		public IEnumerable<AudioSource> GetAudioSources()
		{
			foreach (var device in _devices.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active))
			{
				yield return new AudioSource {
					DisplayName = device.FriendlyName,
					Identifier = device.ID,
					IsOutput = device.DataFlow == DataFlow.Render
				};
			}
		}

		public void StartCapture(IEnumerable<string> devices, string outputFolder, string outputFormat, int clipLength, Action finished)
		{
			_outputFolder = outputFolder;
			_outputFormat = outputFormat;
			_clipLength = clipLength;
			SendStatus("Starting...");
			Task.Run(() => {
				foreach (var id in devices)
				{
					var device = _devices.GetDevice(id);
					var handler = new CaptureHandler(device, _clipLength);
					_handlers.Add(handler);
					handler.OnFailure += OnFailure;
					handler.Activate();
				}
				Main.Dispatcher?.BeginInvoke(DispatcherPriority.Normal, finished);
			});
		}

		public void SaveClip(Action finished, Action error)
		{
			SendStatus("Saving clips...");
			Task.Run(async () => {
				try
				{
					var streams = _handlers.Select((handler, i) =>
						(handler, stream: GetStream(handler, _outputFolder, _outputFormat, i))).ToList();
					await Task.WhenAll(streams.Select(tuple => tuple.handler.Clip(tuple.stream)));
				}
				catch (Exception e)
				{
					var msg = e.Message;
					if (e is AggregateException a)
					{
						msg = string.Join("\n", a.InnerExceptions.Select((ex, i) => $"{i + 1}) {ex.Message}"));
					}
					MessageBox.Show(Main, "There was an error while saving the clips. Message:\n" + msg, "Error",
						MessageBoxButton.OK);
					Main.Dispatcher?.BeginInvoke(DispatcherPriority.Normal, error);
					return;
				}
				foreach (var handler in _handlers)
				{
					handler.Activate();
				}
				Main.Dispatcher?.BeginInvoke(DispatcherPriority.Normal, finished);
			});
		}

		public void StopCapture(Action finished)
		{
			SendStatus("Stopping...");
			Task.Run(async () => {
				await Task.WhenAll(_handlers.Select(handler => handler.Stop()));
				foreach (var handler in _handlers)
				{
					handler.Dispose();
				}
				_handlers.Clear();
				Main.Dispatcher?.BeginInvoke(DispatcherPriority.Normal, finished);
			});
		}

	}
}