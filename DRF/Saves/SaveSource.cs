using System;
using System.Collections.Generic;
using System.Reflection;
using DV.Common;
using DV.UserManagement;
using DV.UserManagement.Data;
using DV.UserManagement.Integration;
using DV.UserManagement.Storage;
using DV.UserManagement.Util;
using DV.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DRF.Saves;

internal sealed class SaveSource
{
    internal string Key { get; }

    internal string Name { get; }

    internal DateTime Timestamp { get; }

    internal bool IsBackup { get; }

    private readonly ISaveGame? _snapshot;
    private readonly GameSession? _session;
    private readonly string? _backupPath;

    private SaveSource(string key, string name, DateTime timestamp, bool isBackup,
        ISaveGame? snapshot, GameSession? session, string? backupPath)
    {
        Key = key;
        Name = name;
        Timestamp = timestamp;
        IsBackup = isBackup;
        _snapshot = snapshot;
        _session = session;
        _backupPath = backupPath;
    }

    internal static SaveSource ForSnapshot(ISaveGame save) => new(
        key: $"save:{save.ParentSession.SessionID}:{save.UID}",
        name: $"{save.Name} ({save.Type})",
        timestamp: save.Timestamp.LocalDateTime,
        isBackup: false,
        snapshot: save,
        session: save.ParentSession as GameSession,
        backupPath: null);

    internal static SaveSource ForBackup(GameSession session, string path, string fileName, DateTime written) => new(
        key: $"bak:{session.SessionID}:{fileName}",
        name: BackupName(fileName),
        timestamp: written,
        isBackup: true,
        snapshot: null,
        session: session,
        backupPath: path);

    // The game's backup prefixes say what made the copy, which is worth surfacing
    private static string BackupName(string fileName)
    {
        if (fileName.StartsWith("lastLoaded_")) return "Backup: as last loaded";
        if (fileName.StartsWith("previous_")) return "Backup: previous save";
        if (fileName.StartsWith("zeroMoney_before_")) return "Backup: before money loss";
        if (fileName.StartsWith("zeroMoney_after_")) return "Backup: after money loss";
        return "Backup: " + fileName;
    }

    internal string Describe() =>
        $"{Name} — {Timestamp:yyyy-MM-dd HH:mm}";

    // Reads the save off disk. Returns null and sets reason on anything that goes wrong, since a save that
    // can't be read is a legitimate outcome here (a backup from an incompatible build, a half written file)
    // rather than something worth exploding over.
    internal SaveGameData? Read(out string? reason)
    {
        reason = null;
        try
        {
            if (_snapshot != null)
            {
                _snapshot.LoadData();
                return SaveGameData.LoadFromJson(_snapshot.Data, _snapshot.CustomChunkData);
            }
            return ReadBackup(out reason);
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return null;
        }
    }

    private SaveGameData? ReadBackup(out string? reason)
    {
        reason = null;
        var manager = SingletonBehaviour<UserManager>.Instance;
        var user = manager != null ? manager.CurrentUser : null;
        if (manager == null || user == null || _backupPath == null)
        {
            reason = "no user profile is loaded";
            return null;
        }

        var storage = manager.Storage;
        if (!storage.FileExists(_backupPath))
        {
            reason = "the file is gone";
            return null;
        }

        var pak = new Paky(storage.OpenFileForReading(_backupPath), SaveGameSnapshot.SAVE_PACK_MAGIC,
            SaveGameSnapshot.SAVE_PACK_VERSION);
        try
        {
            var payload = pak.ReadFirst(SaveGameSnapshot.CHUNK_MAINDATA);
            if (payload == null)
            {
                reason = "it holds no save data";
                return null;
            }

            var key = EncryptionKey(user);
            if (key != null) payload = storage.DecryptData(payload, key);

            var json = JsonConvert.DeserializeObject<JObject>(
                UserManager.ENCODING.GetString(payload), UserManager.JSON_SERIALIZER_SETTINGS);
            if (json == null)
            {
                reason = "it could not be parsed";
                return null;
            }

            // A backup is a byte copy of the save file, so it has never been through the game's own save
            // upgraders. Run them here the same way loading it normally would, or a backup from an older
            // build would be read with the wrong shape.
            json = Upgrade(json, manager, storage);
            return SaveGameData.LoadFromJson(json);
        }
        finally
        {
            pak.Close();
        }
    }

    private JObject Upgrade(JObject json, UserManager manager, IStorageProvider storage)
    {
        if (typeof(UserManager)
            .GetField("saveDataUpgraders", BindingFlags.NonPublic | BindingFlags.Instance)?
            .GetValue(manager) is not ASaveSnapshotUpgrader[] upgraders || _backupPath == null) return json;

        List<(int, byte[])>? noCustomChunks = null;
        return json.Upgrade(manager, _backupPath, noCustomChunks, storage, _session, upgraders);
    }

    private static byte[]? EncryptionKey(User user) =>
        typeof(User).GetProperty("EncryptionKey", BindingFlags.NonPublic | BindingFlags.Instance)?
            .GetValue(user) as byte[];
}
