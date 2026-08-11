using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Rbx2Source.Assembler;
using Rbx2Source.Resources;

using RobloxFiles;

namespace Rbx2Source.Web
{
    public class ProductInfo
    {
        public string Name;
        public string WindowsSafeName;
        public AssetType AssetTypeId;
    }

    public class AssetContentRepresentationSpecifier
    {
        public string Format;
        public string MajorVersion;
        public string Fidelity;
    }

    public class AssetResponseItem
    {
        public Uri Location;
        public bool IsHashDynamic;
        public bool IsCopyrightProtected;
        public bool IsArchived;
        public AssetType AssetTypeId;
        public AssetContentRepresentationSpecifier ContentRepresentationSpecifier;
    }

    public class Asset
    {
        public long Id;

        public AssetType AssetType;
        public ProductInfo ProductInfo;
        public AssetResponseItem ResponseItem;

        public bool Loaded;
        public bool IsLocal;

        public Uri CdnUri;
        public string CdnCacheId;

        public byte[] Content;
        public bool ContentLoaded;

        private static readonly ConcurrentDictionary<long, Asset> assetCache = new ConcurrentDictionary<long, Asset>();
        private static readonly object assetCacheLock = new object();

        private static readonly HttpClient httpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            HttpClientHandler handler = new HttpClientHandler()
            {
                UseDefaultCredentials = true,
                Proxy = null,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            HttpClient client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Rbx2Source");
            return client;
        }

        public static AssetResponseItem GetResponseItem(long assetId, string apiKey)
        {
            Uri uri = new Uri("https://apis.roblox.com/asset-delivery-api/v1/assetId/" + assetId);
            AssetResponseItem responseItem;

            HttpWebRequest request = WebRequest.CreateHttp(uri);
            request.Headers.Add("x-api-key", apiKey);
            request.AllowAutoRedirect = false;
            request.UserAgent = "Rbx2Source";
            request.Method = "GET";

            using (var response = request.GetResponse() as HttpWebResponse)
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream))
            {
                string json = reader.ReadToEnd();
                responseItem = JsonConvert.DeserializeObject<AssetResponseItem>(json);
            }

