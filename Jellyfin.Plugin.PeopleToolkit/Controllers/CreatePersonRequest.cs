namespace Jellyfin.Plugin.PeopleToolkit.Controllers;

/// <summary>
/// Request body for <see cref="PeopleToolkitController.CreatePerson"/>.
/// </summary>
public class CreatePersonRequest
{
    /// <summary>
    /// Gets or sets the name of the person to create.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
