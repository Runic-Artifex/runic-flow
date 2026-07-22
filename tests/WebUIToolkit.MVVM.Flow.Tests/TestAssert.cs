using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebUIToolkit.MVVM.Flow.Tests;

internal static class TestAssert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message ?? "Expected condition to be true.");
        }
    }

    public static void False(bool condition, string? message = null) =>
        True(!condition, message ?? "Expected condition to be false.");

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                message ?? $"Expected '{expected}', but found '{actual}'.");
        }
    }

    public static void SequenceEqual<T>(
        IReadOnlyList<T> expected,
        IReadOnlyList<T> actual,
        string? message = null)
    {
        if (expected.Count != actual.Count)
        {
            throw new InvalidOperationException(
                message ?? $"Expected {expected.Count} values, but found {actual.Count}.");
        }

        for (int index = 0; index < expected.Count; index++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[index], actual[index]))
            {
                throw new InvalidOperationException(
                    message ?? $"Values differ at index {index}: expected '{expected[index]}', but found '{actual[index]}'.");
            }
        }
    }

    public static async ValueTask<TException> ThrowsAsync<TException>(Func<ValueTask> action)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
