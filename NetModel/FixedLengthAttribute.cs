using System;

namespace NetModel;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class FixedLengthAttribute : Attribute
{
	private int Length;

	public FixedLengthAttribute(int length)
	{
		Length = length;
	}
}
