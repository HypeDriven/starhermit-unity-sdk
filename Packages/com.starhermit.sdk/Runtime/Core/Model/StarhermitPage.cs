using System;
using System.Collections;
using System.Collections.Generic;
using Starhermit.Json;

namespace Starhermit
{
    /// <summary>
    /// One page of a list endpoint, carrying the server's own paging metadata.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <remarks>
    /// Paging values come from the server and are authoritative: the SDK never recomputes a total or
    /// guesses a page size the deployment clamped.
    /// </remarks>
    public sealed class StarhermitPage<T> : IReadOnlyList<T>
    {
        /// <summary>Creates a page.</summary>
        /// <param name="items">Items on this page.</param>
        /// <param name="totalCount">Total matching items across all pages.</param>
        /// <param name="page">1-based page number this result represents.</param>
        /// <param name="pageSize">Page size the server actually applied.</param>
        /// <param name="rawJson">The JSON the page was read from.</param>
        public StarhermitPage(
            IReadOnlyList<T> items,
            int totalCount,
            int page,
            int pageSize,
            JsonValue? rawJson = null)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
            TotalCount = totalCount;
            Page = page;
            PageSize = pageSize;
            RawJson = rawJson ?? JsonValue.EmptyObject;
        }

        /// <summary>Items on this page, in server order.</summary>
        public IReadOnlyList<T> Items { get; }

        /// <summary>Total number of matching items the server reports.</summary>
        public int TotalCount { get; }

        /// <summary>1-based number of this page.</summary>
        public int Page { get; }

        /// <summary>Page size the server applied, which may be smaller than the one requested.</summary>
        public int PageSize { get; }

        /// <summary>The untouched JSON this page was read from.</summary>
        public JsonValue RawJson { get; }

        /// <summary>True when at least one further page exists.</summary>
        public bool HasMore => PageSize > 0 && (long)Page * PageSize < TotalCount;

        /// <summary>Number of pages implied by <see cref="TotalCount"/> and <see cref="PageSize"/>.</summary>
        public int TotalPages => PageSize > 0 ? (TotalCount + PageSize - 1) / PageSize : 0;

        /// <summary>Number of items on this page.</summary>
        public int Count => Items.Count;

        /// <summary>Gets the item at <paramref name="index"/> on this page.</summary>
        /// <param name="index">Zero-based index within the page.</param>
        public T this[int index] => Items[index];

        /// <inheritdoc />
        public IEnumerator<T> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Reads a page from the API's <c>{ items, totalCount, page, pageSize }</c> shape.</summary>
        /// <param name="json">The response object.</param>
        /// <param name="readItem">Converter for one element.</param>
        /// <returns>The parsed page.</returns>
        public static StarhermitPage<T> Read(JsonValue json, Func<JsonValue, T> readItem)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            if (readItem == null) throw new ArgumentNullException(nameof(readItem));

            // Some list routes answer with a bare array; treat that as a single complete page rather
            // than failing, so one code path serves both shapes.
            if (json.IsArray)
            {
                var all = json.AsList(readItem);
                return new StarhermitPage<T>(all, all.Count, 1, all.Count, json);
            }

            var items = json["items"].AsList(readItem);
            var pageSize = json["pageSize"].AsInt32OrDefault(items.Count);
            // The deployment spells the total "totalCount" on catalog and chat routes and "total" on
            // leaderboard and external-library ones. Both mean the same thing to a caller.
            var total = json["totalCount"];
            if (total.IsNullOrMissing) total = json["total"];
            return new StarhermitPage<T>(
                items,
                total.AsInt32OrDefault(items.Count),
                json["page"].AsInt32OrDefault(1),
                pageSize,
                json);
        }
    }
}
