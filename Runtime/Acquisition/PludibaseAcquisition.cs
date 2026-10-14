using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Pludibase
{
    /// <summary>Installation analytics, independent of Talo identification and its event queue. Main-thread API.</summary>
    public static class PludibaseAcquisition
    {
        internal static AcquisitionBehaviour Host;
        private static int mainThread;

        public static bool InitReady { get { return Host != null && Host.Ready; } }
        public static string InstallId { get { return Required().InstallId; } }
        public static AcquisitionDiagnostics Diagnostics
        {
            get
            {
                if (Host == null || Thread.CurrentThread.ManagedThreadId != mainThread)
                    throw new InvalidOperationException("Acquisition diagnostics require an initialized module on Unity's main thread.");
                return Host.GetDiagnostics();
            }
        }

        /// <summary>Returns after first-open identity and initial app_open are durably stored, without waiting for HTTP.</summary>
        public static void Initialize(AcquisitionOptions options)
        {
            if (mainThread != 0 && mainThread != Thread.CurrentThread.ManagedThreadId)
                throw new InvalidOperationException("Acquisition APIs must be called on Unity's main thread.");
            if (options == null) throw new ArgumentNullException("options");
            mainThread = Thread.CurrentThread.ManagedThreadId;
            if (Host == null) Host = UnityEngine.Object.FindObjectOfType<AcquisitionBehaviour>();
            if (Host != null)
            {
                Host.CheckConfiguration(options);
                return;
            }
            var owner = new GameObject("PludibaseAcquisition-" + Guid.NewGuid().ToString("N"));
            UnityEngine.Object.DontDestroyOnLoad(owner);
            var host = owner.AddComponent<AcquisitionBehaviour>();
            try
            {
                host.Initialize(options);
                Host = host;
            }
            catch
            {
                UnityEngine.Object.Destroy(owner);
                throw;
            }
        }

        /// <summary>Persists an event immediately. Returned ID and timestamp remain unchanged during retries.</summary>
        public static string Track(string name, IDictionary<string, string> props = null)
        {
            return Required().Track(name, props, DateTime.UtcNow);
        }

        /// <summary>Begin/end only actual gameplay; loading screens, menus and background time are excluded.</summary>
        public static void SetGameplayActive(bool active) { Required().SetGameplayActive(active); }

        /// <summary>Pass the alias ID only after the player SDK has successfully identified that player.</summary>
        public static void LinkIdentifiedPlayer(string aliasId) { Required().Link(aliasId); }

        /// <summary>Updates the in-memory link authentication adapter. No returned credentials are stored on disk.</summary>
        public static void SetLinkHeadersProvider(Func<AcquisitionRequestContext, IDictionary<string, string>> provider)
        {
            Required().SetLinkHeadersProvider(provider);
        }

        /// <summary>Forwards raw provider evidence; this call does not prove or resolve an advertising ID.</summary>
        public static string SubmitAttribution(string source, string rawReferrer, DateTime? referrerClickAt = null, DateTime? installBeginAt = null)
        {
            return Required().SubmitAttribution(source, rawReferrer, referrerClickAt, installBeginAt);
        }

        /// <summary>Checkpoint activity and make queued retries eligible now. HTTP acknowledgement is asynchronous.</summary>
        public static void Flush() { Required().Flush(); }

        /// <summary>Requeue preserved non-transient HTTP failures after fixing the configuration or payload.</summary>
        public static void RetryRejected() { Required().RetryRejected(); }

        private static AcquisitionBehaviour Required()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread)
                throw new InvalidOperationException("Acquisition APIs must be called on Unity's main thread.");
            if (!InitReady) throw new InvalidOperationException("Call PludibaseAcquisition.Initialize before collecting installation events.");
            return Host;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Host = null; mainThread = 0; }
    }
}
