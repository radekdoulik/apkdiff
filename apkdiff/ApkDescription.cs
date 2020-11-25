using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using System.Runtime.Serialization;
using Xamarin.Tools.Zip;
using System.Text.RegularExpressions;

namespace apkdiff {

	struct FileProperties : ISizeProvider {
		public long Size { get; set; }
	}

	[DataContract (Namespace = "apk")]
	public class ApkDescription {

		[DataMember]
		string Comment;

		[DataMember]
		public long PackageSize { get; protected set; }
		string PackagePath;

		ZipArchive Archive;

		[DataMember]
		readonly Dictionary<string, FileProperties> Entries = new Dictionary<string, FileProperties> ();

		Dictionary<string, (long Difference, long OriginalTotal)> totalDifferences = new Dictionary<string, (long, long)> ();

		Regex entriesRegex;

		public static ApkDescription Load (string path, string saveDescriptionPath = null)
		{
			if (!File.Exists (path)) {
				Program.Error ($"File '{path}' does not exist.");
				Environment.Exit (2);
			}

			var extension = Path.GetExtension (path);
			switch (extension.ToLower ()) {
			case ".apk":
			case ".aab":
				var nd = new ApkDescription ();

				nd.LoadApk (path, saveDescriptionPath);

				return nd;
			case ".apkdesc":
			case ".aabdesc":
				return LoadDescription (path);
			default:
				Program.Error ($"Unknown file extension '{extension}'");
				Environment.Exit (3);

				return null;
			}
		}

		void LoadApk (string path, string saveDescriptionPath = null)
		{
			Archive = ZipArchive.Open (path, FileMode.Open);

			if (Program.Verbose)
				Program.ColorWriteLine ($"Loading apk '{path}'", ConsoleColor.Yellow);

			PackageSize = new System.IO.FileInfo (path).Length;
			PackagePath = path;

			foreach (var entry in Archive) {
				var name = entry.FullName;

				if (Entries.ContainsKey (name)) {
					Program.Warning ("Duplicate APK file entry: {name}");
					continue;
				}

				Entries [name] = new FileProperties { Size = (long)entry.Size };

				if (Program.Verbose)
					Program.ColorWriteLine ($"  {entry.Size,12} {name}", ConsoleColor.Gray);
			}

			if (ApkDiff.SaveDescriptions) {
				var descPath = saveDescriptionPath ?? Path.ChangeExtension (path, Path.GetExtension (path) + "desc");

				Program.ColorWriteLine ($"Saving apk description to '{descPath}'", ConsoleColor.Yellow);
				SaveDescription (descPath);
			}
		}

		static ApkDescription LoadDescription (string path)
		{
			if (Program.Verbose)
				Program.ColorWriteLine ($"Loading description '{path}'", ConsoleColor.Yellow);

			using (var reader = File.OpenText (path)) {
				return new Newtonsoft.Json.JsonSerializer ().Deserialize (reader, typeof (ApkDescription)) as ApkDescription;
			}
		}

		void SaveDescription (string path)
		{
			Comment = ApkDiff.Comment;

			using (var writer = File.CreateText (path)) {
				new Newtonsoft.Json.JsonSerializer () { Formatting = Newtonsoft.Json.Formatting.Indented }.Serialize (writer, this);
			}
		}

		void AddToOthersTotal (long diff, long total)
		{
			string others = "Other entries";
			if (totalDifferences.ContainsKey (others)) {
				var info = totalDifferences [others];
				totalDifferences [others] = (info.Difference + diff, info.OriginalTotal + total);
			} else
				totalDifferences.Add (others, (diff, total));
		}

		public static EntryDiff ForExtension (string extension)
		{
			switch (extension) {
				case ".dll":
					return new AssemblyDiff ();
				case ".so":
					return new SharedLibraryDiff ();
				case ".dex":
					return new DexDiff ();
			}

			return null;
		}

		EntryDiff AddToTotalDifferences (string entry, long diff = 0, long total = 0)
		{
			var entryDiff = ForExtension (Path.GetExtension (entry));

			if (entryDiff == null) {
				AddToOthersTotal (diff, total);
				return null;
			}

			var diffType = entryDiff.Name;
			if (!totalDifferences.ContainsKey (diffType))
				totalDifferences.Add (diffType, (diff, total));
			else {
				var info = totalDifferences [diffType];
				totalDifferences [diffType] = (info.Difference + diff, info.OriginalTotal + total);
			}

			return entryDiff;
		}

		bool ShouldCompareEntry (string entry)
		{
			return entriesRegex == null || entriesRegex.IsMatch (entry);
		}

