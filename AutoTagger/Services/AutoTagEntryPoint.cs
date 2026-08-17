using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;

namespace AutoTagger.Services
{
    /// <summary>
    /// Listens for newly added and newly refreshed library items and tags them.
    /// </summary>
    /// <remarks>
    /// This is the Emby counterpart to the Jellyfin build's AutoTagService. The two platforms
    /// diverge sharply here:
    /// <list type="bullet">
    /// <item>Emby uses IServerEntryPoint with a synchronous Run method. Jellyfin removed
    /// IServerEntryPoint in 10.9 in favour of IHostedService with StartAsync/StopAsync.</item>
    /// <item>Emby discovers this class automatically — no DI registration is needed or possible.
    /// Constructor parameters are resolved from Emby's own container.</item>
    /// <item>Because Emby will not resolve plugin-owned types, Tagger is constructed here rather
    /// than injected.</item>
    /// </list>
    /// </remarks>
    public class AutoTagEntryPoint : IServerEntryPoint
    {
        /// <summary>
        /// The update reasons that mean a metadata provider just wrote to the item. The plugin's
        /// own writes use MetadataEdit, which is deliberately absent here so that re-applying a
        /// tag cannot retrigger this handler.
        /// </summary>
        private const ItemUpdateType MetadataReasons =
            ItemUpdateType.MetadataImport | ItemUpdateType.MetadataDownload;

        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly Tagger _tagger;

        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="AutoTagEntryPoint"/> class.
        /// </summary>
        /// <param name="libraryManager">The library manager, supplied by Emby.</param>
        /// <param name="logManager">The log manager, supplied by Emby.</param>
        public AutoTagEntryPoint(ILibraryManager libraryManager, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger("AutoTagger");
            _tagger = new Tagger(libraryManager, logManager);
        }

        /// <summary>
        /// Called once at server start-up. The instance is kept alive for the lifetime of the server.
        /// </summary>
        public void Run()
        {
            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemUpdated += OnItemUpdated;
            _logger.Info("Auto Tagger is listening for new library items");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases resources.
        /// </summary>
        /// <param name="disposing">Whether managed resources should be released.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _libraryManager.ItemAdded -= OnItemAdded;
                _libraryManager.ItemUpdated -= OnItemUpdated;
            }

            _disposed = true;
        }

        /// <summary>
        /// ItemAdded fires from LibraryManager.CreateItems, as soon as the item row is written and
        /// before the metadata providers have run. Tags are applied here so an item that never
        /// triggers a refresh save still gets them, but the Tags field is left unlocked so the
        /// providers can still contribute their own; OnItemUpdated takes the lock afterwards.
        /// </summary>
        /// <param name="sender">The event source.</param>
        /// <param name="e">The event arguments.</param>
        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            Apply(e.Item, false);
        }

        /// <summary>
        /// Runs once a metadata provider has saved the item, which is the point at which tags
        /// written on the ItemAdded path can already have been discarded: Emby's provider merge
        /// overwrites Tags outright when the refresh is a "replace all metadata" one. Re-applying
        /// here restores them, and is the only point at which locking the field is safe.
        /// </summary>
        /// <param name="sender">The event source.</param>
        /// <param name="e">The event arguments.</param>
        private void OnItemUpdated(object sender, ItemChangeEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            // Our own writes come back through this event with MetadataEdit. Without this guard
            // the handler would re-enter itself for every item it tags, forever.
            if ((e.UpdateReason & MetadataReasons) == 0)
            {
                return;
            }

            Apply(e.Item, true);
        }

        /// <summary>
        /// Both events are raised synchronously, in-line, on whichever thread is running the
        /// library scan or refresh — and the ItemUpdated handler would otherwise be writing to the
        /// item from inside the very call that is saving it. The work is pushed onto the thread
        /// pool for both reasons, and an exception must never escape back into the scanner.
        /// </summary>
        /// <param name="item">The item to tag.</param>
        /// <param name="metadataSettled">Whether the metadata providers have finished with the item.</param>
        private void Apply(BaseItem item, bool metadataSettled)
        {
            if (item == null)
            {
                return;
            }

            // A scan raises these events for every item on the server. Bailing out before the
            // thread pool is involved keeps an unconfigured plugin off the scan's critical path.
            var configuration = Plugin.Instance == null ? null : Plugin.Instance.Configuration;
            if (configuration == null || configuration.Rules == null || configuration.Rules.Length == 0)
            {
                return;
            }

            Task.Run(
                () =>
                {
                    try
                    {
                        _tagger.Apply(item, metadataSettled);
                    }
                    catch (Exception ex)
                    {
                        // Never let a tagging failure propagate into the library scan.
                        _logger.ErrorException("Failed to auto-tag {0}", ex, item.Name);
                    }
                },
                CancellationToken.None);
        }
    }
}
