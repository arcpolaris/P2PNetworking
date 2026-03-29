using System;

namespace NetModel;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class IncludeAttribute : Attribute;