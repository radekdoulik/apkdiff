﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using K4os.Compression.LZ4;

namespace apkdiff {
	public class AssemblyDiff : EntryDiff {

		MetadataReader reader1;
		MetadataReader reader2;

		public AssemblyDiff ()
		{
		}

		public override string Name { get { return "Assemblies"; } }

		List<Delegate> headers = new List<Delegate> ();

		TypeDefinition GetTypeDefinition (MetadataReader reader, TypeDefinitionHandle handle, out string fullName)
		{
			var typeDef = reader.GetTypeDefinition (handle);
			var name = reader.GetString (typeDef.Name);
			var nspace = reader.GetString (typeDef.Namespace);

			fullName = "";

			if (typeDef.IsNested) {
				string declTypeFullName;

				GetTypeDefinition (reader, typeDef.GetDeclaringType (), out declTypeFullName);
				fullName += declTypeFullName + "/";
			}

			if (!string.IsNullOrEmpty (nspace))
				fullName += nspace + ".";

			fullName += name;

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

		public override void Compare (string file, string other, string padding)
		{
			using (var per1 = GetPEReader (file, padding)) {
				using (var per2 = GetPEReader (other, padding)) {
					reader1 = per1.GetMetadataReader ();
					reader2 = per2.GetMetadataReader ();

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

		string GetMethodString (MetadataReader reader, MethodDefinition md)
		{
			StringBuilder sb = new StringBuilder ();

			if ((md.Attributes & System.Reflection.MethodAttributes.Static) == System.Reflection.MethodAttributes.Static)
				sb.Append ("static ");
			if ((md.Attributes & System.Reflection.MethodAttributes.Public) == System.Reflection.MethodAttributes.Public)
				sb.Append ("public ");

			var signature = md.DecodeSignature<string, GenericContext> (new SignatureDecoder (), new GenericContext (md.GetGenericParameters (), reader));

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

		Dictionary<string, MethodDefinition> GetMethods (MetadataReader reader, MethodDefinitionHandleCollection mhc)
		{
			var dict = new Dictionary<string, MethodDefinition> ();

			foreach (var h in mhc) {
				var md = reader.GetMethodDefinition (h);
				dict [GetMethodString (reader, md)] = md;
			}

			return dict;
		}

		void CompareMethods (MethodDefinitionHandleCollection mc1, MethodDefinitionHandleCollection mc2, string padding)
		{
			var dict1 = GetMethods (reader1, mc1);
			var dict2 = GetMethods (reader2, mc2);

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

			return sb.ToString ();
		}

		int PushHeader (Delegate d)
		{
			headers.Add (d);

			return headers.Count;
		}

		void PopHeader (int count)
		{
			if (headers.Count == count)
				headers.RemoveAt (headers.Count - 1);
		}

		void CompareTypes (TypeDefinition type1, TypeDefinition type2, string padding)
		{
			var count = PushHeader (new Action (() => Console.WriteLine ($"{padding}Type {GetTypeFullname (reader1, type1)}")));

			CompareCustomAttributes (type1.GetCustomAttributes (), type2.GetCustomAttributes (), padding);
			CompareMethods (type1.GetMethods (), type2.GetMethods (), padding);

			var nTypes1 = GetNestedTypes (reader1, type1);
			var nTypes2 = GetNestedTypes (reader2, type2);

			CompareTypeDictionaries (nTypes1, nTypes2, padding, true);

			PopHeader (count);
		}

		void CompareTypeDictionaries (Dictionary<string, TypeDefinition> types1, Dictionary<string, TypeDefinition> types2, string padding, bool compareNested)
		{
			foreach (var pair in types1) {
				if (!types2.ContainsKey (pair.Key)) {
					PrintHeader ();
					Console.WriteLine ($"{padding}  -             Type {pair.Key}");
				} else {
					var type = types1 [pair.Key];
					if (compareNested || !type.IsNested)
						CompareTypes (type, types2 [pair.Key], padding + "  ");
				}
			}

			foreach (var pair in types2) {
				if (!types1.ContainsKey (pair.Key)) {
					PrintHeader ();
					Console.WriteLine ($"{padding}  +             Type {pair.Key}");
				}
			}
		}

		Dictionary<string, TypeDefinition> GetNestedTypes (MetadataReader reader, TypeDefinition td)
		{
			var dict = new Dictionary<string, TypeDefinition> ();

			foreach (var nthd in td.GetNestedTypes ()) {
				var ntd = reader.GetTypeDefinition (nthd);
				dict [reader.GetString (ntd.Name)] = ntd;
			}

			return dict;
		}

		void CompareKeys (ICollection<string> col1, ICollection<string> col2, string name, string padding)
		{
			foreach (var key in col1) {
				if (!col2.Contains (key)) {
					PrintHeader ();
					Console.Write ($"{padding}  ");
					Program.ColorWrite ("-", ConsoleColor.Green);
					Console.WriteLine ($"             {name} {key}");
				}
			}

			foreach (var key in col2) {
				if (!col1.Contains (key)) {
					PrintHeader ();
					Console.Write ($"{padding}  ");
					Program.ColorWrite ("+", ConsoleColor.Red);
					Console.WriteLine ($"             {name} {key}");
				}
			}
		}

		void PrintHeader ()
		{
			foreach (var d in headers)
				d.DynamicInvoke ();

			headers.Clear ();
		}
	}
}
