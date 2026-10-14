using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Pludibase
{
    [Serializable]
    internal sealed class AcquisitionSnapshot
    {
        public long revision;
        public string payload;
        public string checksum;
    }

    /// <summary>Recoverable atomic snapshot. Corrupt storage fails closed instead of creating a second install.</summary>
    internal sealed class AcquisitionStorage
    {
        private readonly string path;
        private long revision;

        internal AcquisitionStorage(string directory)
        {
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "installation.json");
        }

        internal AcquisitionState Load()
        {
            AcquisitionSnapshot newest = null;
            bool existed = false;
            foreach (string candidate in new[] { path, path + ".tmp", path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                existed = true;
                try
                {
                    var snapshot = JsonUtility.FromJson<AcquisitionSnapshot>(File.ReadAllText(candidate, Encoding.UTF8));
                    if (snapshot == null || snapshot.revision <= 0 || string.IsNullOrEmpty(snapshot.payload) ||
                        snapshot.checksum != Hash(snapshot.payload)) continue;
                    if (newest == null || snapshot.revision > newest.revision) newest = snapshot;
                }
                catch (ArgumentException) { }
                catch (IOException) { }
            }
            if (newest == null)
            {
                if (existed) throw new IOException("Acquisition installation storage is unreadable; restore the file before collecting.");
                return null;
            }
            revision = newest.revision;
            var state = JsonUtility.FromJson<AcquisitionState>(newest.payload);
            Guid parsed;
            DateTime timestamp;
            if (state == null || state.schemaVersion != 1 || !Guid.TryParse(state.installId, out parsed) ||
                !Guid.TryParse(state.firstOpenEventId, out parsed) || !IsHexSecret(state.installationSecret) ||
                !DateTime.TryParse(state.firstOpenAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out timestamp) || timestamp.Kind != DateTimeKind.Utc || state.queue == null)
                throw new IOException("Acquisition installation storage is invalid; no new identity has been generated.");
            return state;
        }

        internal void Save(AcquisitionState state)
        {
            string payload = JsonUtility.ToJson(state);
            var snapshot = new AcquisitionSnapshot { revision = ++revision, payload = payload, checksum = Hash(payload) };
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(snapshot));
            using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (!File.Exists(path))
            {
                File.Move(path + ".tmp", path);
                return;
            }
            try { File.Replace(path + ".tmp", path, path + ".bak"); }
            catch (PlatformNotSupportedException) { Rotate(); }
            // Some Unity Android runtimes expose Replace but report it as unsupported via IOException.
            catch (IOException) { if (File.Exists(path + ".tmp")) Rotate(); else throw; }
        }

        private void Rotate()
        {
            // Each rename is atomic. Load selects the newest checksummed main/tmp/backup after interruption.
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            if (File.Exists(path)) File.Move(path, path + ".bak");
            File.Move(path + ".tmp", path);
        }

        internal static string NewSecret()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            var text = new StringBuilder(64);
            foreach (byte value in bytes) text.Append(value.ToString("x2"));
            return text.ToString();
        }

        internal static string Hash(string value)
        {
            using (var sha = SHA256.Create())
            {
                var text = new StringBuilder(64);
                foreach (byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(value))) text.Append(b.ToString("x2"));
                return text.ToString();
            }
        }

        private static bool IsHexSecret(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            return true;
        }
    }
}
