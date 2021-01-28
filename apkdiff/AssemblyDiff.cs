using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using K4os.Compression.LZ4;

namespace apkdiff {
	public class AssemblyDiff : EntryDiff {

		PEReader per1;
		PEReader per2;

		MetadataReader reader1;
		MetadataReader reader2;

		FileInfo fileInfo1;
		FileInfo fileInfo2;

		int bodySizes1;
		int bodySizes2;

		Regex regex;

		public long Length1 { get; private set; }
		public long Length2 { get; private set; }

		public bool ComapareMetadata { get; set; }
		public bool CompareMethodBodies { get; set; }

		public AssemblyDiff (string typesPattern = null)
		{
			if (typesPattern == null)
				return;

			regex = new Regex (typesPattern);
		}

		public override string Name { get { return "Assemblies"; } }

		TypeDefinition GetTypeDefinition (MetadataReader reader, TypeDefinitionHandle handle, out string fullName)
		{
			var typeDef = reader.GetTypeDefinition (handle);
			fullName = GetTypeFullname (reader, typeDef);

			return typeDef;
		}

		const uint CompressedDataMagic = 0x5A4C4158; // 'XALZ', little-endian
		static readonly ArrayPool<byte> bytePool = ArrayPool<byte>.Shared;

		(PEReader reader, long length) GetPEReaderCompressedAssembly (string filename, BinaryReader binReader, int length)
		{
			var fileInfo = new FileInfo (filename);
			var compressedData = bytePool.Rent ((int)(fileInfo.Length - 12));
			binReader.Read (compressedData, 0, compressedData.Length);

			var decodedData = bytePool.Rent (length);
			LZ4Codec.Decode (compressedData, decodedData);
			bytePool.Return (compressedData);

			return (new PEReader (new MemoryStream (decodedData)), (long)length);
		}

		internal static (bool isCompressed, UInt32 length, BinaryReader reader) GetUncompressedSize (Stream stream)
		{
			try {
				var reader = new BinaryReader (stream);
				var header = reader.ReadUInt32 ();
				reader.ReadUInt32 ();
				return (header == CompressedDataMagic, reader.ReadUInt32 (), reader);
			} catch {
				return (false, 0, null);
			}
		}

		(PEReader reader, long length) GetPEReader (string filename, string padding)
		{
			FileStream fileStream = null;
			try {
				fileStream = File.OpenRead (filename);
				(var isCompressed, var length, var binReader) = GetUncompressedSize (fileStream);
				if (isCompressed) {
					if (Program.Verbose)
						Program.ColorWriteLine ($"{padding}LZ4 compression detected for '{filename}'", ConsoleColor.Yellow);

					var reader = GetPEReaderCompressedAssembly (filename, binReader, (int)length);
					binReader.Dispose ();
					fileStream.Dispose ();

					return reader;
				}

				binReader.BaseStream.Seek (0, SeekOrigin.Begin);

				return (new PEReader (binReader.BaseStream), binReader.BaseStream.Length);

			} catch (Exception e) {
				if (fileStream != null)
					fileStream.Dispose ();

				throw new InvalidOperationException ($"Unable to read the assembly: {filename}\n{e}");
			}
		}

		void CompareManifestResources (string padding)
		{
			if (!per1.IsEntireImageAvailable || !per2.IsEntireImageAvailable)
				return;

			var res1 = GetResources (reader1, per1);
			var res2 = GetResources (reader2, per2);

			CompareSizes (res1, res2, "Resource", padding);
		}

		private Dictionary<string, int> GetResources (MetadataReader reader, PEReader per)
		{
			var resources = new Dictionary<string, int> ();

			per.PEHeaders.TryGetDirectoryOffset (per.PEHeaders.CorHeader.ResourcesDirectory, out int startOffset);

			if (startOffset < 0)
				return resources;

			var image = per.GetEntireImage ();
			foreach (var mh in reader.ManifestResources) {
				var mr = reader.GetManifestResource (mh);
				var name = reader.GetString (mr.Name);
				if (!mr.Implementation.IsNil)
					continue;

				var br = image.GetReader (startOffset + (int)mr.Offset, 4);
				var size = br.ReadInt32 ();
				resources [name] = size;
			}

			return resources;
		}

