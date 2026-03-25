using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetModel;

namespace TestEngine;

[CodeGenTest]
public partial interface ICodeGenTest { }

[TestClass]
public sealed class CodeGenTest
{
	

	[TestMethod]
	public void CodeGenSanity()
	{
		ICodeGenTest.Test();
	}
}
