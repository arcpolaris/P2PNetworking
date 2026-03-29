using System;
using System.Collections.Generic;
using System.Text;

namespace NetModel;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DynamicLengthAttribute : Attribute;
