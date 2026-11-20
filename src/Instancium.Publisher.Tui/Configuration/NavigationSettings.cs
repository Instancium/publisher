using System;
using System.Collections.Generic;

namespace Instancium.Publisher.Tui.Configuration;

/// <summary>
/// TUI-specific navigation settings bound from the PublisherSettings configuration section.
/// </summary>
public sealed class NavigationSettings
{
    public Dictionary<string, string> RegisteredProjects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
