using System;
using System.IO;
using apkdiff;
using Mono.Options;
using static System.Console;

namespace adiff
{
	class ADiff : Program
	{
		static bool CompareMetadata;
		static bool CompareMethodBodies;

		static ADiff ()
		{
			Name = nameof (adiff);
		}

		public static void Main (string [] args)
		{
			var (path1, path2) = ProcessArguments (args);

			var count = Program.Print.Push (new Action (() => Program.ColorWriteLine ($"Compare {path1} with {path2}", ConsoleColor.Yellow)));

			var adiff = new AssemblyDiff (typesPattern) { ComapareMetadata = CompareMetadata, CompareMethodBodies = CompareMethodBodies };

			adiff.Compare (path1, path2, "");
			adiff.Summary ();

			Program.Print.Pop (count);
		}

		static string typesPattern;

		static (string, string) ProcessArguments (string [] args)
		{
			var help = false;
			int helpExitCode = 0;
			var options = new OptionSet {
				$"Usage: {Name}.exe OPTIONS* <assembly1.dll> <assembly2.dll>",
				"",
				"Compares .NET assemblies",
				"",
				"Copyright 2020 Microsoft Corporation",
				"",
				"Options:",
				{ "bs",
					"Compare methods body size",
				  v => CompareMethodBodies = true },
				{ "h|help|?",
					"Show this message and exit",
				  v => help = v != null },
				{ "md",
					"Compare metadata sizes",
				  v => CompareMetadata = true },
				{ "t|type=",
					"Process only types matching regex {PATTERN}",
				  v => typesPattern = v },
				{ "v|verbose",
					"Output information about progress during the run of the tool",
				  v => Verbose = true },
			};

			var remaining = options.Parse (args);

			foreach (var s in remaining) {
				if (s.Length > 0 && (s [0] == '-' || s [0] == '/') && !File.Exists (s)) {
					Error ($"Unknown option: {s}");
					help = true;
					helpExitCode = 99;
				}
			}

			if (remaining.Count != 2) {
				Error ($"Please specify 2 assemblies to compare");
				help = true;
				helpExitCode = 100;
			}

			if (help || args.Length < 1) {
				options.WriteOptionDescriptions (Out);

				Environment.Exit (helpExitCode);
			}

			return (remaining [0], remaining [1]);
		}
	}
}
