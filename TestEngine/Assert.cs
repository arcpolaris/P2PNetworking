using System.Diagnostics.CodeAnalysis;

namespace TestEngine;
[SuppressMessage("Performance", "CA1822:Mark members as static")]
internal class Assert
{
	public static Assert That = new();

	private Assert() { }

	public void IsTrue(bool condition, string? message = null)
	{
		if (condition) return;
		throw new AssertationFailedException(message);
	}

	public void Fail(string? message = null)
	{
		throw new AssertationFailedException(message);
	}

	public void AreEqual<T>(T? expected, T? actual, string? message = null)
	{
		if (expected is null && actual is null) return;
		if (expected?.Equals(actual) ?? false) return;
		throw new AssertationFailedException(message);
	}

	public void NonNull<T>(T? value, string? message = null)
	{
		if (value is not null) return;
		throw new AssertationFailedException(message);
	}

	public void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null) where T : IEquatable<T>
	{
		if(expected.SequenceEqual(actual)) return;
		throw new AssertationFailedException(message);
	}

	public void Inconclusive(string? message = null)
	{
		throw new InconclusiveTestException();
	}
}

internal class AssertationFailedException : Exception
{
	public AssertationFailedException() : base() { }
	public AssertationFailedException(string? message) : base(message) { }
	public AssertationFailedException(string? message, Exception? innerException) : base(message, innerException) { }
}

internal class InconclusiveTestException : Exception
{
	public InconclusiveTestException() : base() { }
	public InconclusiveTestException(string? message) : base(message) { }
	public InconclusiveTestException(string? message, Exception? innerException) : base(message, innerException) { }
}