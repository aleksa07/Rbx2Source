using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace Rbx2Source.Resources
{
    static class Settings
    {
        private static readonly Dictionary<string, object> cache;
        private static readonly HashSet<string> dirtyKeys;
        private static readonly RegistryKey rbx2Source;

        public static object GetSetting(string key)
        {
            if (!cache.ContainsKey(key))
                cache[key] = rbx2Source.GetValue(key);

            return cache[key];
        }

        public static string GetString(string key)
        {
            object value = GetSetting(key);
            string result = "";

            if (value != null)
                result = value.ToInvariantString();

            return result;
        }

        public static void Save()
        {
            if (dirtyKeys.Count == 0)
                return;

            foreach (string key in dirtyKeys)
            {
                object value = cache[key];

                if (value != null)
                {
                    rbx2Source.SetValue(key, value);
                }
            }

            dirtyKeys.Clear();
        }

        public static void SetSetting(string key, object value)
        {
            object existing;

            if (cache.TryGetValue(key, out existing) && Equals(existing, value))
                return;

            cache[key] = value;
            dirtyKeys.Add(key);
        }

        public static void SaveSetting(string key, object value)
        {
            SetSetting(key, value);
            Save();
        }
        
        private static RegistryKey Open(RegistryKey current, string target)
        {
            return current.CreateSubKey(target, RegistryKeyPermissionCheck.ReadWriteSubTree);
        }


        static Settings()
        {
            RegistryKey currentUser = Registry.CurrentUser;
            RegistryKey software = Open(currentUser, "SOFTWARE");

            rbx2Source = Open(software, "Rbx2Source");
            cache = new Dictionary<string, object>();
            dirtyKeys = new HashSet<string>();

            foreach (string key in rbx2Source.GetValueNames())
                SetSetting(key, rbx2Source.GetValue(key));

            if (GetSetting("InitializedV3") == null)
            {
                SetSetting("Username", "Maximum_ADHD");
                SetSetting("AssetId", "19027209");
                SetSetting("CompilerType", "Avatar");
                SetSetting("InitializedV3", true);
                SetSetting("ApiKey", "");
                SetSetting("CurrentVersion", "2.11.0");
                SetSetting("ForceAvatarType", "Default");
                SetSetting("BodyPackage", "Default");
                SetSetting("TorsoType", "Normal");
                SetSetting("HeadMode", "Default");
            }

            software.Dispose();
        }
    }
}