using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Kapla
{
    internal static class KoboMetadata
    {
        private static readonly string[] DirectAuthorKeys =
        {
            "Author",
            "AuthorName",
            "AuthorDisplayName",
            "BookAuthor"
        };

        private static readonly string[] ContributorKeys =
        {
            "Contributors",
            "ContributorList",
            "Authors",
            "Creators",
            "Creator",
            "Contributor",
            "PrimaryContributor"
        };

        public static string FindAuthor(Dictionary<string, object> metadata)
        {
            if (metadata == null)
            {
                return null;
            }

            foreach (var key in DirectAuthorKeys)
            {
                var author = FindPersonName(GetValue(metadata, key));
                if (!String.IsNullOrWhiteSpace(author))
                {
                    return author;
                }
            }

            foreach (var key in ContributorKeys)
            {
                var author = FindContributor(GetValue(metadata, key));
                if (!String.IsNullOrWhiteSpace(author))
                {
                    return author;
                }
            }

            return FindNestedAuthor(metadata, 0);
        }

        public static string PreferAuthor(string candidate, string existing)
        {
            if (!IsFallbackAuthor(candidate))
            {
                return candidate.Trim();
            }
            if (!IsFallbackAuthor(existing))
            {
                return existing.Trim();
            }
            return "Unknown author";
        }

        public static bool IsFallbackAuthor(string author)
        {
            return String.IsNullOrWhiteSpace(author)
                || String.Equals(author.Trim(), "Unknown author", StringComparison.OrdinalIgnoreCase)
                || String.Equals(author.Trim(), "Kobo audiobook", StringComparison.OrdinalIgnoreCase);
        }

        private static string FindNestedAuthor(object value, int depth)
        {
            if (value == null || depth > 4)
            {
                return null;
            }

            var dictionary = value as Dictionary<string, object>;
            if (dictionary != null)
            {
                foreach (var key in DirectAuthorKeys.Concat(ContributorKeys))
                {
                    var candidate = FindPersonName(GetValue(dictionary, key));
                    if (!String.IsNullOrWhiteSpace(candidate))
                    {
                        return candidate;
                    }
                }

                foreach (var pair in dictionary)
                {
                    if (pair.Value == null || String.Equals(pair.Key, "Narrator", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var candidate = FindNestedAuthor(pair.Value, depth + 1);
                    if (!String.IsNullOrWhiteSpace(candidate))
                    {
                        return candidate;
                    }
                }
                return null;
            }

            var values = value as IEnumerable;
            if (values == null || value is string || value is IDictionary)
            {
                return null;
            }
            foreach (var item in values)
            {
                var candidate = FindNestedAuthor(item, depth + 1);
                if (!String.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static string FindContributor(object value)
        {
            var dictionary = value as Dictionary<string, object>;
            if (dictionary != null)
            {
                var role = FirstString(dictionary, "Role", "ContributorType", "Relation", "Contribution");
                var name = FindPersonName(dictionary);
                if (IsNarratorRole(role))
                {
                    return null;
                }
                if (!String.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
                foreach (var child in dictionary.Values)
                {
                    var nested = FindContributor(child);
                    if (!String.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
                return null;
            }

            var values = value as IEnumerable;
            if (values == null || value is string || value is IDictionary)
            {
                return FindPersonName(value);
            }

            var entries = values.Cast<object>().ToList();
            foreach (var entry in entries)
            {
                var entryDictionary = entry as Dictionary<string, object>;
                var role = entryDictionary == null ? null : FirstString(entryDictionary, "Role", "ContributorType", "Relation", "Contribution");
                if (IsAuthorRole(role))
                {
                    var name = FindPersonName(entry);
                    if (!String.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }
            foreach (var entry in entries)
            {
                var entryDictionary = entry as Dictionary<string, object>;
                var role = entryDictionary == null ? null : FirstString(entryDictionary, "Role", "ContributorType", "Relation", "Contribution");
                if (!IsNarratorRole(role))
                {
                    var name = FindPersonName(entry);
                    if (!String.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }
            return null;
        }

        private static string FindPersonName(object value)
        {
            var text = value as string;
            if (!String.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }

            var dictionary = value as Dictionary<string, object>;
            if (dictionary == null)
            {
                return null;
            }

            var name = FirstString(dictionary, "Name", "DisplayName", "FullName", "PersonName", "Author", "AuthorName", "Creator", "CreatorName");
            if (!String.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }

            var first = FirstString(dictionary, "FirstName", "GivenName");
            var last = FirstString(dictionary, "LastName", "FamilyName", "Surname");
            if (!String.IsNullOrWhiteSpace(first) || !String.IsNullOrWhiteSpace(last))
            {
                return String.Join(" ", new[] { first, last }.Where(part => !String.IsNullOrWhiteSpace(part))).Trim();
            }

            return null;
        }

        private static bool IsAuthorRole(string role)
        {
            return !String.IsNullOrWhiteSpace(role) && (role.IndexOf("author", StringComparison.OrdinalIgnoreCase) >= 0 || role.IndexOf("writer", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsNarratorRole(string role)
        {
            return !String.IsNullOrWhiteSpace(role) && (role.IndexOf("narrat", StringComparison.OrdinalIgnoreCase) >= 0 || role.IndexOf("reader", StringComparison.OrdinalIgnoreCase) >= 0 || role.IndexOf("voice", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string FirstString(Dictionary<string, object> dictionary, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = GetValue(dictionary, key);
                var text = value as string;
                if (!String.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }

                var values = value as IEnumerable;
                if (values != null && !(value is IDictionary))
                {
                    var strings = values.Cast<object>()
                        .Select(item => item as string)
                        .Where(item => !String.IsNullOrWhiteSpace(item))
                        .Select(item => item.Trim())
                        .ToList();
                    if (strings.Count > 0)
                    {
                        return String.Join(", ", strings);
                    }
                }
            }
            return null;
        }

        private static object GetValue(Dictionary<string, object> dictionary, string key)
        {
            if (dictionary == null)
            {
                return null;
            }
            var pair = dictionary.FirstOrDefault(value => String.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase));
            return String.IsNullOrEmpty(pair.Key) ? null : pair.Value;
        }
    }
}
