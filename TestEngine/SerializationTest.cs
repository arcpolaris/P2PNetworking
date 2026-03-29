using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetModel;
using IgnoreAttribute = NetModel.IgnoreAttribute;

namespace TestEngine;

[Protocol]
public partial interface ITestProtocol;

[Schema(0)]
public sealed partial class EmptySchema : ITestProtocol;

[Schema(1)]
public sealed partial class PrimitiveSchema(int value1, int value2, bool value3) : ITestProtocol
{
	public int value1 = value1;
	public int value2 = value2;
	public bool value3 = value3;
}

[Schema(2)]
public sealed partial class VisibilitySchema(int value1, int value2, int value3, int value4) : ITestProtocol
{
	private int value1 = value1;
	[Include] private int value2 = value2;
	public int value3 = value3;
	[Ignore] public int value4 = value4;
}

[Schema(3)]
public sealed partial class DynamicSchema(string value1, string value2, int[] value3, int[] value4) : ITestProtocol
{
	[DynamicLength] public string value1 = value1;
	[FixedLength(16)] public string value2 = value2;
	[DynamicLength] public int[] value3 = value3;
	[FixedLength(16)] public int[] value4 = value4;
}

[Schema(4)]
public sealed partial class CompoundSchema(EmptySchema value1, PrimitiveSchema value2, VisibilitySchema value3, DynamicSchema value4) : ITestProtocol
{
	EmptySchema value1 = value1;
	PrimitiveSchema value2 = value2;
	VisibilitySchema value3 = value3;
	DynamicSchema value4 = value4;
}

[TestClass]
public sealed class SerializationTest
{
	[TestMethod]
	public void EmptySchemaDeserialize()
	{
		var x = new EmptySchema();
	}
}
