﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using K4os.Compression.LZ4;

namespace apkdiff {
	public class AssemblyDiff : EntryDiff {

		MetadataReader reader1;
		MetadataReader reader2;

		public AssemblyDiff ()
		{
		}

		public override string Name { get { return "Assemblies"; } }

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

					foreach (var pair in types1) {
						if (!types2.ContainsKey (pair.Key)) {
							Console.WriteLine ($"{padding}  -             Type {pair.Key}");
						} else
							CompareTypes (types1 [pair.Key], types2 [pair.Key], padding + "  ");
					}

					foreach (var pair in types2) {
						if (!types1.ContainsKey (pair.Key)) {
							Console.WriteLine ($"{padding}  +             Type {pair.Key}");
						}
					}
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

			foreach (var pair in dict1) {
				if (!dict2.ContainsKey (pair.Key)) {
					Console.WriteLine ($"{padding}  -             CustomAttribute {pair.Key}");
				}
			}

			foreach (var pair in dict2) {
				if (!dict1.ContainsKey (pair.Key)) {
					Console.WriteLine ($"{padding}  +             CustomAttribute {pair.Key}");
				}
			}
		}

		void CompareTypes (TypeDefinition type1, TypeDefinition type2, string padding)
		{
			CompareCustomAttributes (type1.GetCustomAttributes (), type2.GetCustomAttributes (), padding);
		}
	}
}
