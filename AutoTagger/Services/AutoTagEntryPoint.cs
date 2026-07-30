using System;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;

namespace AutoTagger.Services
{
    /// <summary>
    /// Listens for newly added library items and tags them.
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
            }

            _disposed = true;
        }

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (e == null || e.Item == null)
            {
                return;
            }

            try
            {
                _tagger.TagItem(e.Item);
            }
            catch (Exception ex)
            {
                // Never let a tagging failure propagate into the library scan.
                _logger.ErrorException("Failed to tag {0}", ex, e.Item.Name);
            }
        }
    }
}
