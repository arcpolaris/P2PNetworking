using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;

internal sealed class TestAttribute : Attribute
{
	public bool Disabled { get; }

	public TestAttribute(bool disabled = false)
	{
		Disabled = disabled;
	}
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
internal sealed class TestCaseAttribute : Attribute
{
	public object?[] Arguments { get; }

	public TestCaseAttribute(params object?[] arguments)
	{
		Arguments = arguments ?? [];
	}
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
internal sealed class TagAttribute : Attribute
{
	public IReadOnlyList<string> Tags { get; }

	public TagAttribute(params string[] tags)
	{
		Tags = tags ?? [];
	}
}

internal static class Program
{
	private static readonly object Gate = new();

	private enum LogLevel
	{
		Debug = 0,
		Info = 1,
		Warn = 2,
		Error = 3
	}

	private static LogLevel MinimumLogLevel = LogLevel.Info;

	private static async Task<int> Main(string[] args)
	{
		Trace.Listeners.Add(new ConsoleTraceListener());
		Trace.AutoFlush = true;

		RootCommand root = new("Simple internal test runner")
		{
			TreatUnmatchedTokensAsErrors = true
		};

		Option<string?> assemblyOption = new("--assembly", "-a")
		{ Description = "Path to the test assembly. Defaults to the current executable." };

		Option<string?> classOption = new("--class", "-c")
		{ Description = "Filter by full or partial type name." };

		Option<string?> methodOption = new("--method", "-m")
		{ Description = "Filter by method name." };

		Option<string[]> tagOption = new("--tag", "-t")
		{
			Description = "Filter by tag. Repeatable.",
			AllowMultipleArgumentsPerToken = true
		};

		Option<bool> includeDisabledOption = new("--include-disabled")
		{ Description = "Include disabled tests." };

		Option<bool> listOption = new("--list", "-p")
		{ Description = "List matching tests without running them." };

		Option<bool> promptOption = new("--prompt", "-i")
		{ Description = "Force interactive prompt mode." };

		Option<string> logLevelOption = new("--log", "-l")
		{ Description = "Log level: debug, info, warn, error.", DefaultValueFactory = _ => "info" };

		root.Options.Add(assemblyOption);
		root.Options.Add(classOption);
		root.Options.Add(methodOption);
		root.Options.Add(tagOption);
		root.Options.Add(includeDisabledOption);
		root.Options.Add(listOption);
		root.Options.Add(promptOption);
		root.Options.Add(logLevelOption);

		root.SetAction(parseResult =>
		{
			string[] effectiveArgs = args;

			bool noArgs = args.Length == 0;
			bool forcedPrompt = parseResult.GetValue(promptOption);

			if (noArgs || forcedPrompt)
			{
				effectiveArgs = PromptForArgs();
				parseResult = root.Parse(effectiveArgs);
			}

			string? assemblyPath = parseResult.GetValue(assemblyOption);
			string? classFilter = parseResult.GetValue(classOption);
			string? methodFilter = parseResult.GetValue(methodOption);
			string[] tagFilters = parseResult.GetValue(tagOption) ?? [];
			bool includeDisabled = parseResult.GetValue(includeDisabledOption);
			bool listOnly = parseResult.GetValue(listOption);
			string logLevelText = parseResult.GetValue(logLevelOption) ?? "info";

			MinimumLogLevel = ParseLogLevel(logLevelText);

			return Run(
				assemblyPath,
				classFilter,
				methodFilter,
				tagFilters,
				includeDisabled,
				listOnly);
		});

		var parseResult = root.Parse(args);
		if (parseResult.Errors.Count > 0)
		{
			foreach (var err in parseResult.Errors)
				Console.Error.WriteLine(err.Message);

			return 1;
		}

		return await parseResult.InvokeAsync();
	}

