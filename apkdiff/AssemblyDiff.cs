using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using K4os.Compression.LZ4;

namespace apkdiff {
	public class AssemblyDiff : EntryDiff {

		PEReader per1;
		PEReader per2;

		MetadataReader reader1;
		MetadataReader reader2;

		FileInfo fileInfo1;
		FileInfo fileInfo2;

		public AssemblyDiff ()
		{
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

		PEReader GetPEReaderCompressedAssembly (string filename, BinaryReader binReader, int length)
		{
			var fileInfo = new FileInfo (filename);
			var compressedData = bytePool.Rent ((int)(fileInfo.Length - 12));
			binReader.Read (compressedData, 0, compressedData.Length);

			var decodedData = bytePool.Rent (length);
			LZ4Codec.Decode (compressedData, decodedData);
			bytePool.Return (compressedData);

			return new PEReader (new MemoryStream (decodedData));
		}

		PEReader GetPEReader (string filename, string padding)
		{
			FileStream fileStream = null;
			try {
				fileStream = File.OpenRead (filename);

				var binReader = new BinaryReader (fileStream);
				var header = binReader.ReadUInt32 ();
				var descriptorIndex = binReader.ReadUInt32 ();
				var length = binReader.ReadUInt32 ();
				if (header == CompressedDataMagic) {
					if (Program.Verbose)
						Program.ColorWriteLine ($"{padding}LZ4 compression detected for '{filename}'", ConsoleColor.Yellow);

					var reader = GetPEReaderCompressedAssembly (filename, binReader, (int)length);
					binReader.Dispose ();
					fileStream.Dispose ();

					return reader;
				}

				binReader.BaseStream.Seek (0, SeekOrigin.Begin);

				return new PEReader (binReader.BaseStream);

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

			using (per1 = GetPEReader (file, padding)) {
				using (per2 = GetPEReader (other, padding)) {
					reader1 = per1.GetMetadataReader ();
					reader2 = per2.GetMetadataReader ();

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

			CompareKeys (dict1.Keys, dict2.Keys, "CustomAttribute", padding);
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

		string GetMethodString (MetadataReader reader, TypeDefinition td, MethodDefinition md)
		{
			StringBuilder sb = new StringBuilder ();

			if ((md.Attributes & System.Reflection.MethodAttributes.Public) == System.Reflection.MethodAttributes.Public)
				sb.Append ("public ");
			if ((md.Attributes & System.Reflection.MethodAttributes.Static) == System.Reflection.MethodAttributes.Static)
				sb.Append ("static ");

			var context = new GenericContext (md.GetGenericParameters (), td.GetGenericParameters (), reader);
			var signature = md.DecodeSignature<string, GenericContext> (new SignatureDecoder (), context);

			sb.Append (signature.ReturnType);
			sb.Append (' ');
			sb.Append (reader.GetString (md.Name));

			sb.Append (" (");
			var first = true;
			foreach (var p in signature.ParameterTypes) {
				if (first)
					first = false;
				else
					sb.Append (", ");

				sb.Append (p);
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

			CompareKeys (dict1.Keys, dict2.Keys, "Field", padding);
		}

		void CompareProperties (TypeDefinition type1, TypeDefinition type2, string padding)
		{
			var dict1 = GetProperties (reader1, type1);
			var dict2 = GetProperties (reader2, type2);

			CompareKeys (dict1.Keys, dict2.Keys, "Property", padding);
		}

		void CompareMethods (TypeDefinition type1, TypeDefinition type2, string padding)
		{
			var dict1 = GetMethods (reader1, type1);
			var dict2 = GetMethods (reader2, type2);

			CompareKeys (dict1.Keys, dict2.Keys, "Method", padding);
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
			foreach (var pair in types1) {
				var type = types1 [pair.Key];
				var compare = compareNested || !type.IsNested;

				if (!types2.ContainsKey (pair.Key)) {
					if (!compare)
						continue;

					ColorAPILine (padding, "-", ConsoleColor.Green, "Type", ConsoleColor.Green, pair.Key);
				} else {
					if (compare)
						CompareTypes (type, types2 [pair.Key], padding + "  ");
				}
			}

			foreach (var pair in types2) {
				if (!types1.ContainsKey (pair.Key)) {
					if (!compareNested && pair.Value.IsNested)
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

		void CompareKeys (ICollection<string> col1, ICollection<string> col2, string label, string padding)
		{
			foreach (var key in col1) {
				if (!col2.Contains (key))
					ColorAPILine (padding, "-", ConsoleColor.Green, label, ConsoleColor.Green, key);
			}

			foreach (var key in col2) {
				if (!col1.Contains (key))
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
			Program.PrintDifference ("Metadata size", reader2.MetadataLength - reader1.MetadataLength, reader1.MetadataLength);
			Program.PrintDifference ("Types count", reader2.TypeDefinitions.Count - reader1.TypeDefinitions.Count);
		}
	}
}
