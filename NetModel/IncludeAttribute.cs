using System;

namespace NetModel;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class IncludeAttribute : Attribute;