﻿using Mono.Options;
using System;
using System.IO;

using static System.Console;

namespace apkdiff
{
	class ApkDiff : Program
	{
		public static string Comment;
		public static bool CompareMetadata;
		public static bool CompareMethodBodies;
		public static bool Flat;
		public static bool SaveDescriptions;
		public static string SaveDescription1, SaveDescription2;

		public static long AssemblyRegressionThreshold;
		public static long ApkRegressionThreshold;
		public static bool DecreaseIsRegression;
		public static int RegressionCount;
		public static double ApkRegressionThresholdPercentage;
		public static double FileRegressionThresholdPercentage;

		static ApkDiff ()
		{
			Name = nameof (apkdiff);
		}

		public static void Main (string [] args)
		{
			var (path1, path2) = ProcessArguments (args);

			var desc1 = ApkDescription.Load (path1, SaveDescription1);

			if (path2 != null) {
				var desc2 = ApkDescription.Load (path2, SaveDescription2);

				desc1.Compare (desc2, entriesPattern, Flat);

				if (ApkRegressionThreshold != 0) {
					if ((desc2.PackageSize - desc1.PackageSize) > ApkRegressionThreshold) {
						Error ($"PackageSize increase {desc2.PackageSize - desc1.PackageSize:#,0} is {desc2.PackageSize - desc1.PackageSize - ApkRegressionThreshold:#,0} bytes more than the threshold {ApkRegressionThreshold:#,0}. apk1 size: {desc1.PackageSize:#,0} bytes, apk2 size: {desc2.PackageSize:#,0} bytes.");
						RegressionCount++;
					} else if (DecreaseIsRegression && (desc1.PackageSize - desc2.PackageSize) > ApkRegressionThreshold) {
						Error ($"PackageSize decrease {desc1.PackageSize - desc2.PackageSize:#,0} is {desc1.PackageSize - desc2.PackageSize - ApkRegressionThreshold:#,0} bytes more than the threshold {ApkRegressionThreshold:#,0}. apk1 size: {desc1.PackageSize:#,0} bytes, apk2 size: {desc2.PackageSize:#,0} bytes.");
						RegressionCount++;
					}
				}
				if (ApkRegressionThresholdPercentage != 0) {
					double diff = desc2.PackageSize - desc1.PackageSize;
					var change =  diff / desc1.PackageSize * 100;
					if (change > ApkRegressionThresholdPercentage || (DecreaseIsRegression && change < -ApkRegressionThresholdPercentage)) {
						Error ($"PackageSize difference {change:#,0.00} % exceeds the threshold of {ApkRegressionThresholdPercentage:#,0.00} %. apk1 size: {desc1.PackageSize:#,0} bytes, apk2 size: {desc2.PackageSize:#,0} bytes.");
						RegressionCount++;
					}
				}
			}

			if (RegressionCount > 0) {
				Error ($"Size regression occured, {RegressionCount:#,0} check(s) failed.");
				Environment.Exit (3);
			}
		}

		static string entriesPattern;

		static (string, string) ProcessArguments (string [] args)
		{
			var help = false;
			int helpExitCode = 0;
			var options = new OptionSet {
				$"Usage: {Name}.exe OPTIONS* <package1.[apk|aab][desc]> [<package2.[apk|aab][desc]>]",
				"",
				"Compares APK/AAB packages content or APK/AAB package with content description",
				"",
				"Copyright 2020 Microsoft Corporation",
				"",
				"Options:",
				{ "bs",
					"Compare methods body size",
				  v => CompareMethodBodies = true },
				{ "c|comment=",
					"Comment to be saved inside description file",
				  v => Comment = v },
				{ "descrease-is-regression",
					"Report also size descrease as regression",
				  v => DecreaseIsRegression = true },
				{ "e|entry=",
					"Process only entries matching regex {PATTERN}",
				  v => entriesPattern = v },
				{ "f|flat",
					"Display flat comparison of entries, without showing comparison of their content",
				  v => Flat = true },
				{ "h|help|?",
					"Show this message and exit",
				  v => help = v != null },
				{ "keep-uncompressed-assemblies",
					"Save LZ4 uncompressed assemblies to temporary files",
				  v => KeepUncompressedAssemblies = true },
				{ "md",
					"Compare metadata sizes",
				  v => CompareMetadata = true },
				{ "test-apk-size-regression=",
					"Check whether apk size increased more than {BYTES}",
				  v => ApkRegressionThreshold = long.Parse (v) },
				{ "test-assembly-size-regression=",
					"Check whether any assembly size increased more than {BYTES}",
				  v => AssemblyRegressionThreshold = long.Parse (v) },
				{ "test-apk-percentage-regression=",
					"Check whether the apk size increased by more than {PERCENT}",
				  v => ApkRegressionThresholdPercentage = double.Parse (v) },
				{ "test-content-percentage-regression=",
					"Check whether any individual file size increased by more than {PERCENT}",
				  v => FileRegressionThresholdPercentage = double.Parse (v) },
				{ "s|save-descriptions",
					"Save .[apk|aab]desc description files next to the package(s) or to the specified path",
				  v => SaveDescriptions = true },
				{ "save-description-1=",
					"Save .[apk|aab]desc description for first package to {PATH}",
				  v => SaveDescription1 = v },
				{ "save-description-2=",
					"Save .[apk|aab]desc description for second package to {PATH}",
				  v => SaveDescription2 = v },
				{ "summary-only",
					"Output only summary information",
				  v => { SummaryOnly = true; Flat = true; } },
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

			if (help || args.Length < 1) {
				options.WriteOptionDescriptions (Out);

				Environment.Exit (helpExitCode);
			}

			if (remaining.Count != 2 && (ApkRegressionThreshold != 0 || AssemblyRegressionThreshold != 0)) {
				Error ("Please specify 2 APK packages for regression testing.");
				Environment.Exit (2);
			}

			if (remaining.Count != 2 && (remaining.Count != 1 || !SaveDescriptions)) {
				Error ("Please specify 2 APK/AAB packages to compare or 1 when using the -s option.");
				Environment.Exit (1);
			}

			return (remaining [0], remaining.Count > 1 ? remaining [1] : null);
		}
	}
}
