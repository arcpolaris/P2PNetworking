//using System.Diagnostics;
//using System.IO;
//using UnityEditor;
//using UnityEditor.Build;
//using UnityEditor.Build.Reporting;

//public class MessagePackBuilder : IPreprocessBuildWithReport
//{
//	public int callbackOrder => 0;
//	public void OnPreprocessBuild(BuildReport report) => Generate();

//	[MenuItem("Tools/MessagePack/Generate")]
//	public static void Generate()
//	{
//		string sourceDir = "obj/Generated";
//		string destDir = "Assets/Scripts/Generated";

//		Directory.CreateDirectory(destDir);

//		foreach (string file in Directory.GetFiles(sourceDir, "*.g.cs", SearchOption.AllDirectories))
//		{
//			string dest = Path.Combine(destDir, Path.GetFileName(file));
//			File.Copy(file, dest, overwrite: true);
//		}

//		AssetDatabase.Refresh();
//		UnityEngine.Debug.Log("MessagePack files copied.");
//	}

//}