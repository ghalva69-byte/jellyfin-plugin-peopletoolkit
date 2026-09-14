using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.PeopleToolkit.Controllers;

/// <summary>
/// Request body for <see cref="PeopleToolkitController.DeletePeopleBulk"/>.
/// </summary>
public class DeletePeopleRequest
{
    /// <summary>
    /// Gets or sets the list of person ids to delete.
    /// </summary>
    [SuppressMessage("Design", "CA2227:Collection properties should be read only", Justification = "Required for JSON model binding.")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Required for JSON model binding.")]
    public List<Guid> PersonIds { get; set; } = new();
}
