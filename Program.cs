using System;
using System.IO;
using Mono.Options;

using static System.Console;

namespace apkdiff {
	class Program {
                static readonly string Name = "apkdiff";

                public static bool Verbose;

		public static void Main (string [] args)
		{
                        var (path1, path2) = ProcessArguments (args);

                        var desc1 = ApkDescription.Load (path1);
                        var desc2 = ApkDescription.Load (path2);

                        desc1.Compare (desc2);
                }

                static (string, string) ProcessArguments (string [] args)
                {
                        var help = false;
                        var options = new OptionSet {
                                $"Usage: {Name}.exe OPTIONS* <package1.apk> <package2.apk>",
                                "",
                                "Compares APK packages content or APK package with content description",
                                "",
                                "Copyright 2020 Microsoft Corporation",
                                "",
                                "Options:",
                                { "h|help|?",
                                        "Show this message and exit",
                                  v => help = v != null },
                                { "v|verbose",
                                        "Output information about progress during the run of the tool",
                                  v => Verbose = true },
                        };

                        var remaining = options.Parse (args);

                        if (help || args.Length < 1) {
                                options.WriteOptionDescriptions (Out);

                                Environment.Exit (0);
                        }

                        if (remaining.Count != 2) {
                                Error ("Please specify 2 APK packages to compare.");
                                Environment.Exit (1);
                        }

                        return (remaining [0], remaining [1]);
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
        }
}
