using System;
using System.IO;

using static System.Console;

namespace apkdiff {
	class Program {
		public static bool Verbose;
		protected static string Name;

		public static PrintStack Print { get; private set; }

		static Program ()
		{
			Print = new PrintStack ();
		}

		static void ColorMessage (string message, ConsoleColor color, TextWriter writer, bool writeLine = true)
		{
			ForegroundColor = color;

			if (writeLine)
				writer.WriteLine (message);
			else
				writer.Write (message);

			ResetColor ();
		}

		public static void ColorWriteLine (string message, ConsoleColor color) => ColorMessage (message, color, Out);

		public static void ColorWrite (string message, ConsoleColor color) => ColorMessage (message, color, Out, false);

		public static void Error (string message) => ColorMessage ($"Error: {Name}: {message}", ConsoleColor.Red, Console.Error);

		public static void Warning (string message) => ColorMessage ($"Warning: {Name}: {message}", ConsoleColor.Yellow, Console.Error);

		static public void PrintDifference (string key, long diff, long orig, string comment = null, string padding = null)
		{
			var color = PrintDifferenceStart (key, diff, comment, padding);

			if (orig != 0)
				Program.ColorWrite ($" {(float)diff / orig:0.00%} (of {orig:#,0})", color);

			Console.WriteLine ();
		}

		static ConsoleColor PrintDifferenceStart (string key, long diff, string comment = null, string padding = null)
		{
			var color = diff == 0 ? ConsoleColor.Gray : diff > 0 ? ConsoleColor.Red : ConsoleColor.Green;
			Program.ColorWrite ($"{padding}  {diff:+;-;+}{Math.Abs (diff),12:#,0}", color);
			Program.ColorWrite ($" {key}", ConsoleColor.Gray);
			Program.ColorWrite (comment, color);

			return color;
		}

		static public void PrintDifference (string key, long diff, string comment = null, string padding = null)
		{
			PrintDifferenceStart (key, diff, comment, padding);
			Console.WriteLine ();
		}


	}
}
