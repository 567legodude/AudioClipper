using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.WindowsAPICodePack.Dialogs;
using DispatcherPriority = System.Windows.Threading.DispatcherPriority;

namespace Clipper
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{

		private readonly AudioManager _manager;

		public MainWindow()
		{
			InitializeComponent();

			_manager = new AudioManager(this);
			SetDefaultValues();
			
			RefreshAudioSources();
		}

		private void SetDefaultValues()
		{
			OutputFolder.Text = "<choose folder>";
			foreach (var format in _manager.AudioFormats.Keys)
			{
				OutputFormat.Items.Add(format);
			}
			OutputFormat.SelectedIndex = 0;
			Time.Text = default(int).ToString();

			SaveClip.IsEnabled = false;
		}

		private void RefreshAudioSources()
		{
			Sources.Items.Clear();
			foreach (var source in _manager.GetAudioSources())
			{
				Sources.Items.Add(source);
			}
		}

		// Enable/Disable given elements
		private void ToggleAll(bool value, params UIElement[] elements)
		{
			foreach (var element in elements)
			{
				element.IsEnabled = value;
			}
		}

		private void LockOptions()
		{
			ToggleAll(false, Hotkey, OutputFolder, ChooseOutput, OutputFormat, Time, Refresh, Sources);
		}

		private void UnlockOptions()
		{
			ToggleAll(true, Hotkey, OutputFolder, ChooseOutput, OutputFormat, Time, Refresh, Sources);
		}
		
		// Start capture
		private void HandleStart()
		{
			// Validate inputs
			var dir = OutputFolder.Text;
			if (!Directory.Exists(dir))
			{
				MessageBox.Show(this, "Output folder is not valid.", "Invalid", MessageBoxButton.OK,
					MessageBoxImage.Error);
				return;
			}
			var time = AudioManager.ParseTime(Time.Text);
			if (time <= 0)
			{
				MessageBox.Show(this, "Invalid clip length.", "Invalid", MessageBoxButton.OK);
				return;
			}
			var sources = (from AudioSource source in Sources.SelectedItems select source.Identifier).ToList();
			if (sources.Count < 1)
			{
				MessageBox.Show(this, "No audio sources selected.", "Empty", MessageBoxButton.OK);
				return;
			}
			// Tell audio manager to begin capturing devices
			StartAudio.IsEnabled = false;
			_manager.StartCapture(sources, dir, OutputFormat.Text, time, () => {
				UpdateStatus("Recording", Visuals.Green);
				Dispatcher?.BeginInvoke(DispatcherPriority.Normal, (Action) (() => {
					LockOptions();
					StartAudio.Content = "Stop";
					StartAudio.IsEnabled = true;
					SaveClip.IsEnabled = true;
				}));
			});
		}

		// Stop capture
		internal void HandleStop()
		{
			StartAudio.IsEnabled = false;
			SaveClip.IsEnabled = false;
			_manager.StopCapture(() => {
				UpdateStatus("Not Recording");
				UnlockOptions();
				StartAudio.Content = "Start";
				StartAudio.IsEnabled = true;
			});
		}
		
		// Change the text next to the "Status:" label
		public void UpdateStatus(string text, Brush color = null)
		{
			color ??= Visuals.Black;
			Status.Content = text;
			Status.Foreground = color;
		}

		// Time value becomes when not valid
		private void Time_TextChanged(object sender, TextChangedEventArgs e)
		{
			var time = AudioManager.ParseTime(Time.Text);
			Time.Foreground = time <= 0 ? Visuals.Red : Visuals.Black;
			Nice.Visibility = time == 69 ? Visibility.Visible : Visibility.Hidden;
		}

		private void Refresh_Click(object sender, RoutedEventArgs e)
		{
			RefreshAudioSources();
		}

		// Change output directory
		private void ChooseOutput_Click(object sender, RoutedEventArgs e)
		{
			using var dialog = new CommonOpenFileDialog {IsFolderPicker = true};
			
			// Initial directory is current output folder or MyDocuments fallback.
			var current = OutputFolder.Text;
			dialog.InitialDirectory = Directory.Exists(current) ? current : Environment.GetFolderPath(Environment.SpecialFolder.Personal);
			
			var result = dialog.ShowDialog(this);
			if (result == CommonFileDialogResult.Ok)
			{
				OutputFolder.Text = dialog.FileName;
			}
		}
		
		private void StartAudio_Click(object sender, RoutedEventArgs e)
		{
			if ((string) StartAudio.Content == "Stop")
			{
				HandleStop();
			}
			else
			{
				HandleStart();
			}
		}

		// Save the last N seconds of audio
		private void SaveClip_Click(object sender, RoutedEventArgs e)
		{
			StartAudio.IsEnabled = false;
			SaveClip.IsEnabled = false;
			_manager.SaveClip(() => {
				// OnFinished
				StartAudio.IsEnabled = true;
				SaveClip.IsEnabled = true;
				UpdateStatus("Recording", Visuals.Green);
			}, () => {
				// OnError
				_manager.StopCapture(HandleStop);
			});
		}
	}
}
