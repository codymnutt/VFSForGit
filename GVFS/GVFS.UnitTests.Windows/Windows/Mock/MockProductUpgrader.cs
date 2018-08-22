﻿using GVFS.Common;
using GVFS.Common.Tracing;
using GVFS.UnitTests.Windows.Upgrader;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Windows.Mock.Upgrader
{
    public class MockProductUpgrader : ProductUpgrader
    {
        private string expectedGVFSAssetName;
        private string expectedGitAssetName;
        private ActionType failActionTypes;

        public MockProductUpgrader(
            string currentVersion, 
            CallSequenceTracker sequenceTrackerCallback, 
            ITracer tracer) : base(currentVersion, tracer)
        {
            this.DownloadedFiles = new List<string>();
            this.InstallerArgs = new Dictionary<string, Dictionary<string, string>>();
            this.SequenceTracker = sequenceTrackerCallback;
        }

        [Flags]
        public enum ActionType
        {
            Invalid = 0,
            FetchReleaseInfo = 0x1,
            CopyTools = 0x2,
            GitDownload = 0x4,
            GVFSDownload = 0x8,
            GitInstall = 0x10,
            GVFSInstall = 0x20
        }

        public RingType LocalRingConfig { get; set; }
        public List<string> DownloadedFiles { get; private set; }
        public Dictionary<string, Dictionary<string, string>> InstallerArgs { get; private set; }
        public CallSequenceTracker SequenceTracker { get; private set; }

        private Release FakeUpgradeRelease { get; set; }

        public void SetFailOnAction(ActionType failureType)
        {
            this.failActionTypes |= failureType;
        }

        public void SetSucceedOnAction(ActionType failureType)
        {
            this.failActionTypes &= ~failureType;
        }

        public void ResetFailedAction()
        {
            this.failActionTypes = ActionType.Invalid;
        }

        public void PretendNewReleaseAvailableAtRemote(string upgradeVersion, RingType remoteRing)
        {
            string assetDownloadURLPrefix = "https://github.com/Microsoft/VFSForGit/releases/download/v" + upgradeVersion;

            Release release = new Release();

            release.Name = "GVFS " + upgradeVersion;
            release.Tag = "v" + upgradeVersion;
            release.PreRelease = remoteRing == RingType.Fast;
            release.Assets = new List<Asset>();

            Random random = new Random();
            Asset gvfsAsset = new Asset();
            gvfsAsset.Name = "SetupGVFS." + upgradeVersion + ".exe";

            // This is not cross-checked anywhere, random value is good.
            gvfsAsset.Size = random.Next(int.MaxValue / 10, int.MaxValue / 2);
            gvfsAsset.DownloadURL = new Uri(assetDownloadURLPrefix + "/SetupGVFS." + upgradeVersion + ".exe");
            release.Assets.Add(gvfsAsset);

            Asset gitAsset = new Asset();
            gitAsset.Name = "Git-2.17.1.gvfs.2.1.4.g4385455-64-bit.exe";
            gitAsset.Size = random.Next(int.MaxValue / 10, int.MaxValue / 2);
            gitAsset.DownloadURL = new Uri(assetDownloadURLPrefix + "/Git-2.17.1.gvfs.2.1.4.g4385455-64-bit.exe");
            release.Assets.Add(gitAsset);

            this.expectedGVFSAssetName = gvfsAsset.Name;
            this.expectedGitAssetName = gitAsset.Name;
            this.FakeUpgradeRelease = release;
        }

        public override bool TryCreateToolsDirectory(out string upgraderToolPath, out string error)
        {
            this.SequenceTracker.RecordMethod("CopyTools");

            if (this.failActionTypes.HasFlag(ActionType.CopyTools))
            {
                upgraderToolPath = null;
                error = "Unable to copy upgrader tools";
                return false;
            }

            upgraderToolPath = @"C:\ProgramData\GVFS\GVFS.Upgrade\Tools\GVFS.Upgrader.exe";
            error = null;
            return true;
        }

        protected override bool TryLoadRingConfig(out string error)
        {
            this.SequenceTracker.RecordMethod("LoadRingConfig");

            this.Ring = this.LocalRingConfig;

            if (this.LocalRingConfig == RingType.Invalid)
            {
                error = "Invalid upgrade ring type(Invalid) specified in Git config.";
                return false;
            }

            error = null;
            return true;
        }

        protected override bool TryDownloadAsset(Asset asset, out string errorMessage)
        {
            bool validAsset = true;
            if (this.expectedGVFSAssetName.Equals(asset.Name, StringComparison.OrdinalIgnoreCase))
            {
                this.SequenceTracker.RecordMethod("DownloadAsset_GVFS");

                if (this.failActionTypes.HasFlag(ActionType.GVFSDownload))
                {
                    errorMessage = "Error downloading GVFS from GitHub";
                    return false;
                }
            }
            else if (this.expectedGitAssetName.Equals(asset.Name, StringComparison.OrdinalIgnoreCase))
            {
                this.SequenceTracker.RecordMethod("DownloadAsset_Git");

                if (this.failActionTypes.HasFlag(ActionType.GitDownload))
                {
                    errorMessage = "Error downloading Git from GitHub";
                    return false;
                }
            }
            else
            {
                validAsset = false;
            }
            
            if (validAsset)
            {
                string fakeDownloadDirectory = @"C:\ProgramData\GVFS\GVFS.Upgrade\Downloads";
                asset.LocalPath = Path.Combine(fakeDownloadDirectory, asset.Name);
                this.DownloadedFiles.Add(asset.LocalPath);

                errorMessage = null;
                return true;
            }

            errorMessage = "Cannot download unknown asset.";
            return false;
        }

        protected override bool TryFetchReleases(out List<Release> releases, out string errorMessage)
        {
            this.SequenceTracker.RecordMethod("FetchReleaseInfo");

            if (this.failActionTypes.HasFlag(ActionType.FetchReleaseInfo))
            {
                releases = null;
                errorMessage = "Error fetching upgrade release info.";
                return false;
            }

            releases = new List<Release> { this.FakeUpgradeRelease };
            errorMessage = null;

            return true;
        }

        protected override void RunInstaller(string path, string args, out int exitCode, out string error)
        {
            string fileName = Path.GetFileName(path);
            Dictionary<string, string> installationInfo = new Dictionary<string, string>();
            installationInfo.Add("Installer", fileName);
            installationInfo.Add("Args", args);

            exitCode = 0;
            error = null;

            if (fileName.Equals(this.expectedGitAssetName, StringComparison.OrdinalIgnoreCase))
            {
                this.SequenceTracker.RecordMethod("InstallAsset_Git");
                this.InstallerArgs.Add("Git", installationInfo);
                if (this.failActionTypes.HasFlag(ActionType.GitInstall))
                {
                    exitCode = -1;
                    error = "Git installation failed";
                }

                return;
            }

            if (fileName.Equals(this.expectedGVFSAssetName, StringComparison.OrdinalIgnoreCase))
            {
                this.SequenceTracker.RecordMethod("InstallAsset_GVFS");
                this.InstallerArgs.Add("GVFS", installationInfo);
                if (this.failActionTypes.HasFlag(ActionType.GVFSInstall))
                {
                    exitCode = -1;
                    error = "GVFS installation failed";
                }

                return;
            }

            exitCode = -1;
            error = "Cannot launch unknown installer";
            return;
        }
    }
}