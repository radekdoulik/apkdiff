using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;

namespace apkdiff {
	public class SharedLibraryDiff : EntryDiff {
		public SharedLibraryDiff ()
		{
		}

		public override string Name { get { return "Shared libraries"; } }

		string RunCommand (string cmd, string arguments)
		{
			String output;

			using (var p = new System.Diagnostics.Process ()) {

				p.StartInfo.UseShellExecute = false;
				p.StartInfo.RedirectStandardError = true;
				p.StartInfo.RedirectStandardOutput = true;
				p.StartInfo.FileName = cmd;
				p.StartInfo.Arguments = arguments;

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

		bool UnixPlatforms
		{
			get {
				var platform = Environment.OSVersion.Platform;
				return platform == PlatformID.Unix || platform == PlatformID.MacOSX;
			}
		}

		string PlatformExtension
		{
			get
			{
				return UnixPlatforms ? "" : ".exe";
			}
		}


		string HomeDir {
			get {
				return UnixPlatforms
					? Environment.GetEnvironmentVariable ("HOME")
					: Environment.ExpandEnvironmentVariables ("%HOMEDRIVE%%HOMEPATH%");
			}
		}

		string AndroidToolsDir {
			get {
				return HomeDir + (UnixPlatforms ? "/android-toolchain/toolchains/x86-clang/bin" : "/android-toolchain/ndk/toolchains/llvm/prebuilt/windows-x86_64/bin");
			}
		}

		string RunNMCmd (string file)
		{
			return RunCommand ($"{AndroidToolsDir}/x86_64-linux-android-nm{PlatformExtension}", $"-S --size-sort -D {file}");
		}

		string RunSizeCmd (string file)
		{
			return RunCommand ($"{AndroidToolsDir}/x86_64-linux-android-size{PlatformExtension}", $"-A {file}");
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

		struct SectionInfo : ISizeProvider {
			public long Size { get; set; }
		}

		Dictionary<string, SectionInfo> ParseSizeOutput (string output)
		{
			var sections = new Dictionary<string, SectionInfo> ();

			int skipLines = 2;

			foreach (var line in output.Split (new char [] { '\n' })) {
				if (skipLines > 0) {
					skipLines--;
					continue;
				}

				var cols = line.Split (new char [] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

				if (cols.Length != 3)
					continue;

				sections [cols [0]] = new SectionInfo () { Size = int.Parse (cols [1]) };
			}

			return sections;
		}

		void CompareSections (string file, string other, string padding)
		{
			var scs1 = ParseSizeOutput (RunSizeCmd (file));
			var scs2 = ParseSizeOutput (RunSizeCmd (other));

			var differences = new Dictionary<string, long> ();
			var singles = new HashSet<string> ();

			foreach (var entry in scs1) {
				var key = entry.Key;

				if (scs2.ContainsKey (key)) {
					var otherEntry = scs2 [key];
					differences [key] = otherEntry.Size - scs1 [key].Size;
				} else {
					differences [key] = -scs1 [key].Size;
					singles.Add (key);
				}
			}

			foreach (var key in scs2.Keys) {
				if (scs1.ContainsKey (key))
					continue;

				differences [key] = scs2 [key].Size;
				singles.Add (key);
			}

			var count = Program.Print.Push (() => Program.ColorWriteLine ($"{padding}                Section size difference", ConsoleColor.Yellow));

			foreach (var diff in differences.OrderByDescending (v => v.Value)) {
				if (diff.Value == 0)
					continue;

				var single = singles.Contains (diff.Key);

				Program.Print.Invoke ();
				Program.PrintDifference (diff.Key, diff.Value, single ? $" *{(diff.Value > 0 ? 2 : 1)}" : null, padding);
			}

			Program.Print.Pop (count);
		}

		void CompareSymbols (string file, string other, string padding)
		{
			var sym1 = ParseNMOutput (RunNMCmd (file));
			var sym2 = ParseNMOutput (RunNMCmd (other));

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

			var count = Program.Print.Push (() => Program.ColorWriteLine ($"{padding}                Symbol size difference", ConsoleColor.Yellow));

			foreach (var diff in differences.OrderByDescending (v => v.Value)) {
				if (diff.Value == 0)
					continue;

				var single = singles.Contains (diff.Key);

				Program.Print.Invoke ();
				Program.PrintDifference (diff.Key, diff.Value, single ? $" *{(diff.Value > 0 ? 2 : 1)}" : null, padding);
			}

			Program.Print.Pop (count);
		}

		public override void Compare (string file, string other, string padding)
		{
			if (!Directory.Exists (AndroidToolsDir))
				return;

			CompareSections (file, other, padding);
			CompareSymbols (file, other, padding);
		}
	}
}