		public override void Compare (string file, string other, string padding)
		{
			fileInfo1 = new FileInfo (file);
			fileInfo2 = new FileInfo (other);

			(per1, Length1) = GetPEReader (file, padding);
			(per2, Length2) = GetPEReader (other, padding);

			using (per1) {
				using (per2) {
					reader1 = per1.GetMetadataReader ();
					reader2 = per2.GetMetadataReader ();

					if (ComapareMetadata)
						CompareMetadataStreams (padding);

					CompareManifestResources (padding);

					var types1 = new Dictionary<string, TypeDefinition> (reader1.TypeDefinitions.Count);
					var types2 = new Dictionary<string, TypeDefinition> (reader2.TypeDefinitions.Count);

					string fullName;

					foreach (var typeHandle in reader1.TypeDefinitions) {
						var td = GetTypeDefinition (reader1, typeHandle, out fullName);
						types1 [fullName] = td;
					}

					foreach (var typeHandle in reader2.TypeDefinitions) {
						var td = GetTypeDefinition (reader2, typeHandle, out fullName);
						types2 [fullName] = td;
					}

					CompareTypeDictionaries (types1, types2, padding, false);
				}
			}
		}

		void CompareMetadataStreams (string padding)
		{
			var diff = reader2.MetadataLength - reader1.MetadataLength;
			if (diff != 0) {
				Program.Print.Invoke ();
				Program.PrintDifference ("Metadata", diff, null, padding);
			}

			var sizes1 = GetMetadataStreamSizes (per1, reader1);
			var sizes2 = GetMetadataStreamSizes (per2, reader2);

			CompareDictionaries<int> (sizes1, sizes2, "Stream", padding + "  ", CompareStream);
		}

		string StreamKey (string key)
		{
			if (key != "#~")
				return key;

			return key + " (tables)";
		}

		void CompareStream (string key, int s1, int s2, string label, string padding)
		{
			if (s1 != s2)
				ColorAPILine (padding, s1 > s2 ? "-" : "+", s1 > s2 ? ConsoleColor.Green : ConsoleColor.Red, label, ConsoleColor.Green, StreamKey (key), true, s2 - s1);

			if (key != "#~")
				return;

			padding += "  ";

			foreach (var io in Enum.GetValues (typeof (TableIndex))) {
				var idx = (TableIndex) io;
				var len1 = reader1.GetTableRowSize (idx) * reader1.GetTableRowCount (idx);
				var len2 = reader2.GetTableRowSize (idx) * reader2.GetTableRowCount (idx);
				if (len1 == len2)
					continue;

				ColorAPILine (padding, len1 > len2 ? "-" : "+", len1 > len2 ? ConsoleColor.Green : ConsoleColor.Red, "Table", ConsoleColor.Green, idx.ToString (), true, len2 - len1);
			}
		}

		Dictionary<string, int> GetMetadataStreamSizes (PEReader per, MetadataReader reader)
		{
			var sizes = new Dictionary<string, int> ();
			per.PEHeaders.TryGetDirectoryOffset (per.PEHeaders.CorHeader.MetadataDirectory, out int startOffset);

			if (startOffset < 0)
				return sizes;

			var image = per.GetEntireImage ();
			var br = image.GetReader (startOffset, per.PEHeaders.MetadataSize);
			var signature = br.ReadInt32 ();
			if (signature != 0x424A5342)
				return sizes;

			// read version string length
			br.Offset = 12;
			var versionLength = br.ReadInt32 ();

			// read number of streams
			br.Offset = 16 + versionLength + 2;
			var nStreams = br.ReadInt16 ();

			for (int i = 0; i < nStreams; i++) {
				br.Offset += 4;

				var sSize = br.ReadInt32 ();
				var off = br.Offset;
				int l;
				for (l=0; l<32;) {
					var b = br.ReadByte ();
					l++;

					if (b == 0)
						break;
				}

				br.Offset = off;

				var sName = br.ReadUTF8 (l - 1);
				if (l < 32)
					br.Offset++;

				if ((l % 4) != 0)
					br.Offset += (4 - (l % 4));

				sizes [sName] = sSize;
			}

			return sizes;
		}

