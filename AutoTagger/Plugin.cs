using System;
using System.Collections.Generic;
using System.IO;
using AutoTagger.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace AutoTagger
{
    /// <summary>
    /// Auto Tagger plugin entry point.
    /// </summary>
    /// <remarks>
    /// Emby differences from the Jellyfin build of this plugin:
    /// <list type="bullet">
    /// <item>The plugin id must be returned from an overridden <see cref="Id"/> property.
    /// Jellyfin's BasePlugin declares it abstract; Emby's declares it virtual.</item>
    /// <item>Emby has no equivalent of Jellyfin's IPluginServiceRegistrator, so there is
    /// no DI registration file in this project.</item>
    /// <item>The dashboard icon comes from IHasThumbImage (an embedded resource stream),
    /// not from a manifest imagePath as it does with jprm on Jellyfin.</item>
    /// </list>
    /// </remarks>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasThumbImage
    {
        private readonly Guid _id = new Guid("ddd2f8f0-cd06-41a9-a5c2-2b6e54160596");

        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Server application paths, supplied by Emby.</param>
        /// <param name="xmlSerializer">Serializer used to persist the configuration, supplied by Emby.</param>
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static Plugin Instance { get; private set; }

        /// <inheritdoc />
        public override string Name
        {
            get { return "Auto Tagger"; }
        }

        /// <inheritdoc />
        public override string Description
        {
            get
            {
                return "Applies configured tags to media as it is added to selected libraries. " +
                       "Useful for tag-based parental controls, where a user policy allows or blocks content by tag.";
            }
        }

        /// <inheritdoc />
        public override Guid Id
        {
            get { return _id; }
        }

        /// <summary>
        /// Gets the strongly typed configuration. Convenience accessor matching Emby plugin convention.
        /// </summary>
        public PluginConfiguration PluginConfiguration
        {
            get { return Configuration; }
        }

        /// <inheritdoc />
        public ImageFormat ThumbImageFormat
        {
            get { return ImageFormat.Png; }
        }

        /// <inheritdoc />
        public Stream GetThumbImage()
        {
            var type = GetType();
            return type.Assembly.GetManifestResourceStream(type.Namespace + ".thumb.png");
        }

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    // Single token, no spaces: this value ends up in the dashboard page URL.
                    Name = "AutoTagger",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
                }
            };
        }
    }
}
