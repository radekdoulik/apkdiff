using System;
using System.Reflection.Metadata;

namespace apkdiff
{
	public class GenericContext
	{
		public GenericParameterHandleCollection Parameters { get; }
		public GenericParameterHandleCollection TypeParameters { get; }
		public MetadataReader Reader { get; }

		public GenericContext (GenericParameterHandleCollection parameters, GenericParameterHandleCollection typeParameters, MetadataReader reader)
		{
			Parameters = parameters;
			TypeParameters = typeParameters;
			Reader = reader;
		}
	}
}
