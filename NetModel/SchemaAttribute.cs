using System;
namespace NetModel;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class SchemaAttribute : Attribute
{
	private ushort Key;

	public SchemaAttribute(ushort key)
	{
		Key = key;
	}
}