		string GetTypeName (MetadataReader reader, EntityHandle handle)
		{
			string fullName = "";

			if (handle.Kind == HandleKind.TypeDefinition) {
				GetTypeDefinition (reader, (TypeDefinitionHandle) handle, out fullName);

				return fullName;
			}

			if (handle.Kind != HandleKind.TypeReference)
				return null;

			var typeRef = reader.GetTypeReference ((TypeReferenceHandle)handle);
			var nspace = reader.GetString (typeRef.Namespace);

			if (!string.IsNullOrEmpty (nspace))
				fullName += nspace + ".";

			return fullName += reader.GetString (typeRef.Name);
		}

		Dictionary<string, CustomAttribute> GetCustomAttributes (MetadataReader reader, CustomAttributeHandleCollection cac)
		{
			var dict = new Dictionary<string, CustomAttribute> ();

			foreach (var handle in cac) {
				var ca = reader.GetCustomAttribute (handle);
				var cHandle = ca.Constructor;

				string typeName;

				switch (cHandle.Kind) {
				case HandleKind.MethodDefinition:
					var methodDef = reader.GetMethodDefinition ((MethodDefinitionHandle)cHandle);

					typeName = GetTypeName (reader, methodDef.GetDeclaringType ());
					break;
				case HandleKind.MemberReference:
					var memberDef = reader.GetMemberReference ((MemberReferenceHandle)cHandle);

					typeName = GetTypeName (reader, memberDef.Parent);
					break;
				default:
					Program.Warning ($"Unexpected EntityHandle kind: {cHandle.Kind}");
					continue;
				}

				dict [typeName] = ca;
			}

			return dict;
		}

		void CompareCustomAttributes (CustomAttributeHandleCollection cac1, CustomAttributeHandleCollection cac2, string padding)
		{
			var dict1 = GetCustomAttributes (reader1, cac1);
			var dict2 = GetCustomAttributes (reader2, cac2);

			CompareDictionaries<CustomAttribute> (dict1, dict2, "CustomAttribute", padding);
		}

		string GetFieldString (MetadataReader reader, TypeDefinition td, FieldDefinition fd)
		{
			StringBuilder sb = new StringBuilder ();

			if ((fd.Attributes & System.Reflection.FieldAttributes.Public) == System.Reflection.FieldAttributes.Public)
				sb.Append ("public ");
			if ((fd.Attributes & System.Reflection.FieldAttributes.Static) == System.Reflection.FieldAttributes.Static)
				sb.Append ("static ");
			if ((fd.Attributes & System.Reflection.FieldAttributes.InitOnly) == System.Reflection.FieldAttributes.InitOnly)
				sb.Append ("readonly ");

			var context = new GenericContext (new GenericParameterHandleCollection (), td.GetGenericParameters (), reader);

			return $"{sb.ToString ()}{fd.DecodeSignature<string, GenericContext> (new SignatureDecoder (), context)} {reader.GetString (fd.Name)}";
		}

		string GetPropertyString (MetadataReader reader, TypeDefinition td, PropertyDefinition pd)
		{
			StringBuilder sb = new StringBuilder ();

			var context = new GenericContext (new GenericParameterHandleCollection (), td.GetGenericParameters (), reader);
			var ms = pd.DecodeSignature<string, GenericContext> (new SignatureDecoder (), context);
			var pa = pd.GetAccessors ();
			var ga = pa.Getter;
			var sa = pa.Setter;
			sb.Append ($"{ms.ReturnType} {reader.GetString (pd.Name)} {{ ");

			if (!ga.IsNil) {
				sb.Append ("get; ");
			}

			if (!sa.IsNil) {
				sb.Append ("set; ");
			}

			sb.Append ('}');

			return sb.ToString ();
		}

		bool warnedAboutSigErr = false;

