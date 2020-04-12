using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Clipper
{
    public static class Extensions
    {

        // Get the name of a device modified for the file name.
        public static string GetFileName(this MMDevice device)
        {
            var name = device.FriendlyName;

            var open = name.IndexOf('(');
            if (open > 0)
            {
                name = name[..open].Trim();
            }
            name += " " + (device.DataFlow == DataFlow.Render ? "(Output)" : "(Input)");
            
            name = name.Replace(' ', '_');
            var invalid = string.Format(@"([{0}]*\.+$)|([{0}]+)", Regex.Escape(new string(Path.GetInvalidFileNameChars())));
            name = Regex.Replace(name, invalid, "");
            return name;
        }

        // Get a bitrate in bits per second from a wave format.
        public static int BitRate(this WaveFormat format)
        {
            return format.SampleRate * format.BitsPerSample * format.Channels;
        }

        // Add extension to filename
        public static string WithExtension(this string s, string ext)
        {
            return $"{s}.{ext.ToLower()}";
        }

    }

}