		public void Compare (ApkDescription other, string entriesPattern = null, bool flat = false)
		{
			var keys = Entries.Keys.Union (other.Entries.Keys);
			var differences = new Dictionary<string, long> ();
			var singles = new HashSet<string> ();
			var comparingApks = Archive != null && other.Archive != null;

			entriesRegex = string.IsNullOrEmpty (entriesPattern) ? null : new Regex (entriesPattern);

			Program.ColorWriteLine ("Size difference in bytes ([*1] apk1 only, [*2] apk2 only):", ConsoleColor.Yellow);

			foreach (var entry in Entries) {
				var key = entry.Key;
				if (other.Entries.ContainsKey (key)) {
					var otherEntry = other.Entries [key];
					differences [key] = otherEntry.Size - Entries [key].Size;
				} else {
					differences [key] = -Entries [key].Size;
					singles.Add (key);
				}

				if (ShouldCompareEntry (key))
					AddToTotalDifferences (key, total: Entries [key].Size);
			}

			foreach (var key in other.Entries.Keys) {
				if (Entries.ContainsKey (key))
					continue;

				differences [key] = other.Entries [key].Size;
				singles.Add (key);
			}

			foreach (var diff in differences.OrderByDescending (v => v.Value)) {
				if (!ShouldCompareEntry (diff.Key))
					continue;

				var single = singles.Contains (diff.Key);

				Action pa = new Action (() => Program.PrintDifference (diff.Key, diff.Value, single ? $" *{(diff.Value > 0 ? 2 : 1)}" : null));
				int count = -1;
				if (diff.Value == 0)
					count = Program.Print.Push (pa);
				else
					pa.DynamicInvoke ();

				EntryDiff entryDiff = AddToTotalDifferences (diff.Key, diff: diff.Value);
				if (entryDiff != null) {
					if (ApkDiff.AssemblyRegressionThreshold != 0 && entryDiff is AssemblyDiff && diff.Value > ApkDiff.AssemblyRegressionThreshold) {
						Program.Error ($"Assembly '{diff.Key}' size increase {diff.Value:#,0} is {diff.Value - ApkDiff.AssemblyRegressionThreshold:#,0} bytes more than the threshold {ApkDiff.AssemblyRegressionThreshold:#,0}.");
						ApkDiff.RegressionCount++;
					}

					if (!flat && comparingApks && !single)
						CompareEntries (new KeyValuePair<string, FileProperties> (diff.Key, Entries [diff.Key]), new KeyValuePair<string, FileProperties> (diff.Key, other.Entries [diff.Key]), other, entryDiff);
				}

				Program.Print.Pop (count);
			}

			Program.ColorWriteLine ("Summary:", ConsoleColor.Green);
			if (Program.Verbose)
				Program.ColorWriteLine ($"  apk1: {PackageSize,12}  {PackagePath}\n  apk2: {other.PackageSize,12}  {other.PackagePath}", ConsoleColor.Gray);

			foreach (var total in totalDifferences)
				Program.PrintDifference (total.Key, total.Value.Difference, total.Value.OriginalTotal);

			Program.PrintDifference ("Package size difference", other.PackageSize - PackageSize, PackageSize);
		}

		void CompareEntries (KeyValuePair<string, FileProperties> entry, KeyValuePair<string, FileProperties> other, ApkDescription otherApk, EntryDiff diff)
		{
			var tmpDir = Path.Combine (Path.GetTempPath (), Path.GetRandomFileName ());
			var tmpDirOther = Path.Combine (Path.GetTempPath (), Path.GetRandomFileName ());
			var padding = "  ";

			Directory.CreateDirectory (tmpDir);

			var zipEntry = Archive.ReadEntry (entry.Key, true);
			zipEntry.Extract (tmpDir, entry.Key);

			var zipEntryOther = otherApk.Archive.ReadEntry (other.Key, true);
			zipEntryOther.Extract (tmpDirOther, other.Key);

			if (Program.Verbose) {
				Program.ColorWriteLine ($"{padding}Extracted '{entry.Key}' compression method [{zipEntry.CompressionMethod}] to {tmpDir} and {tmpDirOther} temporary directories", ConsoleColor.Gray);
				if (zipEntry.CompressionMethod != zipEntryOther.CompressionMethod)
					Program.ColorWriteLine ($"{padding}  Compression methods differ: [{zipEntry.CompressionMethod}]*1 and [{zipEntryOther.CompressionMethod}]*2", ConsoleColor.Yellow);
			}

			diff.Compare (Path.Combine (tmpDir, entry.Key), Path.Combine (tmpDirOther, other.Key), padding);

			Directory.Delete (tmpDir, true);
			Directory.Delete (tmpDirOther, true);
		}
	}
}
