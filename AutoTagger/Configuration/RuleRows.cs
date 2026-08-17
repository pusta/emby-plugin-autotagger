using System;
using System.Collections.Generic;
using System.Linq;
using Emby.Web.GenericEdit;
using MediaBrowser.Model.Entities;

namespace AutoTagger.Configuration
{
    /// <summary>
    /// Converts between the stored rules and the rows shown on the settings page.
    /// </summary>
    /// <remarks>
    /// Kept separate from <c>Plugin</c>, and free of any dependency on the server, so that the
    /// conversion both ways can be exercised without a running Emby.
    /// </remarks>
    public static class RuleRows
    {
        /// <summary>
        /// Builds one editable row per library, seeded from the stored rule for that library.
        /// </summary>
        /// <param name="folders">The server's libraries.</param>
        /// <param name="stored">The stored rules.</param>
        /// <returns>The rows to display, in library order.</returns>
        public static EditableObjectCollection Build(IEnumerable<VirtualFolderInfo> folders, LibraryTagRule[] stored)
        {
            var rows = new EditableObjectCollection();

            if (folders == null)
            {
                return rows;
            }

            var rules = stored ?? new LibraryTagRule[0];

            foreach (var folder in folders)
            {
                if (folder == null)
                {
                    continue;
                }

                var rule = FindRule(rules, folder);

                rows.Add(new LibraryRuleRow
                {
                    LibraryId = folder.ItemId ?? string.Empty,
                    LibraryName = folder.Name ?? string.Empty,
                    Tags = rule == null ? string.Empty : JoinTags(rule.Tags),
                    ExcludeTags = rule == null ? string.Empty : JoinTags(rule.ExcludeTags)
                });
            }

            return rows;
        }

        /// <summary>
        /// Folds the edited rows back into stored rules. A row with no tags to apply is dropped:
        /// it has nothing to exclude from either.
        /// </summary>
        /// <param name="rows">The rows from the settings page.</param>
        /// <returns>The rules to store.</returns>
        public static LibraryTagRule[] ToRules(IEnumerable<LibraryRuleRow> rows)
        {
            if (rows == null)
            {
                return new LibraryTagRule[0];
            }

            var rules = new List<LibraryTagRule>();

            foreach (var row in rows)
            {
                if (row == null)
                {
                    continue;
                }

                var tags = ParseTags(row.Tags);

                if (tags.Length == 0)
                {
                    continue;
                }

                rules.Add(new LibraryTagRule
                {
                    LibraryId = row.LibraryId ?? string.Empty,
                    LibraryName = row.LibraryName ?? string.Empty,
                    Tags = tags,
                    ExcludeTags = ParseTags(row.ExcludeTags)
                });
            }

            return rules.ToArray();
        }

        /// <summary>
        /// Finds the stored rule for a library, by id first and then by name.
        /// </summary>
        /// <param name="rules">The stored rules.</param>
        /// <param name="folder">The library.</param>
        /// <returns>The matching rule, or <c>null</c>.</returns>
        private static LibraryTagRule FindRule(LibraryTagRule[] rules, VirtualFolderInfo folder)
        {
            foreach (var rule in rules)
            {
                if (rule == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(rule.LibraryId)
                    && !string.IsNullOrEmpty(folder.ItemId)
                    && string.Equals(rule.LibraryId, folder.ItemId, StringComparison.OrdinalIgnoreCase))
                {
                    return rule;
                }

                if (!string.IsNullOrEmpty(rule.LibraryName)
                    && string.Equals(rule.LibraryName, folder.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return rule;
                }
            }

            return null;
        }

        private static string JoinTags(string[] tags)
        {
            return tags == null ? string.Empty : string.Join(", ", tags);
        }

        private static string[] ParseTags(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new string[0];
            }

            return value
                .Split(',')
                .Select(tag => tag.Trim())
                .Where(tag => tag.Length > 0)
                .ToArray();
        }
    }
}
