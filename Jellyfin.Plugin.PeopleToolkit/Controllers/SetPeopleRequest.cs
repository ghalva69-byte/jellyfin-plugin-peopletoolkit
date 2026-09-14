using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.PeopleToolkit.Controllers;

/// <summary>
/// Request body for <see cref="PeopleToolkitController.SetPeople"/>.
/// </summary>
public class SetPeopleRequest
{
    /// <summary>
    /// Gets or sets the item id.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the list of people names to set on the item.
    /// </summary>
    [SuppressMessage("Design", "CA2227:Collection properties should be read only", Justification = "Required for JSON model binding.")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Required for JSON model binding.")]
    public List<string> PeopleNames { get; set; } = new();
}
