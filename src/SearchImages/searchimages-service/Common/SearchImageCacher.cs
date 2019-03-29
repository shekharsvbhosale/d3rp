﻿using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace SearchImagesService.Common
{
    public class SearchImageCacher
    {
        private readonly ConcurrentDictionary<DapiSearchType, SemaphoreSlim> _locks = new ConcurrentDictionary<DapiSearchType, SemaphoreSlim>();
        private readonly HttpClient _http;
        private readonly Random _rng;
        private readonly SortedSet<ImageCacherObject> _cache;

        private static readonly List<string> defaultTagBlacklist = new List<string>() {
            "loli",
            "lolicon",
            "shota"
        };

        public SearchImageCacher()
        {
            _http = new HttpClient();
            _rng = new Random();
            _cache = new SortedSet<ImageCacherObject>();
        }

        public async Task<ImageCacherObject> GetImage(string[] tags, bool forceExplicit, DapiSearchType type,
            HashSet<string> blacklistedTags = null)
        {
            tags = tags.Select(tag => tag?.ToLowerInvariant()).ToArray();

            blacklistedTags = blacklistedTags ?? new HashSet<string>();

            foreach (var item in defaultTagBlacklist)
            {
                blacklistedTags.Add(item);
            }

            blacklistedTags = blacklistedTags.Select(t => t.ToLowerInvariant()).ToHashSet();
            
            if (tags.Any(x => blacklistedTags.Contains(x)))
            {
                // todo localize blacklisted_tag (already exists)
                throw new Exception("One of the specified tags is blacklisted");
            }

            if (type == DapiSearchType.E621)
                tags = tags.Select(tag => tag?.Replace("yuri", "female/female", StringComparison.InvariantCulture))
                    .ToArray();

            var _lock = GetLock(type);
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                ImageCacherObject[] imgs;
                if (tags.Any())
                {
                    imgs = _cache.Where(x => x.Tags.IsSupersetOf(tags) && x.SearchType == type && (!forceExplicit || x.Rating == "e")).ToArray();
                }
                else
                {
                    imgs = _cache.Where(x => x.SearchType == type).ToArray();
                }
                imgs = imgs.Where(x => x.Tags.All(t => !blacklistedTags.Contains(t.ToLowerInvariant()))).ToArray();
                ImageCacherObject img;
                if (imgs.Length == 0)
                    img = null;
                else
                    img = imgs[_rng.Next(imgs.Length)];

                if (img != null)
                {
                    _cache.Remove(img);
                    return img;
                }
                else
                {
                    var images = await DownloadImagesAsync(tags, forceExplicit, type).ConfigureAwait(false);
                    images = images
                        .Where(x => x.Tags.All(t => !blacklistedTags.Contains(t.ToLowerInvariant())))
                        .ToArray();
                    if (images.Length == 0)
                        return null;
                    var toReturn = images[_rng.Next(images.Length)];
                    foreach (var dledImg in images)
                    {
                        if (dledImg != toReturn)
                            _cache.Add(dledImg);
                    }
                    return toReturn;
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        private SemaphoreSlim GetLock(DapiSearchType type)
        {
            return _locks.GetOrAdd(type, _ => new SemaphoreSlim(1, 1));
        }

        public async Task<ImageCacherObject[]> DownloadImagesAsync(string[] tags, bool isExplicit, DapiSearchType type)
        {
            var tag = "rating%3Aexplicit+";
            tag += string.Join('+', tags.Select(x => x.Replace(" ", "_", StringComparison.InvariantCulture).ToLowerInvariant()));
            if (isExplicit)
                tag = "rating%3Aexplicit+" + tag;
            var website = "";
            switch (type)
            {
                case DapiSearchType.Safebooru:
                    website = $"https://safebooru.org/index.php?page=dapi&s=post&q=index&limit=1000&tags={tag}";
                    break;
                case DapiSearchType.E621:
                    website = $"https://e621.net/post/index.json?limit=1000&tags={tag}";
                    break;
                case DapiSearchType.Danbooru:
                    website = $"http://danbooru.donmai.us/posts.json?limit=100&tags={tag}";
                    break;
                case DapiSearchType.Gelbooru:
                    website = $"http://gelbooru.com/index.php?page=dapi&s=post&q=index&limit=100&tags={tag}";
                    break;
                case DapiSearchType.Rule34:
                    website = $"https://rule34.xxx/index.php?page=dapi&s=post&q=index&limit=100&tags={tag}";
                    break;
                case DapiSearchType.Konachan:
                    website = $"https://konachan.com/post.json?s=post&q=index&limit=100&tags={tag}";
                    break;
                case DapiSearchType.Yandere:
                    website = $"https://yande.re/post.json?limit=100&tags={tag}";
                    break;
                case DapiSearchType.Derpibooru:
                    website = $"https://derpibooru.org/search.json?q={tag?.Replace('+', ',')}&perpage=49";
                    break;
            }

            try
            {
                if (type == DapiSearchType.Konachan || type == DapiSearchType.Yandere ||
                    type == DapiSearchType.E621 || type == DapiSearchType.Danbooru)
                {
                    var data = await _http.GetStringAsync(website).ConfigureAwait(false);
                    return JsonConvert.DeserializeObject<DapiImageObject[]>(data)
                        .Where(x => x.FileUrl != null)
                        .Select(x => new ImageCacherObject(x, type))
                        .ToArray();
                }

                if (type == DapiSearchType.Derpibooru)
                {
                    var data = await _http.GetStringAsync(website).ConfigureAwait(false);
                    return JsonConvert.DeserializeObject<DerpiContainer>(data)
                        .Search
                        .Where(x => !string.IsNullOrWhiteSpace(x.Image))
                        .Select(x => new ImageCacherObject("https:" + x.Image,
                            type, x.Tags, x.Score))
                        .ToArray();

                }

                return (await LoadXmlAsync(website, type).ConfigureAwait(false)).ToArray();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error downloading an image: {Message}", ex.Message);
                return Array.Empty<ImageCacherObject>();
            }
        }

        private async Task<ImageCacherObject[]> LoadXmlAsync(string website, DapiSearchType type)
        {
            var list = new List<ImageCacherObject>();
            using (var stream = await _http.GetStreamAsync(website).ConfigureAwait(false))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings()
            {
                Async = true,
            }))
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    if (reader.NodeType == XmlNodeType.Element &&
                        reader.Name == "post")
                    {
                        list.Add(new ImageCacherObject(new DapiImageObject()
                        {
                            FileUrl = reader["file_url"],
                            Tags = reader["tags"],
                            Rating = reader["rating"] ?? "e"

                        }, type));
                    }
                }
            }
            return list.ToArray();
        }

        public void Clear()
        {
            _cache.Clear();
        }
    }
}
