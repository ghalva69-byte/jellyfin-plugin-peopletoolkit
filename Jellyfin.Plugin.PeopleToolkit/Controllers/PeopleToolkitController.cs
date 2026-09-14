using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PeopleToolkit.Controllers;

/// <summary>
/// Allows setting, reading, renaming and deleting People, including on items that don't natively support it (e.g. Photos).
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("PeopleToolkit")]
public class PeopleToolkitController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly IPeopleRepository _peopleRepository;
    private readonly IDbContextFactory<JellyfinDbContext> _dbContextFactory;
    private readonly ILogger<PeopleToolkitController> _logger;
    private readonly IFileSystem _fileSystem;
    private readonly IProviderManager _providerManager;
    private readonly IServerConfigurationManager _configurationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeopleToolkitController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="peopleRepository">Instance of the <see cref="IPeopleRepository"/> interface.</param>
    /// <param name="dbContextFactory">Instance of the <see cref="IDbContextFactory{JellyfinDbContext}"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{PeopleToolkitController}"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
    /// <param name="configurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    public PeopleToolkitController(
        ILibraryManager libraryManager,
        IPeopleRepository peopleRepository,
        IDbContextFactory<JellyfinDbContext> dbContextFactory,
        ILogger<PeopleToolkitController> logger,
        IFileSystem fileSystem,
        IProviderManager providerManager,
        IServerConfigurationManager configurationManager)
    {
        _libraryManager = libraryManager;
        _peopleRepository = peopleRepository;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _fileSystem = fileSystem;
        _providerManager = providerManager;
        _configurationManager = configurationManager;
    }

    private void MoveCachedPersonImage(string oldName, string newName)
    {
        try
        {
            var oldItem = _libraryManager.GetPerson(oldName);
            var oldPath = oldItem?.Path;
            if (string.IsNullOrEmpty(oldPath) || !Directory.Exists(oldPath))
            {
                return;
            }

            var newItem = _libraryManager.GetPerson(newName);
            var newPath = newItem?.Path;
            if (string.IsNullOrEmpty(newPath) || string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (Directory.Exists(newPath))
            {
                Directory.Delete(newPath, true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            Directory.Move(oldPath, newPath);

            _providerManager.QueueRefresh(
                newItem!.Id,
                new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                {
                    ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                    MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                    ReplaceAllImages = false
                },
                RefreshPriority.High);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not move cached image for person {Old} -> {New}", oldName, newName);
        }
    }

    private void DeletePersonEntityAndImage(string name)
    {
        try
        {
            var item = _libraryManager.GetPerson(name);
            if (item is not null)
            {
                _libraryManager.DeleteItem(item, new DeleteOptions { DeleteFileLocation = true });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete person entity/image for {Name}", name);
        }
    }

    private void EnsurePersonExists(string name)
    {
        try
        {
            var existing = _libraryManager.GetPerson(name);
            if (existing is not null)
            {
                return;
            }

            var path = Person.GetPath(name);
            var normalize = _configurationManager.Configuration.EnableNormalizedItemByNameIds;
            var idKey = normalize ? path.ToLowerInvariant() : path;

            var personEntity = new Person
            {
                Name = name,
                Id = _libraryManager.GetNewItemId(idKey, typeof(Person)),
                DateCreated = DateTime.UtcNow,
                DateModified = DateTime.UtcNow,
                Path = path
            };
            personEntity.PresentationUniqueKey = personEntity.CreatePresentationUniqueKey();

            _libraryManager.CreateItem(personEntity, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create Person entity for {Name}", name);
        }
    }

    /// <summary>
    /// Creates a new Person entity directly, without attaching it to any item.
    /// </summary>
    /// <param name="request">The name of the person to create.</param>
    /// <returns>Diagnostic info about the operation.</returns>
    [HttpPost("CreatePerson")]
    public async Task<ActionResult> CreatePerson([FromBody] CreatePersonRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return BadRequest(new { Error = "Name is required" });
        }

        var db = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        try
        {
            var existingRow = await db.Peoples.FirstOrDefaultAsync(p => p.Name == name).ConfigureAwait(false);
            if (existingRow is not null)
            {
                return Ok(new { Created = false, AlreadyExisted = true, Name = name });
            }

            EnsurePersonExists(name);

            db.Peoples.Add(new()
            {
                Id = Guid.NewGuid(),
                Name = name,
                PersonType = "Actor"
            });
            await db.SaveChangesAsync().ConfigureAwait(false);

            return Ok(new { Created = true, AlreadyExisted = false, Name = name });
        }
        finally
        {
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Lists all real Person entities known to Jellyfin, with their actual item id (for image display).
    /// </summary>
    /// <returns>The list of people (id and name).</returns>
    [HttpGet("Gallery")]
    public ActionResult GetGallery()
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Person },
            Recursive = true
        };

        var people = _libraryManager.GetItemList(query)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new { p.Id, p.Name })
            .ToList();

        return Ok(new { Total = people.Count, Items = people });
    }

    /// <summary>
    /// Sets the list of people on an item directly at the repository level,
    /// bypassing the BaseItem.SupportsPeople restriction.
    /// </summary>
    /// <param name="request">The item id and list of people names.</param>
    /// <returns>Diagnostic info about the operation.</returns>
    [HttpPost("SetPeople")]
    public ActionResult SetPeople([FromBody] SetPeopleRequest request)
    {
        var item = _libraryManager.GetItemById(request.ItemId);
        if (item is null)
        {
            return NotFound(new { Error = "Item not found", request.ItemId });
        }

        var people = new List<PersonInfo>();
        foreach (var name in request.PeopleNames)
        {
            EnsurePersonExists(name);

            people.Add(new PersonInfo
            {
                Name = name,
                Type = PersonKind.Actor
            });
        }

        _peopleRepository.UpdatePeople(item.Id, people);

        var readBack = _peopleRepository.GetPeople(new InternalPeopleQuery { ItemId = item.Id });

        return Ok(new
        {
            ItemFound = true,
            ItemName = item.Name,
            PeopleWritten = people.Count,
            PeopleReadBackNames = readBack.Select(p => p.Name).ToList()
        });
    }

    /// <summary>
    /// Gets the list of people on an item directly from the repository level,
    /// bypassing the BaseItem.SupportsPeople restriction.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <returns>The list of people names on this item.</returns>
    [HttpGet("GetPeople/{itemId}")]
    public ActionResult GetPeople([FromRoute] Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound(new { Error = "Item not found", itemId });
        }

        var people = _peopleRepository.GetPeople(new InternalPeopleQuery { ItemId = item.Id });

        return Ok(new
        {
            ItemName = item.Name,
            People = people.Select(p => new { p.Name, p.Type, PersonId = p.Id }).ToList()
        });
    }

    /// <summary>
    /// Lists Photo items that currently have no People assigned.
    /// </summary>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <returns>The list of untagged photos.</returns>
    [HttpGet("UntaggedPhotos")]
    public ActionResult GetUntaggedPhotos([FromQuery] int limit = 100)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Photo },
            Recursive = true
        };

        var allPhotos = _libraryManager.GetItemList(query);
        var untagged = new List<object>();

        foreach (var photo in allPhotos)
        {
            var people = _peopleRepository.GetPeople(new InternalPeopleQuery { ItemId = photo.Id });
            if (people.Count == 0)
            {
                untagged.Add(new { photo.Id, photo.Name });
                if (untagged.Count >= limit)
                {
                    break;
                }
            }
        }

        return Ok(new { Total = untagged.Count, Items = untagged });
    }

    /// <summary>
    /// Renames a Person entity by its id, preserving all existing links to items.
    /// </summary>
    /// <param name="request">The person id and new name.</param>
    /// <returns>200 on success, 404 if the person was not found.</returns>
    [HttpPost("RenamePerson")]
    public async Task<ActionResult> RenamePerson([FromBody] RenamePersonRequest request)
    {
        var db = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        try
        {
            var person = await db.Peoples.FirstOrDefaultAsync(p => p.Id == request.PersonId).ConfigureAwait(false);
            if (person is null)
            {
                return NotFound(new { Error = "Person not found", request.PersonId });
            }

            var oldName = person.Name;
            var newName = request.NewName;

            EnsurePersonExists(newName);
            MoveCachedPersonImage(oldName, newName);

            var oldItem = _libraryManager.GetPerson(oldName);
            if (oldItem is not null)
            {
                _libraryManager.DeleteItem(oldItem, new DeleteOptions { DeleteFileLocation = false });
            }

            person.Name = newName;
            await db.SaveChangesAsync().ConfigureAwait(false);

            return Ok(new { OldName = oldName, NewName = person.Name });
        }
        finally
        {
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes a Person entity by its id, removing it and all its links to items.
    /// </summary>
    /// <param name="personId">The person id.</param>
    /// <returns>200 on success, 404 if the person was not found.</returns>
    [HttpPost("DeletePerson/{personId}")]
    public async Task<ActionResult> DeletePerson([FromRoute] Guid personId)
    {
        var db = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        try
        {
            var person = await db.Peoples.FirstOrDefaultAsync(p => p.Id == personId).ConfigureAwait(false);
            if (person is null)
            {
                return NotFound(new { Error = "Person not found", personId });
            }

            var name = person.Name;
            DeletePersonEntityAndImage(name);

            var links = db.PeopleBaseItemMap.Where(m => m.PeopleId == personId);
            db.PeopleBaseItemMap.RemoveRange(links);
            db.Peoples.Remove(person);

            await db.SaveChangesAsync().ConfigureAwait(false);

            return Ok(new { Deleted = true, Name = name });
        }
        finally
        {
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Lists Person entities that have no links to any item (orphaned).
    /// </summary>
    /// <returns>The list of orphaned people.</returns>
    [HttpGet("OrphanedPeople")]
    public async Task<ActionResult> GetOrphanedPeople()
    {
        var db = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        try
        {
            var orphaned = await db.Peoples
                .Where(p => !db.PeopleBaseItemMap.Any(m => m.PeopleId == p.Id))
                .Select(p => new { p.Id, p.Name })
                .ToListAsync().ConfigureAwait(false);

            return Ok(new { Total = orphaned.Count, Items = orphaned });
        }
        finally
        {
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Lists real Person items whose name has no corresponding row in the lightweight Peoples
    /// table (leftover items from bugs, testing, or manual database edits).
    /// </summary>
    /// <returns>The list of orphaned Person items.</returns>
    [HttpGet("OrphanedItems")]
    public async Task<ActionResult> GetOrphanedItems()
    {
        var db = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        try
        {
            var knownNames = await db.Peoples
                .Select(p => p.Name)
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            var knownNameSet = new HashSet<string>(knownNames, StringComparer.OrdinalIgnoreCase);

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Person },
                Recursive = true
            };

            var orphaned = _libraryManager.GetItemList(query)
                .Where(p => !knownNameSet.Contains(p.Name))
                .Select(p => new { p.Id, p.Name })
                .ToList();

            return Ok(new { Total = orphaned.Count, Items = orphaned });
        }
        finally
        {
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes several orphaned Person items (and their cached image folder) by their real item ids.
    /// </summary>
    /// <param name="request">The list of item ids to delete.</param>
    /// <returns>Diagnostic info about the operation.</returns>
    [HttpPost("DeleteOrphanedItems")]
    public ActionResult DeleteOrphanedItems([FromBody] DeleteOrphanedItemsRequest request)
    {
        var deletedNames = new List<string>();
        foreach (var itemId in request.ItemIds)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item is null)
            {
                continue;
            }

            deletedNames.Add(item.Name);
            _libraryManager.DeleteItem(item, new DeleteOptions { DeleteFileLocation = true });
        }

        return Ok(new { DeletedCount = deletedNames.Count, DeletedNames = deletedNames });
    }

    /// <summary>
    /// Deletes several Person entities at once by their ids.
    /// </summary>
    /// <param name="request">The list of person ids to delete.</param>
    /// <returns>Diagnostic info about the operation.</returns>
    [HttpPost("DeletePeople")]
    public async Task<ActionResult> DeletePeopleBulk([FromBody] DeletePeopleRequest request)
    {
        var db = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        try
        {
            var deletedNames = new List<string>();
            foreach (var id in request.PersonIds)
            {
                var person = await db.Peoples.FirstOrDefaultAsync(p => p.Id == id).ConfigureAwait(false);
                if (person is null)
                {
                    continue;
                }

                deletedNames.Add(person.Name);
                DeletePersonEntityAndImage(person.Name);

                var links = db.PeopleBaseItemMap.Where(m => m.PeopleId == id);
                db.PeopleBaseItemMap.RemoveRange(links);
                db.Peoples.Remove(person);
            }

            await db.SaveChangesAsync().ConfigureAwait(false);

            return Ok(new { DeletedCount = deletedNames.Count, DeletedNames = deletedNames });
        }
        finally
        {
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Lists all Person entities from the plugin's own repository, for autocomplete purposes.
    /// </summary>
    /// <returns>The list of all people (id and name).</returns>
    [HttpGet("AllPeople")]
    public async Task<ActionResult> GetAllPeople()
    {
        var db = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        try
        {
            var people = await db.Peoples
                .Select(p => new { p.Id, p.Name })
                .ToListAsync().ConfigureAwait(false);

            return Ok(new { Total = people.Count, Items = people });
        }
        finally
        {
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Lists Video items (movies, episodes, etc.) that currently have no People assigned.
    /// </summary>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <returns>The list of untagged videos.</returns>
    [HttpGet("UntaggedVideos")]
    public ActionResult GetUntaggedVideos([FromQuery] int limit = 100)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.Video, BaseItemKind.MusicVideo },
            Recursive = true
        };

        var allVideos = _libraryManager.GetItemList(query);
        var untagged = new List<object>();

        foreach (var video in allVideos)
        {
            var people = _peopleRepository.GetPeople(new InternalPeopleQuery { ItemId = video.Id });
            if (people.Count == 0)
            {
                untagged.Add(new { video.Id, video.Name });
                if (untagged.Count >= limit)
                {
                    break;
                }
            }
        }

        return Ok(new { Total = untagged.Count, Items = untagged });
    }
}
