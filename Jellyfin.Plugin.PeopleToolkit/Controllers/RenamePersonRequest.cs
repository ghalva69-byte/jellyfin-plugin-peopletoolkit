using System;

namespace Jellyfin.Plugin.PeopleToolkit.Controllers;

/// <summary>
/// Request body for <see cref="PeopleToolkitController.RenamePerson"/>.
/// </summary>
public class RenamePersonRequest
{
    /// <summary>
    /// Gets or sets the person id to rename.
    /// </summary>
    public Guid PersonId { get; set; }

    /// <summary>
    /// Gets or sets the new name for this person.
    /// </summary>
    public string NewName { get; set; } = string.Empty;
}
