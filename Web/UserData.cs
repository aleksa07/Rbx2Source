#pragma warning disable 0649

using System;
using System.Collections.Generic;
using System.IO;
using RobloxFiles.DataTypes;

using Newtonsoft.Json;

namespace Rbx2Source.Web
{
    public enum AvatarType { R6, R15, Unknown }

    public struct AvatarScale
    {
        public float Width;
        public float Height;
        public float Head;
        public float Depth;

        public float Proportion;
        public float BodyType;
    }

    public class UserInfo
    {
        public long Id;
        public string Name;
        public string DisplayName;
        public bool HasVerifiedBadge;
        public List<WebApiError> Errors;
    }
    
    public struct UserInfos
    {
        public UserInfo[] Data;
    }

    public struct MultiGetByUsernameRequest
    {
        public string[] Usernames;
        public bool ExcludeBannedUsers;

        public MultiGetByUsernameRequest(bool excludeBannedUsers, params string[] usernames)
        {
            ExcludeBannedUsers = excludeBannedUsers;
            Usernames = usernames;
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public struct AvatarBodyColors
    {
        public string HeadColor3;
        public string LeftArmColor3;
        public string RightArmColor3;
        public string LeftLegColor3;
        public string RightLegColor3;
        public string TorsoColor3;
    }

    public struct AssetVector3
    {
        public float X;
        public float Y;
        public float Z;

        public AssetVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static implicit operator Vector3(AssetVector3 vec) => new Vector3(vec.X, vec.Y, vec.Z);
        public static implicit operator AssetVector3(Vector3 vec) => new AssetVector3(vec.X, vec.Y, vec.Z);
    }

    public struct WebAssetType
    {
        public AssetType Id;
        public string Name => Enum.GetName(typeof(AssetType), Id);

        public static implicit operator AssetType(WebAssetType assetType) => assetType.Id;
        public static implicit operator WebAssetType(AssetType id) => new WebAssetType() { Id = id };
    }

    public struct AssetMeta
    {
        public int Version;
        public int? Order;
        public float? Puffiness;

        public AssetVector3? Position;
        public AssetVector3? Rotation;
        public AssetVector3? Scale;
    }

    public class AssetInfo
    {
        public long Id;
        public string Name;

        public WebAssetType AssetType;

        public AssetMeta? Meta;
    }

    public class ThumbnailConfig
    {
        public int ThumbnailId = 3;
        public string ThumbnailType = "3d";
        public string Size = "420x420";
    }
    
    public class RenderAvatarRequest
    {
        public UserAvatar AvatarDefinition;
        public ThumbnailConfig ThumbnailConfig = new ThumbnailConfig();

        public RenderAvatarRequest(UserAvatar avatar)
        {
            AvatarDefinition = avatar;
        }
    }

    public class UserAvatar
    {
        public bool UserExists;
        public UserInfo UserInfo;

        public AvatarScale Scales;
        public AvatarType PlayerAvatarType;

        public AvatarBodyColors BodyColor3s;
        public AssetInfo[] Assets;

        private static UserAvatar CreateUserAvatar(UserInfo info)
        {
            var avatar = WebUtil.DownloadJSON<UserAvatar>($"https://avatar.roblox.com/v2/avatar/users/{info.Id}/avatar", maxRetriesOn429: 3);
            avatar.UserExists = true;
            avatar.UserInfo = info;

            return avatar;
        }

        public static UserAvatar FromUserId(long userId)
        {
            var info = WebUtil.DownloadJSON<UserInfo>($"https://users.roblox.com/v1/users/{userId}", maxRetriesOn429: 3);
            return CreateUserAvatar(info);
        }

        public static UserAvatar FromUsername(string userName)
        {
            var request = new MultiGetByUsernameRequest(false, userName);
            var requestBody = request.ToString();

            var userInfos = WebUtil.DownloadJSON<UserInfos>("https://users.roblox.com/v1/usernames/users", "POST", requestBody, 3);

            if (userInfos.Data == null || userInfos.Data.Length == 0)
                return new UserAvatar() { UserExists = false };

            var userInfo = userInfos.Data[0];
            return CreateUserAvatar(userInfo);
        }

        public static UserAvatar FromOutfitId(long outfitId)
        {
            OutfitDetails outfit = LoadOutfitFromCache(outfitId);

            if (outfit == null)
            {
                outfit = WebUtil.DownloadJSON<OutfitDetails>($"https://avatar.roblox.com/v1/outfits/{outfitId}/details", maxRetriesOn429: 3);

                if (!string.IsNullOrWhiteSpace(outfit.Name))
                    SaveOutfitToCache(outfitId, outfit);
            }

            if (string.IsNullOrWhiteSpace(outfit.Name))
                return new UserAvatar() { UserExists = false };

            var info = new UserInfo
            {
                Id = outfitId,
                Name = outfit.Name,
                DisplayName = outfit.Name,
            };

            var bodyColors = outfit.BodyColors;
            if (string.IsNullOrWhiteSpace(bodyColors.HeadColor3))
                bodyColors.HeadColor3 = "F2F2F2";
            if (string.IsNullOrWhiteSpace(bodyColors.TorsoColor3))
                bodyColors.TorsoColor3 = "F2F2F2";
            if (string.IsNullOrWhiteSpace(bodyColors.LeftArmColor3))
                bodyColors.LeftArmColor3 = "F2F2F2";
            if (string.IsNullOrWhiteSpace(bodyColors.RightArmColor3))
                bodyColors.RightArmColor3 = "F2F2F2";
            if (string.IsNullOrWhiteSpace(bodyColors.LeftLegColor3))
                bodyColors.LeftLegColor3 = "C3C3C3";
            if (string.IsNullOrWhiteSpace(bodyColors.RightLegColor3))
                bodyColors.RightLegColor3 = "C3C3C3";

            var avatar = new UserAvatar
            {
                UserExists = true,
                UserInfo = info,
                PlayerAvatarType = outfit.PlayerAvatarType,
                BodyColor3s = bodyColors,
                Assets = outfit.Assets,
                Scales = new AvatarScale
                {
                    Width = 1,
                    Height = 1,
                    Head = 1,
                    Depth = 1,
                    Proportion = 0,
                    BodyType = 0,
                },
            };

            return avatar;
        }

        private static string GetOutfitCachePath(long outfitId)
        {
            string appData = Environment.GetEnvironmentVariable("LocalAppData");
            string cacheDir = Path.Combine(appData, "Rbx2Source", "OutfitCache");
            return Path.Combine(cacheDir, outfitId.ToInvariantString() + ".json");
        }

        private static OutfitDetails LoadOutfitFromCache(long outfitId)
        {
            try
            {
                string path = GetOutfitCachePath(outfitId);

                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    OutfitDetails outfit = JsonConvert.DeserializeObject<OutfitDetails>(json);

                    if (outfit != null && !string.IsNullOrWhiteSpace(outfit.Name) && outfit.Assets != null)
                        return outfit;

                    File.Delete(path);
                }
            }
            catch
            {
                // Corrupted cache? Fall back to a fresh fetch.
            }

            return null;
        }

        private static void SaveOutfitToCache(long outfitId, OutfitDetails outfit)
        {
            try
            {
                string path = GetOutfitCachePath(outfitId);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(outfit, Formatting.None));
            }
            catch
            {
                // Oh well.
            }
        }
    }

    public class OutfitDetails
    {
        public long Id;
        public string Name;
        public AssetInfo[] Assets;
        public AvatarBodyColors BodyColors;
        public AvatarType PlayerAvatarType;
    }
}
