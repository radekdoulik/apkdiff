using System;
using System.Linq;
using System.Collections.Generic;

namespace apkdiff {
	public class SharedLibraryDiff : EntryDiff {
		public SharedLibraryDiff ()
		{
		}

		string RunNM (string file)
		{
			String output;

			using (var p = new System.Diagnostics.Process ()) {

				p.StartInfo.UseShellExecute = false;
				p.StartInfo.RedirectStandardError = true;
				p.StartInfo.RedirectStandardOutput = true;
				p.StartInfo.FileName = "nm";
				p.StartInfo.Arguments = $"-S --size-sort -D {file}";

				try {
					p.Start ();
				} catch {
					Program.Warning ($"Unable to run '{p.StartInfo.FileName}' command");

					return null;
				}

				output = p.StandardOutput.ReadToEnd ();
				var error = p.StandardError.ReadToEnd ();

				if (error.Length > 0)
					Program.Error ($"nm error output:\n{error}");

				p.WaitForExit ();
			}

			return output;
		}

		struct SymbolInfo : ISizeProvider {
			public long Size { get; set; }
		}

		Dictionary<string, SymbolInfo> ParseNMOutput (string output)
		{
			var symbols = new Dictionary<string, SymbolInfo> ();

			foreach (var line in output.Split (new char [] { '\n' })) {
				var cols = line.Split (new char [] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

				if (cols.Length != 4)
					continue;

				symbols [cols [3]] = new SymbolInfo () { Size = int.Parse (cols [1], System.Globalization.NumberStyles.HexNumber) };
			}

			return symbols;
		}

		public override void Compare (string file, string other, string padding)
		{
			var sym1 = ParseNMOutput (RunNM (file));
			var sym2 = ParseNMOutput (RunNM (other));

			Program.ColorWriteLine ($"{padding}                Symbol size difference", ConsoleColor.Yellow);

			var differences = new Dictionary<string, long> ();
			var singles = new HashSet<string> ();

			foreach (var entry in sym1) {
				var key = entry.Key;

				if (sym2.ContainsKey (key)) {
					var otherEntry = sym2 [key];
					differences [key] = otherEntry.Size - sym1 [key].Size;
				} else {
					differences [key] = -sym1 [key].Size;
					singles.Add (key);
				}
			}

			foreach (var key in sym2.Keys) {
				if (sym1.ContainsKey (key))
					continue;

				differences [key] = sym2 [key].Size;
				singles.Add (key);
			}

			foreach (var diff in differences.OrderByDescending (v => v.Value)) {
				if (diff.Value == 0)
					continue;

				var single = singles.Contains (diff.Key);

				ApkDescription.PrintDifference (diff.Key, diff.Value, single ? $" *{(diff.Value > 0 ? 2 : 1)}" : null, padding);
			}

		}

	}
}