	private static int Run(
		string? assemblyPath,
		string? classFilter,
		string? methodFilter,
		IReadOnlyList<string> tagFilters,
		bool includeDisabled,
		bool listOnly)
	{
		try
		{
			string resolvedAssembly = ResolveAssemblyPath(assemblyPath);
			Log(LogLevel.Info, $"Loading assembly: {resolvedAssembly}");

			Assembly assembly = Assembly.LoadFrom(resolvedAssembly);
			List<TestCaseDescriptor> tests = DiscoverTests(assembly);

			Log(LogLevel.Info, $"Discovered {tests.Count} test case(s).");

			List<TestCaseDescriptor> filtered = tests
				.Where(t => MatchesFilters(t, classFilter, methodFilter, tagFilters, includeDisabled))
				.ToList();

			Log(LogLevel.Info, $"Matched {filtered.Count} test case(s).");

			if (filtered.Count == 0)
			{
				Log(LogLevel.Warn, "No matching tests found.");
				return 1;
			}

			if (listOnly)
			{
				foreach (TestCaseDescriptor test in filtered)
				{
					Console.WriteLine(FormatTest(test));
				}

				return 0;
			}

			int passed = 0;
			int failed = 0;
			int skipped = 0;

			DateTimeOffset suiteStart = DateTimeOffset.UtcNow;

			foreach (TestCaseDescriptor test in filtered)
			{
				if (test.IsDisabled && !includeDisabled)
				{
					skipped++;
					Log(LogLevel.Info, $"SKIP {FormatTest(test)} [disabled]");
					continue;
				}

				DateTimeOffset start = DateTimeOffset.UtcNow;
				Log(LogLevel.Info, $"RUN  {FormatTest(test)}");

				try
				{
					RunSingle(test);
					TimeSpan elapsed = DateTimeOffset.UtcNow - start;
					passed++;
					Log(LogLevel.Info, $"PASS {FormatTest(test)} ({elapsed.TotalMilliseconds:F0} ms)");
				}
				catch (Exception ex) when (!Debugger.IsAttached)
				{
					TimeSpan elapsed = DateTimeOffset.UtcNow - start;
					failed++;
					Log(LogLevel.Error, $"FAIL {FormatTest(test)} ({elapsed.TotalMilliseconds:F0} ms)");
					LogException(ex);
				}
			}

			TimeSpan suiteElapsed = DateTimeOffset.UtcNow - suiteStart;
			Console.WriteLine();
			Console.WriteLine("========== SUMMARY ==========");
			Console.WriteLine($"Passed : {passed}");
			Console.WriteLine($"Failed : {failed}");
			Console.WriteLine($"Skipped: {skipped}");
			Console.WriteLine($"Total  : {passed + failed + skipped}");
			Console.WriteLine($"Time   : {suiteElapsed.TotalMilliseconds:F0} ms");
			Console.WriteLine("=============================");

			return failed == 0 ? 0 : 1;
		}
		catch (Exception ex) when (!Debugger.IsAttached)
		{
			Log(LogLevel.Error, "Fatal runner error.");
			LogException(ex);
			return 1;
		}
	}

	private static void RunSingle(TestCaseDescriptor test)
	{
		object? instance = null;

		if (!test.Method.IsStatic)
		{
			ConstructorInfo? ctor = test.DeclaringType.GetConstructor(
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				binder: null,
				Type.EmptyTypes,
				modifiers: null);

			if (ctor is null)
				throw new InvalidOperationException(
					$"Type '{test.DeclaringType.FullName}' requires a parameterless constructor for instance test methods.");

			instance = ctor.Invoke(null);
		}

		Type returnType = test.Method.ReturnType;
		ParameterInfo[] parameters = test.Method.GetParameters();

		if (returnType == typeof(void))
		{
			InvokeVoid(test.Method, instance, test.Arguments, parameters);
			return;
		}

		if (returnType == typeof(Task))
		{
			InvokeTask(test.Method, instance, test.Arguments, parameters).GetAwaiter().GetResult();
			return;
		}

		throw new InvalidOperationException(
			$"Test method '{test.Method.DeclaringType?.FullName}.{test.Method.Name}' must return void or Task.");
	}

	private static void InvokeVoid(MethodInfo method, object? instance, object?[] args, ParameterInfo[] parameters)
	{
		switch (parameters.Length)
		{
			case 0:
				((Action)method.CreateDelegate(typeof(Action), instance))();
				return;

			case 1:
				CreateAction1(method, instance).DynamicInvoke(args);
				return;

			case 2:
				CreateAction2(method, instance).DynamicInvoke(args);
				return;

			default:
				method.Invoke(instance, args);
				return;
		}
	}

	private static Task InvokeTask(MethodInfo method, object? instance, object?[] args, ParameterInfo[] parameters)
	{
		switch (parameters.Length)
		{
			case 0:
				return ((Func<Task>)method.CreateDelegate(typeof(Func<Task>), instance))();

			case 1:
				return (Task)CreateFuncTask1(method, instance).DynamicInvoke(args)!;

			case 2:
				return (Task)CreateFuncTask2(method, instance).DynamicInvoke(args)!;

			default:
				return (Task)method.Invoke(instance, args)!;
		}
	}

	private static Delegate CreateAction1(MethodInfo method, object? instance)
	{
		Type t1 = method.GetParameters()[0].ParameterType;
		Type delegateType = typeof(Action<>).MakeGenericType(t1);
		return method.CreateDelegate(delegateType, instance);
	}

	private static Delegate CreateAction2(MethodInfo method, object? instance)
	{
		Type[] types =
		[
			method.GetParameters()[0].ParameterType,
		method.GetParameters()[1].ParameterType
		];

		Type delegateType = typeof(Action<,>).MakeGenericType(types);
		return method.CreateDelegate(delegateType, instance);
	}

