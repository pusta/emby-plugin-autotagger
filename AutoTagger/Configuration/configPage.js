define(['baseView', 'loading', 'emby-input', 'emby-button', 'emby-checkbox', 'emby-scroller'], function (BaseView, loading) {
    'use strict';

    var pluginId = 'ddd2f8f0-cd06-41a9-a5c2-2b6e54160596';

    // The library rows are built with innerHTML rather than createElement on purpose. Emby's
    // inputs are customized built-in elements (<input is="emby-input">), and setting the "is"
    // attribute on an element created by document.createElement never upgrades it — the element
    // has to be parsed from markup. Anything interpolated into that markup is escaped here.
    function escapeAttribute(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;')
            .replace(/"/g, '&quot;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }

    function parseTags(value) {
        return value
            .split(',')
            .map(function (tag) { return tag.trim(); })
            .filter(function (tag) { return tag.length > 0; });
    }

    // Emby reports library ids as numeric strings, but the same rule may have been saved against
    // a GUID by an older build, or match on name alone. This mirrors Tagger.MatchesLibrary on the
    // server side.
    function ruleFor(config, folder) {
        var rules = config.Rules || [];

        for (var i = 0; i < rules.length; i++) {
            var rule = rules[i];
            if (!rule) {
                continue;
            }
            if (rule.LibraryId && folder.ItemId && String(rule.LibraryId) === String(folder.ItemId)) {
                return rule;
            }
            if (rule.LibraryName && folder.Name &&
                rule.LibraryName.toLowerCase() === folder.Name.toLowerCase()) {
                return rule;
            }
        }

        return null;
    }

    function renderLibraries(view, config, folders) {
        var container = view.querySelector('.autoTaggerLibraries');

        if (!folders || !folders.length) {
            container.innerHTML = '<p>No libraries found. Add a library first, then return here.</p>';
            return;
        }

        container.innerHTML = folders.map(function (folder) {
            var rule = ruleFor(config, folder);
            var name = escapeAttribute(folder.Name);
            var id = escapeAttribute(folder.ItemId);
            var tags = escapeAttribute(rule && rule.Tags ? rule.Tags.join(', ') : '');
            var excludeTags = escapeAttribute(rule && rule.ExcludeTags ? rule.ExcludeTags.join(', ') : '');
            var heading = name + (folder.CollectionType ? ' (' + escapeAttribute(folder.CollectionType) + ')' : '');

            return '<div class="autoTaggerLibrary" data-libraryid="' + id + '" data-libraryname="' + name + '">'
                + '<h3>' + heading + '</h3>'
                + '<div class="inputContainer">'
                + '<input is="emby-input" type="text" class="autoTaggerTagsInput"'
                + ' label="Tags to apply" value="' + tags + '" placeholder="e.g. kids, family-safe" />'
                + '</div>'
                + '<div class="inputContainer">'
                + '<input is="emby-input" type="text" class="autoTaggerExcludeInput"'
                + ' label="Skip items already tagged" value="' + excludeTags + '" placeholder="e.g. manual-review" />'
                + '<div class="fieldDescription">Leave empty to tag everything in this library.</div>'
                + '</div>'
                + '</div>';
        }).join('');
    }

    function collectRules(view) {
        var rows = view.querySelectorAll('.autoTaggerLibrary');
        var rules = [];

        for (var i = 0; i < rows.length; i++) {
            var row = rows[i];
            var input = row.querySelector('.autoTaggerTagsInput');
            if (!input) {
                continue;
            }

            var tags = parseTags(input.value);

            // A library with no tags to apply has nothing to exclude from, so it is not stored.
            if (!tags.length) {
                continue;
            }

            var excludeInput = row.querySelector('.autoTaggerExcludeInput');

            rules.push({
                LibraryId: row.getAttribute('data-libraryid') || '',
                LibraryName: row.getAttribute('data-libraryname') || '',
                Tags: tags,
                ExcludeTags: excludeInput ? parseTags(excludeInput.value) : []
            });
        }

        return rules;
    }

    function getVirtualFolders() {
        if (typeof ApiClient.getVirtualFolders === 'function') {
            return ApiClient.getVirtualFolders();
        }

        return ApiClient.getJSON(ApiClient.getUrl('Library/VirtualFolders'));
    }

    function onSubmit(e) {
        e.preventDefault();

        var view = this.view;

        loading.show();

        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            config.Rules = collectRules(view);
            config.TagEpisodesAndSeasons = view.querySelector('.autoTaggerTagEpisodes').checked;
            config.LockTags = view.querySelector('.autoTaggerLockTags').checked;

            ApiClient.updatePluginConfiguration(pluginId, config).then(function (result) {
                loading.hide();
                Dashboard.processPluginConfigurationUpdateResult(result);
            });
        });

        // Disable default form submission.
        return false;
    }

    function View(view, params) {
        BaseView.apply(this, arguments);

        view.querySelector('form').addEventListener('submit', onSubmit.bind(this));
    }

    Object.assign(View.prototype, BaseView.prototype);

    // onResume rather than a pageshow listener: Emby's dashboard raises pageshow through jQuery,
    // and a jQuery-triggered custom event never reaches a native addEventListener handler.
    View.prototype.onResume = function (options) {
        BaseView.prototype.onResume.apply(this, arguments);

        var view = this.view;

        loading.show();

        Promise.all([
            ApiClient.getPluginConfiguration(pluginId),
            getVirtualFolders()
        ]).then(function (results) {
            var config = results[0];
            var folders = results[1] || [];

            view.querySelector('.autoTaggerTagEpisodes').checked = config.TagEpisodesAndSeasons === true;
            view.querySelector('.autoTaggerLockTags').checked = config.LockTags === true;

            renderLibraries(view, config, folders);

            loading.hide();
        }, function (error) {
            loading.hide();
            view.querySelector('.autoTaggerLibraries').innerHTML =
                '<p>Could not load the Auto Tagger settings. Check the browser console and the server log.</p>';
            console.error('Auto Tagger configuration page failed to load', error);
        });
    };

    return View;
});
