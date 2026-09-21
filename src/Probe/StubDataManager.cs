// A hollow IDataManager, so the probe can exercise the paths that look the library up.
//
// Only GetAllEmulators answers: it is enough for AddSaveFile, whose job is to find the memory stick
// and which falls back to "any PPSSPP the library knows" when the save's own game cannot be
// resolved. Everything else returns null or an empty array rather than throwing - a plugin that
// probes the data manager for something we do not model should degrade, not blow up the harness.

using System;
using System.Collections.Generic;
using Unbroken.LaunchBox.Plugins.Data;

namespace LbIntegrations.Probe
{
    internal sealed class StubDataManager : IDataManager
    {
        private readonly IEmulator[] _emulators;

        public StubDataManager(params IEmulator[] emulators) => _emulators = emulators ?? Array.Empty<IEmulator>();

        public IEmulator[] GetAllEmulators() => _emulators;

        public IEmulator GetEmulatorById(string id)
        {
            foreach (var e in _emulators)
                if (string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        public IGame GetGameById(string id) => null;

        public IGame[] GetAllGames() => Array.Empty<IGame>();
        public IParent[] GetAllParents() => Array.Empty<IParent>();
        public IPlatform[] GetAllPlatforms() => Array.Empty<IPlatform>();
        public IPlatformCategory[] GetAllPlatformCategories() => Array.Empty<IPlatformCategory>();
        public IPlaylist[] GetAllPlaylists() => Array.Empty<IPlaylist>();
        public IList<IGameController> GetGameControllers() => new List<IGameController>();
        public IList<IPlatform> GetRootPlatformsCategoriesPlaylists() => new List<IPlatform>();

        public IPlatform GetPlatformByName(string name) => null;
        public IPlatformCategory GetPlatformCategoryByName(string name) => null;
        public IPlaylist GetPlaylistById(string id) => null;

        public string AddGameController(string controllerName, string controllerCategory, string[] associatedPlatforms) => null;
        public IEmulator AddNewEmulator() => null;
        public IGame AddNewGame(string title) => null;
        public IPlatform AddNewPlatform(string name) => null;
        public IPlatformCategory AddNewPlatformCategory(string name) => null;
        public IPlaylist AddNewPlaylist(string name) => null;

        public bool TryRemoveEmulator(IEmulator emulator) => false;
        public bool TryRemoveGame(IGame game) => false;
        public bool TryRemovePlatform(IPlatform platform) => false;
        public bool TryRemovePlatformCategory(IPlatformCategory platformCategory) => false;
        public bool TryRemovePlaylist(IPlaylist playlist) => false;

        public void BackgroundReloadSave(Action changes) { }
        public void ForceReload() { }
        public void ReloadIfNeeded() { }
        public void Save(bool wait) { }
    }
}
