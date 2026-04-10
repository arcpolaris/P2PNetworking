using NetModel;
using Ardalis.GuardClauses;
using System;

namespace Ardalis.GuardClauses;

public static class NetworkGuards
{
	internal static void NotHost(this IGuardClause guardClause, Network network)
	{
		if (!network.IsHost)
		{
			throw new InvalidOperationException("This method is only valid for a host");
		}
	}

	internal static void NotClient(this IGuardClause guardClause, Network network)
	{
		if (network.IsHost)
		{
			throw new InvalidOperationException("This method is only valid for a client");
		}
	}

	/// <summary>
	/// Throws an <see cref="ArgumentException" /> or a custom <see cref="Exception" /> if  <paramref name="input"/> is not of type <typeparamref name="T"/>.
	/// </summary>
	/// <param name="guardClause"></param>
	/// <param name="input"></param>
	/// <param name="parameterName"></param>
	/// <param name="predicate"></param>
	/// <param name="message">Optional. Custom error message</param>
	/// <param name="exceptionCreator"></param>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	/// <exception cref="ArgumentException"></exception>
	/// <exception cref="Exception"></exception>
	public static T WrongType<T>(this IGuardClause guardClause, object input, string? message = null, Func<Exception>? exceptionCreator = null)
	{
		if (input is T instance)
			return instance;
		else
		{
			Exception? exception = exceptionCreator?.Invoke();
			throw exception ?? new ArgumentException(message ?? $"Argument of type {input.GetType()} was not of type {typeof(T).FullName}");
		}
	}
}