		string GetMethodString (MetadataReader reader, TypeDefinition td, MethodDefinition md)
		{
			StringBuilder sb = new StringBuilder ();

			if ((md.Attributes & System.Reflection.MethodAttributes.Public) == System.Reflection.MethodAttributes.Public)
				sb.Append ("public ");
			if ((md.Attributes & System.Reflection.MethodAttributes.Static) == System.Reflection.MethodAttributes.Static)
				sb.Append ("static ");

			var context = new GenericContext (md.GetGenericParameters (), td.GetGenericParameters (), reader);

			MethodSignature<string> signature = new MethodSignature<string> ();
			bool sigErr = false;
			try {
				signature = md.DecodeSignature<string, GenericContext> (new SignatureDecoder (), context);
			} catch (BadImageFormatException) {
				sigErr = true;
				if (!warnedAboutSigErr) {
					Program.Warning ("Exception in signature decoder. Some differences might be missing.");
					warnedAboutSigErr = true;
				}
			}

			sb.Append (sigErr ? "SIGERR" : signature.ReturnType);
			sb.Append (' ');
			sb.Append (reader.GetString (md.Name));

			sb.Append (" (");
			var first = true;

			if (sigErr) {
				sb.Append ("SIGERR");
			} else {
				foreach (var p in signature.ParameterTypes) {
					if (first)
						first = false;
					else
						sb.Append (", ");

					sb.Append (p);
				}
			}

			sb.Append (')');

			return sb.ToString ();
		}

		Dictionary<string, FieldDefinition> GetFields (MetadataReader reader, TypeDefinition type)
		{
			var dict = new Dictionary<string, FieldDefinition> ();
			foreach (var h in type.GetFields ()) {
				var fd = reader.GetFieldDefinition (h);
				dict [GetFieldString (reader, type, fd)] = fd;
			}

			return dict;
		}

		Dictionary<string, PropertyDefinition> GetProperties (MetadataReader reader, TypeDefinition type)
		{
			var dict = new Dictionary<string, PropertyDefinition> ();
			foreach (var h in type.GetProperties ()) {
				var pd = reader.GetPropertyDefinition (h);
				dict [GetPropertyString (reader, type, pd)] = pd;
			}

			return dict;
		}

		Dictionary<string, MethodDefinition> GetMethods (MetadataReader reader, TypeDefinition type)
		{
			var dict = new Dictionary<string, MethodDefinition> ();

			foreach (var h in type.GetMethods ()) {
				var md = reader.GetMethodDefinition (h);
				dict [GetMethodString (reader, type, md)] = md;
			}

			return dict;
		}

		void CompareFields (TypeDefinition type1, TypeDefinition type2, string padding)
		{
			var dict1 = GetFields (reader1, type1);
			var dict2 = GetFields (reader2, type2);

			CompareDictionaries<FieldDefinition> (dict1, dict2, "Field", padding);
		}

		void CompareProperties (TypeDefinition type1, TypeDefinition type2, string padding)
		{
			var dict1 = GetProperties (reader1, type1);
			var dict2 = GetProperties (reader2, type2);

			CompareDictionaries<PropertyDefinition> (dict1, dict2, "Property", padding);
		}

		int GetMethodBodySize (MethodDefinition md, PEReader per)
		{
			if (md.RelativeVirtualAddress == 0)
				return 0;

			return per.GetMethodBody (md.RelativeVirtualAddress).Size;
		}

		void AddMethodBodySize (bool orig, int size)
		{
			if (orig)
				bodySizes1 += size;
			else
				bodySizes2 += size;
		}

		void CompareMethods (TypeDefinition type1, TypeDefinition type2, string padding)
		{
			var dict1 = GetMethods (reader1, type1);
			var dict2 = GetMethods (reader2, type2);

			CompareDictionariesSizes<MethodDefinition> (dict1, dict2, "Method", padding, CompareMethodBodies ? GetMethodBodySize : (GetSizeDictionaryValues<MethodDefinition>)null, CompareMethodBodies ? AddMethodBodySize : (AddSizeDictionaryValues)null);
		}

