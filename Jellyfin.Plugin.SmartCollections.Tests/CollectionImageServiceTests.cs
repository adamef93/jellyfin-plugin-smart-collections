using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartCollections.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests
{
    /// <summary>
    /// Tests for <see cref="CollectionImageService"/>.
    /// </summary>
    /// <remarks>
    /// Shares the "BaseItemStaticState" collection with other tests that assign the
    /// static <see cref="BaseItem.LibraryManager"/> property, so they never run in parallel.
    /// </remarks>
    [Collection("BaseItemStaticState")]
    public class CollectionImageServiceTests
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILibraryQueryService _libraryQueryService;
        private readonly ILogger<CollectionImageService> _logger;
        private readonly CollectionImageService _sut;

        public CollectionImageServiceTests()
        {
            _libraryManager = Substitute.For<ILibraryManager>();
            _libraryQueryService = Substitute.For<ILibraryQueryService>();
            _logger = Substitute.For<ILogger<CollectionImageService>>();
            _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

            // BoxSet.GetItems resolves items through the static library manager.
            BaseItem.LibraryManager = _libraryManager;
            _sut = new CollectionImageService(_libraryManager, _libraryQueryService, _logger);
        }

        [Fact]
        public async Task SetImageAsync_NoPersonMatch_UsesMediaItemImage()
        {
            // Arrange
            var collection = new BoxSet { Name = "Sci-Fi", Id = Guid.NewGuid() };
            var movie = new Movie
            {
                Name = "Blade Runner",
                Id = Guid.NewGuid(),
                ImageInfos = new[]
                {
                    new ItemImageInfo
                    {
                        Type = ImageType.Primary,
                        Path = "/images/bladerunner.jpg",
                    },
                },
            };

            _libraryQueryService.FindPersonWithImage("Sci-Fi").Returns((Person?)null);
            _libraryManager.GetItemsResult(Arg.Any<InternalItemsQuery>())
                .Returns(new QueryResult<BaseItem>(new BaseItem[] { movie }));

            // Act
            await _sut.SetImageAsync(collection, null);

            // Assert
            await _libraryManager.Received(1).UpdateItemAsync(
                collection,
                Arg.Any<BaseItem>(),
                ItemUpdateType.ImageUpdate,
                Arg.Any<System.Threading.CancellationToken>());
        }

        [Fact]
        public async Task SetImageAsync_NoItemsWithImages_LogsWarningWithoutUpdating()
        {
            // Arrange
            var collection = new BoxSet { Name = "Barren", Id = Guid.NewGuid() };
            _libraryQueryService.FindPersonWithImage("Barren").Returns((Person?)null);
            _libraryManager.GetItemsResult(Arg.Any<InternalItemsQuery>())
                .Returns(new QueryResult<BaseItem>(Array.Empty<BaseItem>()));

            // Act
            await _sut.SetImageAsync(collection, null);

            // Assert
            await _libraryManager.DidNotReceive().UpdateItemAsync(
                Arg.Any<BaseItem>(),
                Arg.Any<BaseItem>(),
                Arg.Any<ItemUpdateType>(),
                Arg.Any<System.Threading.CancellationToken>());
        }

        [Fact]
        public async Task SetImageAsync_WithPersonImage_SetsImageFromPerson()
        {
            // Arrange
            var collection = new BoxSet { Name = "Test Collection", Id = Guid.NewGuid() };
            var person = new Person
            {
                Name = "Tom Hanks",
                Id = Guid.NewGuid(),
                ImageInfos = new[]
                {
                    new ItemImageInfo
                    {
                        Type = ImageType.Primary,
                        Path = "/images/tom.jpg",
                    },
                },
            };

            _libraryManager.UpdateItemAsync(
                Arg.Any<BaseItem>(),
                Arg.Any<BaseItem>(),
                Arg.Any<ItemUpdateType>(),
                Arg.Any<System.Threading.CancellationToken>())
                .Returns(Task.CompletedTask);

            // Act
            await _sut.SetImageAsync(collection, person);

            // Assert
            await _libraryManager.Received(1).UpdateItemAsync(
                collection,
                Arg.Any<BaseItem>(),
                ItemUpdateType.ImageUpdate,
                Arg.Any<System.Threading.CancellationToken>());
        }

        [Fact]
        public async Task SetImageAsync_WithNullPerson_TriesDetectedPerson()
        {
            // Arrange
            var collection = new BoxSet { Name = "Tom Hanks", Id = Guid.NewGuid() };
            var person = new Person
            {
                Name = "Tom Hanks",
                Id = Guid.NewGuid(),
                ImageInfos = new[]
                {
                    new ItemImageInfo
                    {
                        Type = ImageType.Primary,
                        Path = "/images/tom.jpg",
                    },
                },
            };

            _libraryQueryService.FindPersonWithImage("Tom Hanks").Returns(person);
            _libraryManager.UpdateItemAsync(
                Arg.Any<BaseItem>(),
                Arg.Any<BaseItem>(),
                Arg.Any<ItemUpdateType>(),
                Arg.Any<System.Threading.CancellationToken>())
                .Returns(Task.CompletedTask);

            // Act
            await _sut.SetImageAsync(collection, null);

            // Assert
            await _libraryManager.Received(1).UpdateItemAsync(
                collection,
                Arg.Any<BaseItem>(),
                ItemUpdateType.ImageUpdate,
                Arg.Any<System.Threading.CancellationToken>());
        }

        [Fact]
        public async Task SetImageAsync_NoPerson_NoImage_LogsWarning()
        {
            // Arrange
            var collection = new BoxSet { Name = "Empty Collection", Id = Guid.NewGuid() };
            _libraryQueryService.FindPersonWithImage("Empty Collection").Returns((Person?)null);

            // Act
            await _sut.SetImageAsync(collection, null);

            // Assert
            await _libraryManager.DidNotReceive().UpdateItemAsync(
                Arg.Any<BaseItem>(),
                Arg.Any<BaseItem>(),
                Arg.Any<ItemUpdateType>(),
                Arg.Any<System.Threading.CancellationToken>());
        }

        [Fact]
        public async Task SetImageAsync_PersonWithNoImageInfos_FallsThrough()
        {
            // Arrange
            var collection = new BoxSet { Name = "Test", Id = Guid.NewGuid() };
            var person = new Person
            {
                Name = "No Image Person",
                Id = Guid.NewGuid(),
                ImageInfos = Array.Empty<ItemImageInfo>(),
            };

            _libraryQueryService.FindPersonWithImage("Test").Returns((Person?)null);

            // Act
            await _sut.SetImageAsync(collection, person);

            // Assert
            await _libraryManager.DidNotReceive().UpdateItemAsync(
                Arg.Any<BaseItem>(),
                Arg.Any<BaseItem>(),
                Arg.Any<ItemUpdateType>(),
                Arg.Any<System.Threading.CancellationToken>());
        }

        [Fact]
        public async Task SetImageAsync_PersonWithNullImageInfos_FallsThrough()
        {
            // Arrange
            var collection = new BoxSet { Name = "Test2", Id = Guid.NewGuid() };
            var person = new Person
            {
                Name = "Null Infos",
                Id = Guid.NewGuid(),
                ImageInfos = null,
            };

            _libraryQueryService.FindPersonWithImage("Test2").Returns((Person?)null);

            // Act
            await _sut.SetImageAsync(collection, person);

            // Assert
            await _libraryManager.DidNotReceive().UpdateItemAsync(
                Arg.Any<BaseItem>(),
                Arg.Any<BaseItem>(),
                Arg.Any<ItemUpdateType>(),
                Arg.Any<System.Threading.CancellationToken>());
        }

        [Fact]
        public async Task SetImageAsync_PersonWithNonPrimaryImage_FallsThrough()
        {
            // Arrange
            var collection = new BoxSet { Name = "Test3", Id = Guid.NewGuid() };
            var person = new Person
            {
                Name = "Backdrop Only",
                Id = Guid.NewGuid(),
                ImageInfos = new[]
                {
                    new ItemImageInfo
                    {
                        Type = ImageType.Backdrop,
                        Path = "/images/backdrop.jpg",
                    },
                },
            };

            _libraryQueryService.FindPersonWithImage("Test3").Returns((Person?)null);

            // Act
            await _sut.SetImageAsync(collection, person);

            // Assert
            await _libraryManager.DidNotReceive().UpdateItemAsync(
                Arg.Any<BaseItem>(),
                Arg.Any<BaseItem>(),
                Arg.Any<ItemUpdateType>(),
                Arg.Any<System.Threading.CancellationToken>());
        }

        [Fact]
        public async Task SetImageAsync_ExceptionThrown_DoesNotRethrow()
        {
            // Arrange
            var collection = new BoxSet { Name = "Error Collection", Id = Guid.NewGuid() };
            var person = new Person
            {
                Name = "Bad Person",
                Id = Guid.NewGuid(),
                ImageInfos = new[]
                {
                    new ItemImageInfo
                    {
                        Type = ImageType.Primary,
                        Path = "/images/bad.jpg",
                    },
                },
            };

            _libraryManager.UpdateItemAsync(
                Arg.Any<BaseItem>(),
                Arg.Any<BaseItem>(),
                Arg.Any<ItemUpdateType>(),
                Arg.Any<System.Threading.CancellationToken>())
                .Returns(Task.FromException(new InvalidOperationException("DB error")));

            // Act & Assert - no exception should propagate
            await _sut.SetImageAsync(collection, person);
        }
    }
}
