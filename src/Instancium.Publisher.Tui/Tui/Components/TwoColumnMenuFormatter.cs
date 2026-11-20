namespace Instancium.Publisher.Tui.Components;

using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Formats selection menu items into aligned two-column table representations.
/// Prevents text overlap and misalignment in terminal selection prompts.
/// </summary>
public static class TwoColumnMenuFormatter
{
    private const int MinTitlePadding = 20;
    private const int ColumnGap = 4;

    /// <summary>
    /// Calculates optimal column widths and builds padded Spectre markup rows.
    /// </summary>
    /// <typeparam name="T">Source element type.</typeparam>
    /// <param name="items">Collection of elements to format.</param>
    /// <param name="titleSelector">Projection to primary column text.</param>
    /// <param name="descriptionSelector">Projection to secondary description text.</param>
    /// <returns>Lookup mapping each item to its formatted console markup string.</returns>
    public static Dictionary<T, string> FormatRows<T>(
        IReadOnlyCollection<T> items,
        Func<T, string> titleSelector,
        Func<T, string> descriptionSelector) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(titleSelector);
        ArgumentNullException.ThrowIfNull(descriptionSelector);

        int maxTitleLength = items.Count > 0
            ? items.Max(item => titleSelector(item).Length)
            : MinTitlePadding;

        int totalPadding = Math.Max(MinTitlePadding, maxTitleLength) + ColumnGap;

        var result = new Dictionary<T, string>(items.Count);
        foreach (T item in items)
        {
            string rawTitle = titleSelector(item);
            string rawDescription = descriptionSelector(item);

            string paddedTitle = rawTitle.PadRight(totalPadding);
            string formatted = $"[bold white]{Markup.Escape(paddedTitle)}[/][dim]{Markup.Escape(rawDescription)}[/]";
            result[item] = formatted;
        }

        return result;
    }
}