		string GetTypeFullname (MetadataReader reader, TypeDefinition td)
		{
			StringBuilder sb = new StringBuilder ();
			//if (td.IsNested) {
			//	sb.Append (GetTypeFullname (reader, reader.GetTypeDefinition (td.GetDeclaringType ())));
			//}
			var ns = reader.GetString (td.Namespace);
			if (ns.Length > 0) {
				sb.Append (ns);
				sb.Append (".");
			}
			sb.Append (reader.GetString (td.Name));

			var gps = td.GetGenericParameters ();
			if (gps.Count > 0) {
				sb.Append ('<');

				for (int i = 0; i < 1; i++) {
					if (i > 0)
						sb.Append (", ");

					var gp = reader.GetGenericParameter (gps [i]);
					sb.Append (reader.GetString (gp.Name));
				}

				sb.Append ('>');
			}

			return sb.ToString ();
		}

		void CompareTypes (TypeDefinition type1, TypeDefinition type2, string padding)
		{
			var count = Program.Print.Push (new Action (() => {
				Console.Write (padding);
				Program.ColorWrite ("Type", ConsoleColor.Green);
				Console.WriteLine ($" {GetTypeFullname (reader1, type1)}");
			}));

			CompareCustomAttributes (type1.GetCustomAttributes (), type2.GetCustomAttributes (), padding);
			CompareFields (type1, type2, padding);
			CompareProperties (type1, type2, padding);
			CompareMethods (type1, type2, padding);

			var nTypes1 = GetNestedTypes (reader1, type1);
			var nTypes2 = GetNestedTypes (reader2, type2);

			CompareTypeDictionaries (nTypes1, nTypes2, padding, true);

			Program.Print.Pop (count);
		}

		void CompareTypeDictionaries (Dictionary<string, TypeDefinition> types1, Dictionary<string, TypeDefinition> types2, string padding, bool compareNested)
		{
			var origRegex = regex;
			foreach (var pair in types1) {
				var type = types1 [pair.Key];
				var compare = compareNested || !type.IsNested || regex != null;

				if (regex != null && !regex.IsMatch (pair.Key))
					continue;

				regex = null;

				if (!types2.ContainsKey (pair.Key)) {
					if (!compare)
						continue;

					ColorAPILine (padding, "-", ConsoleColor.Green, "Type", ConsoleColor.Green, pair.Key);
				} else {
					if (compare)
						CompareTypes (type, types2 [pair.Key], padding + "  ");
				}

				regex = origRegex;
			}

			regex = origRegex;

			foreach (var pair in types2) {
				if (regex != null && !regex.IsMatch (pair.Key))
					continue;

				regex = null;

				if (!types1.ContainsKey (pair.Key)) {
					if (!compareNested && pair.Value.IsNested && origRegex == null)
						continue;

					ColorAPILine (padding, "+", ConsoleColor.Red, "Type", ConsoleColor.Green, pair.Key);
				}
			}
		}

		Dictionary<string, TypeDefinition> GetNestedTypes (MetadataReader reader, TypeDefinition td)
		{
			var dict = new Dictionary<string, TypeDefinition> ();

			foreach (var nthd in td.GetNestedTypes ()) {
				var ntd = reader.GetTypeDefinition (nthd);
				dict [GetTypeFullname (reader, ntd)] = ntd;
			}

			return dict;
		}

		void ColorAPILine (string padding1, string sign, ConsoleColor signColor, string label, ConsoleColor labelColor, string name, bool printSizeDifference = false, int sizeDifference = 0)
		{
			Program.Print.Invoke ();
			Console.Write ($"{padding1}  ");
			Program.ColorWrite (sign, signColor);

			if (printSizeDifference)
				Program.ColorWrite ($"{Math.Abs (sizeDifference),12:#,0} ", signColor);
			else
				Console.Write ("             ");

			Program.ColorWrite (label, labelColor);
			Console.WriteLine ($" {name}");
		}

