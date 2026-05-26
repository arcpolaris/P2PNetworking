using System.Collections.Generic;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace DemoGame.Util
{
	class UnityTraceListener : TraceListener
	{
		public HashSet<string> BlackList { get; private set; } = new();

		public UnityTraceListener(params string[] blacklist) => BlackList.UnionWith(blacklist);

		public override void Write(string message)
		{
			Debug.Log(message);
		}

		public override void WriteLine(string message)
			=> Write(message);

		public override void Write(object o, string category)
		{
			if (BlackList.Contains(category)) return;
			Debug.LogFormat("{0}: {1}", category, o);
		}

		public override void WriteLine(object o, string category)
			=> Write(o, category);

		public override void Write(string message, string category)
			=> Write((object)message, category);

		public override void WriteLine(string message, string category)
			=> Write((object)message, category);

		public override void Fail(string message)
		{
			Debug.LogError(message);
		}
	}
}