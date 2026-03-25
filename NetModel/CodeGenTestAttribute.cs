#if DEBUG
using System;

namespace NetModel;

[AttributeUsage(AttributeTargets.Interface)]
public class CodeGenTestAttribute : Attribute { }

#endif