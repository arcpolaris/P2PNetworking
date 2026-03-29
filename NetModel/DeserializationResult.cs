using System;
using System.Collections.Generic;
using System.Text;

namespace NetModel;

[Flags]
public enum DeserializationResult
{
	Success = 1,
	Failure = 2,
	UnknownKey = 4
}