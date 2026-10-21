using System.Globalization;

namespace TaskDesk.Presentation.Tests;

/// <summary>Runs a test under a known culture, so localized wording can be checked against literals.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);

    public void Dispose() => CultureInfo.CurrentCulture = _culture;
}