	private static Delegate CreateFuncTask1(MethodInfo method, object? instance)
	{
		Type t1 = method.GetParameters()[0].ParameterType;
		Type delegateType = typeof(Func<,>).MakeGenericType(t1, typeof(Task));
		return method.CreateDelegate(delegateType, instance);
	}

	private static Delegate CreateFuncTask2(MethodInfo method, object? instance)
	{
		Type[] types =
		[
			method.GetParameters()[0].ParameterType,
		method.GetParameters()[1].ParameterType,
		typeof(Task)
		];

		Type delegateType = typeof(Func<,,>).MakeGenericType(types);
		return method.CreateDelegate(delegateType, instance);
	}

	private static List<TestCaseDescriptor> DiscoverTests(Assembly assembly)
	{
		List<TestCaseDescriptor> tests = [];

		foreach (Type type in assembly.GetTypes()
			.Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false }))
		{
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
			{
				TestAttribute? testAttribute = method.GetCustomAttribute<TestAttribute>(inherit: false);
				if (testAttribute is null)
					continue;

				TagAttribute[] tagAttributes = method.GetCustomAttributes<TagAttribute>(inherit: false).ToArray();
				List<string> tags = tagAttributes.SelectMany(t => t.Tags).ToList();

				TestCaseAttribute[] testCases = method.GetCustomAttributes<TestCaseAttribute>(inherit: false).ToArray();

				if (testCases.Length == 0)
				{
					tests.Add(new TestCaseDescriptor(
						type,
						method,
						testAttribute.Disabled,
						tags,
						[],
						null));
				}
				else
				{
					for (int i = 0; i < testCases.Length; i++)
					{
						object?[] args = testCases[i].Arguments.ToArray();
						string suffix = $"[{i}]({string.Join(", ", args.Select(FormatArgument))})";

						tests.Add(new TestCaseDescriptor(
							type,
							method,
							testAttribute.Disabled,
							tags,
							args,
							suffix));
					}
				}
			}
		}

		return tests;
	}

	private static bool MatchesFilters(
		TestCaseDescriptor test,
		string? classFilter,
		string? methodFilter,
		IReadOnlyList<string> tagFilters,
		bool includeDisabled)
	{
		if (!includeDisabled && test.IsDisabled)
			return false;

		if (!string.IsNullOrWhiteSpace(classFilter))
		{
			string fullName = test.DeclaringType.FullName ?? test.DeclaringType.Name;
			if (!fullName.Contains(classFilter, StringComparison.OrdinalIgnoreCase) &&
				!test.DeclaringType.Name.Contains(classFilter, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}

		if (!string.IsNullOrWhiteSpace(methodFilter) &&
			!test.Method.Name.Contains(methodFilter, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (tagFilters.Count > 0)
		{
			HashSet<string> testTags = test.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase);

			foreach (string tag in tagFilters)
			{
				if (!testTags.Contains(tag))
					return false;
			}
		}

		return true;
	}

	private static string ResolveAssemblyPath(string? assemblyPath)
	{
		if (string.IsNullOrWhiteSpace(assemblyPath))
			return Assembly.GetExecutingAssembly().Location;

		return Path.GetFullPath(assemblyPath);
	}

	private static string[] PromptForArgs()
	{
		string? line = Console.ReadLine();

		if (string.IsNullOrWhiteSpace(line))
			return [];

		return CommandLineParser.SplitCommandLine(line).ToArray();
	}

	private static LogLevel ParseLogLevel(string value)
	{
		return value.Trim().ToLowerInvariant() switch
		{
			"debug" => LogLevel.Debug,
			"info" => LogLevel.Info,
			"warn" or "warning" => LogLevel.Warn,
			"error" => LogLevel.Error,
			_ => LogLevel.Info
		};
	}

	private static void Log(LogLevel level, string message)
	{
		if (level < MinimumLogLevel)
			return;

		lock (Gate)
		{
			Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {level.ToString().ToUpperInvariant(),-5} {message}");
		}
	}

	private static void LogException(Exception ex)
	{
		lock (Gate)
		{
			Console.WriteLine(ex);
		}
	}

	private static string FormatTest(TestCaseDescriptor test)
	{
		string baseName = $"{test.DeclaringType.FullName}.{test.Method.Name}";
		if (test.DisplaySuffix is null)
			return baseName;

		return baseName + test.DisplaySuffix;
	}

	private static string FormatArgument(object? value)
	{
		return value switch
		{
			null => "null",
			string s => $"\"{s}\"",
			char c => $"'{c}'",
			_ => value.ToString() ?? "<unknown>"
		};
	}

	private sealed record TestCaseDescriptor(
		Type DeclaringType,
		MethodInfo Method,
		bool IsDisabled,
		IReadOnlyList<string> Tags,
		object?[] Arguments,
		string? DisplaySuffix);
}