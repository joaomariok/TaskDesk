using System.Reflection;
using TaskDesk.Core.Shell;

namespace TaskDesk.App.Tests;

/// <summary>
/// A do-nothing implementation of any service interface, for data contexts that only need to exist: every
/// member returns an empty collection, its type's default, or a completed task of either.
/// </summary>
public class Stub : DispatchProxy
{
    public static T Of<T>() where T : class => Create<T, Stub>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var type = targetMethod?.ReturnType;
        if (type is null || type == typeof(void))
        {
            return null;
        }

        if (type == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var result = type.GenericTypeArguments[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(result)
                .Invoke(null, [DefaultOf(result)]);
        }

        return DefaultOf(type);
    }

    private static object? DefaultOf(Type type)
    {
        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        // IReadOnlyList<T>, IEnumerable<T> and the like: an empty array, so callers can enumerate it.
        if (type.IsInterface && type.IsGenericType && type.GenericTypeArguments.Length == 1)
        {
            var empty = Array.CreateInstance(type.GenericTypeArguments[0], 0);
            if (type.IsInstanceOfType(empty))
            {
                return empty;
            }
        }

        return null;
    }
}

/// <summary>Settings held in memory, so nothing touches settings.json.</summary>
public sealed class InMemorySettingsStore : IShellSettingsStore
{
    private ShellSettings _settings = new();

    public ShellSettings Load() => _settings;

    public void Save(ShellSettings settings) => _settings = settings;
}