            return responseItem;
        }

        public Instance OpenAsModel()
        {
            byte[] content = GetContent();
            return RobloxFile.Open(content);
        }

        public override int GetHashCode()
        {
            return Id.ToString().GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is Asset asset)
                return asset.Id == Id;

            return false;
        }

        public byte[] GetContent()
        {
            if (!ContentLoaded)
            {
                try
                {
                    Content = httpClient.GetByteArrayAsync(CdnUri).GetAwaiter().GetResult();
                    ContentLoaded = true;
                }
                catch
                {
                    Content = Array.Empty<byte>();
                    ContentLoaded = false;
                }
            }

            return Content;
        }

        private class AssetCacheData
        {
            public long Id;
            public AssetType AssetType;
            public ProductInfo ProductInfo;
            public AssetResponseItem ResponseItem;
            public bool Loaded;
            public bool IsLocal;
            public Uri CdnUri;
            public string CdnCacheId;
            public bool ContentLoaded;
        }

        private static void DeleteCachedFiles(string cachedFile, string cachedContentFile)
        {
            Rbx2Source.Print("Deleting corrupted file {0}", cachedFile);

            if (File.Exists(cachedFile))
            {
                try { File.Delete(cachedFile); }
                catch { }
            }

            if (File.Exists(cachedContentFile))
            {
                try { File.Delete(cachedContentFile); }
                catch { }
            }
        }

        private static Asset LoadFromCache(string cachedFile, string cachedContentFile, long assetId)
        {
            if (!File.Exists(cachedFile) || !File.Exists(cachedContentFile))
                return null;

            try
            {
                string serialized = File.ReadAllText(cachedFile);
                AssetCacheData data = JsonConvert.DeserializeObject<AssetCacheData>(serialized);

                if (data == null || data.Id != assetId)
                {
                    DeleteCachedFiles(cachedFile, cachedContentFile);
                    return null;
                }

                byte[] content = File.ReadAllBytes(cachedContentFile);

                if (content == null || content.Length == 0)
                {
                    DeleteCachedFiles(cachedFile, cachedContentFile);
                    return null;
                }

                Asset asset = new Asset()
                {
                    Id = assetId,
                    AssetType = data.AssetType,
                    ProductInfo = data.ProductInfo,
                    ResponseItem = data.ResponseItem,
                    Loaded = true,
                    IsLocal = data.IsLocal,
                    CdnUri = data.CdnUri,
                    CdnCacheId = data.CdnCacheId,
                    Content = content,
                    ContentLoaded = true
                };

                Rbx2Source.Print("Fetched pre-cached asset {0}", assetId);
                return asset;
            }
            catch
            {
                DeleteCachedFiles(cachedFile, cachedContentFile);
                return null;
            }
        }

        private static void SaveToCache(string cachedFile, string cachedContentFile, Asset asset)
        {
            try
            {
                AssetCacheData data = new AssetCacheData()
                {
                    Id = asset.Id,
                    AssetType = asset.AssetType,
                    ProductInfo = asset.ProductInfo,
                    ResponseItem = asset.ResponseItem,
                    Loaded = asset.Loaded,
                    IsLocal = asset.IsLocal,
                    CdnUri = asset.CdnUri,
                    CdnCacheId = asset.CdnCacheId,
                    ContentLoaded = asset.ContentLoaded
                };

                string serialized = JsonConvert.SerializeObject(data, Formatting.None);

                File.WriteAllBytes(cachedContentFile, asset.Content);
                File.WriteAllText(cachedFile, serialized);

                Rbx2Source.Print("Precached AssetId {0}", asset.Id);
            }
            catch
            {
                Rbx2Source.Print("Failed to cache AssetId {0}", asset.Id);
            }
        }

        public static Asset Get(long assetId)
        {
            if (!assetCache.TryGetValue(assetId, out Asset asset))
            {
                lock (assetCacheLock)
                {
                    if (!assetCache.TryGetValue(assetId, out asset))
                    {
                        string appData = Environment.GetEnvironmentVariable("LocalAppData");
                        string assetCacheDir = Path.Combine(appData, "Rbx2Source", "AssetCache");
                        Directory.CreateDirectory(assetCacheDir);

                        string cacheName = assetId.ToString(CultureInfo.InvariantCulture);
                        string cachedFile = Path.Combine(assetCacheDir, cacheName + ".json");
                        string cachedContentFile = Path.Combine(assetCacheDir, cacheName + ".bin");

                        asset = LoadFromCache(cachedFile, cachedContentFile, assetId);

                        if (asset == null)
                        {
                            string apiKey = Rbx2Source.GetApiKey();

                            if (apiKey == "")
                                throw new Exception("Invalid API key!");

                            AssetResponseItem responseItem = GetResponseItem(assetId, apiKey);
                            Uri location = responseItem.Location;

                            asset = new Asset()
                            {
                                Id = assetId,
                                ResponseItem = responseItem,
                            };

                            try
                            {
                                string productInfoJson = httpClient.GetStringAsync("https://economy.roblox.com/v2/assets/" + assetId + "/details").GetAwaiter().GetResult();
                                asset.ProductInfo = JsonConvert.DeserializeObject<ProductInfo>(productInfoJson);

                                asset.ProductInfo.WindowsSafeName = FileUtility.MakeNameWindowsSafe(asset.ProductInfo.Name);
                                asset.AssetType = asset.ProductInfo.AssetTypeId;
                            }
                            catch
                            {
                                string name = "unknown_" + asset.Id;

                                ProductInfo dummyInfo = new ProductInfo()
                                {
                                    Name = name,
                                    WindowsSafeName = name,
                                    AssetTypeId = AssetType.Model
                                };

                                asset.ProductInfo = dummyInfo;
                            }

                            var segments = location.Segments;
                            var identifier = segments.Length > 1 ? segments[1] : assetId.ToString(CultureInfo.InvariantCulture);

                            asset.CdnUri = location;
                            asset.CdnCacheId = identifier;

                            asset.GetContent();
                            asset.Loaded = true;

                            SaveToCache(cachedFile, cachedContentFile, asset);
                        }

                        assetCache[assetId] = asset;
                    }
                }
            }

            return asset;
        }

        public static Asset FromResource(string path)
        {
            byte[] embedded = ResourceUtility.GetResource(path);

            return new Asset()
            {
                Content = embedded,
                ContentLoaded = true,
                AssetType = AssetType.Model,
                IsLocal = true,
                Loaded = true,
                Id = 0
            };
        }

        public static Asset GetByAssetId(string address = "")
        {
            if (address == null || address.Length == 0)
                address = "rbxassetid://9854798";

            long legacyId = LegacyAssets.Check(address);

            if (legacyId > 0)
                return Get(legacyId);
           
            var match = Regex.Match(address.Trim(), @"\d+$");
            string sAssetId = match.Value;

            if (!long.TryParse(sAssetId, out long assetId))
                if (address == "rbxasset://textures/face.png")
                    return FromResource("Images/face.png");


            return Get(assetId);
        }
    }
}