		delegate void CompareDictionaryValues<T> (string key, T i1, T i2, string label, string padding);
		delegate int GetSizeDictionaryValues<T> (T i, PEReader per);
		delegate void AddSizeDictionaryValues (bool orig, int size);

		void CompareDictionariesSizes<T> (Dictionary<string, T> dict1, Dictionary<string, T> dict2, string label, string padding, GetSizeDictionaryValues<T> getSize = null, AddSizeDictionaryValues addSize = null)
		{
			bool hasSize = getSize != null;
			int size1, size2;
			foreach (var key in dict1.Keys) {
				if (hasSize) {
					size1 = getSize (dict1 [key], per1);

					if (addSize != null)
						addSize (true, size1);
				} else
					size1 = 0;

				if (!dict2.ContainsKey (key))
					ColorAPILine (padding, "-", ConsoleColor.Green, label, ConsoleColor.Green, key, hasSize, size1);
				else if (hasSize) {
						size2 = getSize (dict2 [key], per2);

						if (addSize != null)
							addSize (false, size2);

					var diff = size2 - size1;
					if (diff == 0)
						continue;

					Program.Print.Invoke ();
					ColorAPILine (padding, diff > 0 ? "+" : "-", diff > 0 ? ConsoleColor.Red : ConsoleColor.Green, label, ConsoleColor.Green, key, true, diff);
				}
			}

			foreach (var key in dict2.Keys) {
				if (!dict1.ContainsKey (key)) {
					if (hasSize) {
						size2 = getSize (dict2 [key], per2);

						if (addSize != null)
							addSize (false, size2);
					} else
						size2 = 0;

					ColorAPILine (padding, "+", ConsoleColor.Red, label, ConsoleColor.Green, key, hasSize, size2);
				}
			}
		}

		void CompareDictionaries<T> (Dictionary<string, T> dict1, Dictionary<string, T> dict2, string label, string padding, CompareDictionaryValues<T> compare = null)
		{
			foreach (var key in dict1.Keys) {
				if (!dict2.ContainsKey (key))
					ColorAPILine (padding, "-", ConsoleColor.Green, label, ConsoleColor.Green, key);
				else if (compare != null)
					compare (key, dict1 [key], dict2 [key], label, padding);
			}

			foreach (var key in dict2.Keys) {
				if (!dict1.ContainsKey (key))
					ColorAPILine (padding, "+", ConsoleColor.Red, label, ConsoleColor.Green, key);
			}
		}

		void CompareSizes (Dictionary<string, int> col1, Dictionary<string, int> col2, string label, string padding)
		{
			foreach (var p in col1) {
				if (!col2.ContainsKey (p.Key))
					ColorAPILine (padding, "-", ConsoleColor.Green, label, ConsoleColor.Green, p.Key);
				else {
					var s1 = p.Value;
					var s2 = col2 [p.Key];
					if (s1 != s2)
						ColorAPILine (padding, s1 > s2 ? "-" : "+", s1 > s2 ? ConsoleColor.Green : ConsoleColor.Red, label, ConsoleColor.Green, p.Key, true, s2 - s1);
				}
			}

			foreach (var p in col2) {
				if (!col1.ContainsKey (p.Key))
					ColorAPILine (padding, "+", ConsoleColor.Red, label, ConsoleColor.Green, p.Key);
			}
		}

		public void Summary ()
		{
			Program.ColorWriteLine ("Summary:", ConsoleColor.Green);
			Program.PrintDifference ("File size", fileInfo2.Length - fileInfo1.Length, fileInfo1.Length);

			if (Length1 != fileInfo1.Length || Length2 != fileInfo2.Length)
				Program.PrintDifference ("Uncompressed size", Length2 - Length1, Length1);

			Program.PrintDifference ("Metadata size", reader2.MetadataLength - reader1.MetadataLength, reader1.MetadataLength);

			if (CompareMethodBodies)
				Program.PrintDifference ("Method bodies size", bodySizes2 - bodySizes1, bodySizes1);

			Program.PrintDifference ("Types count", reader2.TypeDefinitions.Count - reader1.TypeDefinitions.Count);
		}
	}
}
