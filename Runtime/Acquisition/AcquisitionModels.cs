using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pludibase
{
    /// <summary>Initialize this module on Unity's main thread before downloads or player identification.</summary>
    public sealed class AcquisitionOptions
    {
        public string ApiUrl;
        public string AccessKey;
        // Stable, non-secret name for this game's local installation storage. Do not use the access key.
        public string StorageNamespace = "default";
        public bool DevelopmentBuild = Debug.isDebugBuild;
        public string AppVersion;
        public string BuildVersion = "";
        /// <summary>Optional configured test/target country (ISO two letters); this does not detect location.</summary>
        public string Country = "";
        public int RequestTimeoutSeconds = 10;
        public double DurationCheckpointSeconds = 15;
        /// <summary>Requires explicitly installing the sample Java bridge and Google dependency.</summary>
        public bool CollectGooglePlayReferrer;
        /// <summary>
        /// Called on the main thread for every link attempt. Return fresh x-talo-signature/session/player headers
        /// from the game's authenticated Talo integration; return null while that context is unavailable.
        /// Neither this delegate nor returned credential headers are persisted.
        /// </summary>
        public Func<AcquisitionRequestContext, IDictionary<string, string>> LinkHeadersProvider;
    }

    /// <summary>The exact outgoing link request. Body includes the installation proof; do not log or persist it.</summary>
    public sealed class AcquisitionRequestContext
    {
        public string Method { get; private set; }
        public string Url { get; private set; }
        public string Path { get; private set; }
        public string Body { get; private set; }
        public string AliasId { get; private set; }

        internal AcquisitionRequestContext(string method, string url, string path, string body, string aliasId)
        {
            Method = method; Url = url; Path = path; Body = body; AliasId = aliasId;
        }
    }

    [Serializable]
    public sealed class GooglePlayReferrerResult
    {
        public string nonce;
        public string status;
        public int responseCode;
        public string rawReferrer;
        public long referrerClickTimestampSeconds;
        public long installBeginTimestampSeconds;
        public bool googlePlayInstant;
        public string reason;
    }

    public sealed class AcquisitionDiagnostics
    {
        public int PendingCount { get; internal set; }
        public int RejectedCount { get; internal set; }
        public bool FirstOpenAcknowledged { get; internal set; }
        public long LastHttpStatus { get; internal set; }
        public string LastError { get; internal set; }
        public string GooglePlayReferrerStatus { get; internal set; }
        public string GooglePlayReferrerReason { get; internal set; }
        public string AttributionStatus { get; internal set; }
        public string AttributionReason { get; internal set; }
        public string MetaReferrerStatus { get { return "unavailable"; } }
        public string MetaReferrerReason { get { return "Meta collection adapter and decryption credentials have not been verified."; } }
    }

    [Serializable]
    internal sealed class AcquisitionProp
    {
        public string key;
        public string value;
    }

    [Serializable]
    internal sealed class AcquisitionRecord
    {
        public string kind;
        public string eventId;
        public string occurredAt;
        public string name;
        public List<AcquisitionProp> props = new List<AcquisitionProp>();
        public string source;
        public string rawReferrer;
        public string referrerClickAt;
        public string installBeginAt;
        public string aliasId;
        public int attempts;
        public string retryAt;
        public bool rejected;
        public long responseCode;
    }

    [Serializable]
    internal sealed class AcquisitionState
    {
        public int schemaVersion = 1;
        public string apiUrl;
        public bool developmentBuild;
        public string installId;
        public string installationSecret;
        public string firstOpenEventId;
        public string firstOpenAt;
        public string platform;
        public string appVersion;
        public string buildVersion;
        public string country;
        public bool firstOpenAcknowledged;
        public int firstOpenAttempts;
        public string firstOpenRetryAt;
        public bool googleReferrerCollected;
        public GooglePlayReferrerResult googleReferrer;
        public string attributionStatus;
        public string attributionReason;
        public List<AcquisitionRecord> queue = new List<AcquisitionRecord>();
    }
}
