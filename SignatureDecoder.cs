using System;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace apkdiff
{
	public class SignatureDecoder : ISignatureTypeProvider<string, GenericContext>
	{
		public string GetArrayType (string elementType, ArrayShape shape)
		{
			throw new NotImplementedException ();
		}

		public string GetByReferenceType (string elementType)
		{
			throw new NotImplementedException ();
		}

		public string GetFunctionPointerType (MethodSignature<string> signature)
		{
			throw new NotImplementedException ();
		}

		public string GetGenericInstantiation (string genericType, ImmutableArray<string> typeArguments)
		{
			throw new NotImplementedException ();
		}

		public string GetGenericMethodParameter (GenericContext genericContext, int index)
		{
			throw new NotImplementedException ();
		}

		public string GetGenericTypeParameter (GenericContext genericContext, int index)
		{
			throw new NotImplementedException ();
		}

		public string GetModifiedType (string modifier, string unmodifiedType, bool isRequired)
		{
			throw new NotImplementedException ();
		}

		public string GetPinnedType (string elementType)
		{
			throw new NotImplementedException ();
		}

		public string GetPointerType (string elementType)
		{
			throw new NotImplementedException ();
		}

		public string GetPrimitiveType (PrimitiveTypeCode typeCode)
		{
			switch (typeCode) {
				case PrimitiveTypeCode.Boolean:
					return "bool";
				case PrimitiveTypeCode.Byte:
					return "byte";
				case PrimitiveTypeCode.Char:
					return "char";
				case PrimitiveTypeCode.Double:
					return "double";
				case PrimitiveTypeCode.Int16:
					return "short";
				case PrimitiveTypeCode.Int32:
					return "int";
				case PrimitiveTypeCode.Int64:
					return "long";
				case PrimitiveTypeCode.IntPtr:
					return "IntPtr";
				case PrimitiveTypeCode.Object:
					return "object";
				case PrimitiveTypeCode.SByte:
					return "sbyte";
				case PrimitiveTypeCode.Single:
					return "float";
				case PrimitiveTypeCode.String:
					return "string";
				case PrimitiveTypeCode.TypedReference:
					return "TypedReference";
				case PrimitiveTypeCode.UInt16:
					return "ushort";
				case PrimitiveTypeCode.UInt32:
					return "uint";
				case PrimitiveTypeCode.UInt64:
					return "ulong";
				case PrimitiveTypeCode.UIntPtr:
					return "UIntPtr";
				case PrimitiveTypeCode.Void:
					return "void";
				default:
					throw new ArgumentOutOfRangeException (nameof (typeCode));
			}
		}

		public string GetSZArrayType (string elementType)
		{
			return $"{elementType}[]";
		}

		public string GetTypeFromDefinition (MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
		{
			throw new NotImplementedException ();
		}

		public string GetTypeFromReference (MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
		{
			throw new NotImplementedException ();
		}

		public string GetTypeFromSpecification (MetadataReader reader, GenericContext genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
		{
			throw new NotImplementedException ();
		}
	}
}
