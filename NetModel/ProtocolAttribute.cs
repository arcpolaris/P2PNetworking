using System;

namespace NetModel;

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class ProtocolAttribute : Attribute;
