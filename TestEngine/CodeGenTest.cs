using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NetModel;

namespace TestEngine;

[Protocol]
public partial interface ITestProtocol
{

}

[TestClass]
public sealed class CodeGenTest
{
	

	[TestMethod]
	public void CodeGenSanity()
	{
		ITestProtocol.Test();
	}
}
