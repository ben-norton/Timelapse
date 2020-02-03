﻿using Microsoft.Win32;
using System;
using Timelapse.Util;

namespace Timelapse.Editor.Util
{
    internal class EditorUserRegistrySettings : UserRegistrySettings
    {
        // same key as Timelapse uses; intentional as both Timelapse and template editor are released together
        public DateTime MostRecentCheckForUpdates { get; set; }

        public MostRecentlyUsedCollection<string> MostRecentTemplates { get; private set; }

        public bool ShowUtcOffset { get; set; }

        public EditorUserRegistrySettings()
            : this(Constant.WindowRegistryKeys.RootKey)
        {
        }

        internal EditorUserRegistrySettings(string keyPath)
            : base(keyPath)
        {
            this.ReadFromRegistry();
        }

        public void ReadFromRegistry()
        {
            using (RegistryKey registryKey = this.OpenRegistryKey())
            {
                this.MostRecentCheckForUpdates = registryKey.ReadDateTime(Constant.WindowRegistryKeys.MostRecentCheckForUpdates, DateTime.UtcNow);
                this.MostRecentTemplates = registryKey.ReadMostRecentlyUsedList(EditorConstant.Registry.EditorKey.MostRecentlyUsedTemplates);
                this.ShowUtcOffset = registryKey.ReadBoolean(EditorConstant.Registry.EditorKey.ShowUtcOffset, false);
            }
        }

        public void WriteToRegistry()
        {
            using (RegistryKey registryKey = this.OpenRegistryKey())
            {
                registryKey.Write(Constant.WindowRegistryKeys.MostRecentCheckForUpdates, this.MostRecentCheckForUpdates);
                registryKey.Write(EditorConstant.Registry.EditorKey.MostRecentlyUsedTemplates, this.MostRecentTemplates);
                registryKey.Write(EditorConstant.Registry.EditorKey.ShowUtcOffset, this.ShowUtcOffset);
            }
        }
    }
}
