using System;
using System.Collections.Generic;
using System.Text;

namespace NetModel;

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public class ProtocolAttribute : Attribute
{
	public ProtocolAttribute()
	{

	}
}
