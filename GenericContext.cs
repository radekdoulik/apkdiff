using System;
using System.Reflection.Metadata;

namespace apkdiff
{
	public class GenericContext
	{
		public GenericParameterHandleCollection Parameters { get; }
		public MetadataReader Reader { get; }

		public GenericContext (GenericParameterHandleCollection parameters, MetadataReader reader)
		{
			Parameters = parameters;
			Reader = reader;
		}
	}
}
