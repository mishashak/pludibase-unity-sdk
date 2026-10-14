using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Pludibase
{
    internal sealed class AcquisitionBehaviour : MonoBehaviour
    {
        internal const int MaxEventBatchBytes = 512 * 1024;
        private AcquisitionOptions options;
        private AcquisitionState state;
        private AcquisitionStorage storage;
        private bool storageFault;
        private bool paused;
        private bool focused;
        private bool foreground;
        private bool gameplay;
        private long lastTick;
        private double checkpointElapsed;
        private AcquisitionDuration foregroundDuration;
        private AcquisitionDuration gameplayDuration;
        private UnityWebRequest activeRequest;
        private long lastHttpStatus;
        private string lastError = "";
        private string referrerNonce;
        private double referrerDeadline = 0;
        private double nextReferrerAttempt;
        private int referrerAttempts;

        internal bool Ready { get { return state != null && !storageFault; } }
        internal string InstallId { get { return state.installId; } }
        private static double Monotonic { get { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; } }

        internal void Initialize(AcquisitionOptions supplied)
        {
            options = CopyAndValidate(supplied);
            string folder = AcquisitionStorage.Hash(options.StorageNamespace + (options.DevelopmentBuild ? ":dev" : ":live")).Substring(0, 24);
            storage = new AcquisitionStorage(Path.Combine(Application.persistentDataPath, "pludibase-acquisition", folder));
            state = storage.Load();
            if (state == null)
            {
                state = new AcquisitionState
                {
                    apiUrl = options.ApiUrl, developmentBuild = options.DevelopmentBuild,
                    installId = Guid.NewGuid().ToString(), installationSecret = AcquisitionStorage.NewSecret(),
                    firstOpenEventId = Guid.NewGuid().ToString(), firstOpenAt = AcquisitionJson.Utc(DateTime.UtcNow),
                    platform = Platform(), appVersion = options.AppVersion, buildVersion = options.BuildVersion, country = options.Country
                };
                Persist();
            }
            if (state.apiUrl != options.ApiUrl || state.developmentBuild != options.DevelopmentBuild)
                throw new InvalidOperationException("Acquisition storage belongs to a different API/environment. Use a different StorageNamespace for a different backend.");
            focused = Application.isFocused;
            lastTick = Stopwatch.GetTimestamp();
            SetForeground(!paused && focused);
            if (!options.CollectGooglePlayReferrer && state.googleReferrer == null)
            {
                state.googleReferrer = new GooglePlayReferrerResult { status = "unavailable", reason = "Google Play Referrer collection is not enabled.", responseCode = -1 };
                Persist();
            }
            StartCoroutine(Drain());
        }

        internal void CheckConfiguration(AcquisitionOptions supplied)
        {
            var candidate = CopyAndValidate(supplied);
            if (candidate.ApiUrl != options.ApiUrl || candidate.AccessKey != options.AccessKey ||
                candidate.DevelopmentBuild != options.DevelopmentBuild || candidate.StorageNamespace != options.StorageNamespace ||
                candidate.CollectGooglePlayReferrer != options.CollectGooglePlayReferrer || candidate.Country != options.Country ||
                candidate.AppVersion != options.AppVersion || candidate.BuildVersion != options.BuildVersion ||
                candidate.RequestTimeoutSeconds != options.RequestTimeoutSeconds || candidate.DurationCheckpointSeconds != options.DurationCheckpointSeconds ||
                !Equals(candidate.LinkHeadersProvider, options.LinkHeadersProvider))
                throw new InvalidOperationException("Acquisition is already initialized with different settings.");
            if (!Ready) throw new IOException("Acquisition storage failed; collection has stopped until the storage is repaired and the app restarts.");
        }

        private static AcquisitionOptions CopyAndValidate(AcquisitionOptions supplied)
        {
            Uri url;
            if (string.IsNullOrWhiteSpace(supplied.ApiUrl) || !Uri.TryCreate(supplied.ApiUrl.TrimEnd('/'), UriKind.Absolute, out url) ||
                (url.Scheme != "https" && url.Scheme != "http") || !string.IsNullOrEmpty(url.UserInfo) ||
                !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
                throw new ArgumentException("ApiUrl must be an HTTP(S) base URL without credentials, query or fragment.");
            if (string.IsNullOrWhiteSpace(supplied.AccessKey) || supplied.AccessKey.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("A game access key with write:events scope is required.");
            if (string.IsNullOrWhiteSpace(supplied.StorageNamespace)) throw new ArgumentException("StorageNamespace must be a stable non-secret game name.");
            string country = (supplied.Country ?? "").ToUpperInvariant();
            if (country.Length != 0 && (country.Length != 2 || country[0] < 'A' || country[0] > 'Z' || country[1] < 'A' || country[1] > 'Z'))
                throw new ArgumentException("Country must be empty or two ASCII letters; no location detection is performed.");
            if (double.IsNaN(supplied.DurationCheckpointSeconds) || supplied.DurationCheckpointSeconds < 1 || supplied.DurationCheckpointSeconds > 60)
                throw new ArgumentException("DurationCheckpointSeconds must be between 1 and 60.");
            return new AcquisitionOptions
            {
                ApiUrl = supplied.ApiUrl.Trim().TrimEnd('/'), AccessKey = supplied.AccessKey, StorageNamespace = supplied.StorageNamespace,
                DevelopmentBuild = supplied.DevelopmentBuild, Country = country,
                AppVersion = supplied.AppVersion ?? Application.version, BuildVersion = supplied.BuildVersion ?? "",
                RequestTimeoutSeconds = Math.Max(1, Math.Min(120, supplied.RequestTimeoutSeconds)),
                DurationCheckpointSeconds = supplied.DurationCheckpointSeconds, CollectGooglePlayReferrer = supplied.CollectGooglePlayReferrer,
                LinkHeadersProvider = supplied.LinkHeadersProvider
            };
        }

        private static string Platform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return "android";
#elif UNITY_IOS && !UNITY_EDITOR
            return "ios";
#else
            return "other";
#endif
        }

        internal string Track(string name, IDictionary<string, string> props, DateTime occurredAt)
        {
            if (!ValidEventName(name)) throw new ArgumentException("Event name must contain 1 to 64 ASCII letters, digits, underscores, dots, colons or hyphens.");
            var record = new AcquisitionRecord { kind = "event", eventId = Guid.NewGuid().ToString(), name = name, occurredAt = AcquisitionJson.Utc(occurredAt) };
            if (props != null)
            {
                foreach (var prop in props)
                {
                    if (string.IsNullOrEmpty(prop.Key) || prop.Key.Length > 64 || prop.Value == null || prop.Value.Length > 1024)
                        throw new ArgumentException("Event props must have non-empty keys (max 64) and string values (max 1024).");
                    record.props.Add(new AcquisitionProp { key = prop.Key, value = prop.Value });
                }
                if (record.props.Count > 32) throw new ArgumentException("An event supports at most 32 props.");
            }
            state.queue.Add(record);
            Persist();
            return record.eventId;
        }

        internal string SubmitAttribution(string source, string rawReferrer, DateTime? clickAt, DateTime? beginAt)
        {
            if (source != "google_play_referrer" && source != "meta_install_referrer") throw new ArgumentException("Unsupported attribution evidence source.");
            if (rawReferrer == null || Encoding.UTF8.GetByteCount(rawReferrer) > 8192) throw new ArgumentException("Raw referrer must be present and at most 8192 UTF-8 bytes.");
            var record = AttributionRecord(source, rawReferrer, clickAt, beginAt);
            state.queue.Add(record);
            Persist();
            return record.eventId;
        }

        private static AcquisitionRecord AttributionRecord(string source, string raw, DateTime? click, DateTime? begin)
        {
            return new AcquisitionRecord
            {
                kind = "attribution", eventId = Guid.NewGuid().ToString(), occurredAt = AcquisitionJson.Utc(DateTime.UtcNow),
                source = source, rawReferrer = raw,
                referrerClickAt = click.HasValue ? AcquisitionJson.Utc(click.Value) : null,
                installBeginAt = begin.HasValue ? AcquisitionJson.Utc(begin.Value) : null
            };
        }

        internal void Link(string aliasId)
        {
            if (string.IsNullOrWhiteSpace(aliasId) || aliasId.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("A successfully identified alias ID is required.");
            foreach (var item in state.queue) if (item.kind == "link" && item.aliasId == aliasId && !item.rejected) return;
            state.queue.Add(new AcquisitionRecord { kind = "link", aliasId = aliasId, eventId = Guid.NewGuid().ToString(), occurredAt = AcquisitionJson.Utc(DateTime.UtcNow) });
            Persist();
        }

        internal void SetLinkHeadersProvider(Func<AcquisitionRequestContext, IDictionary<string, string>> provider)
        {
            options.LinkHeadersProvider = provider;
        }

        private static bool ValidEventName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64) return false;
            foreach (char c in name)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                    c == '_' || c == '.' || c == ':' || c == '-')) return false;
            return true;
        }

        private sealed class LinkHeadersUnavailableException : Exception { }

        private void ApplyLinkHeaders(UnityWebRequest request, string path, string body, string aliasId)
        {
            if (options.LinkHeadersProvider == null) return;
            try
            {
                var headers = options.LinkHeadersProvider(new AcquisitionRequestContext(UnityWebRequest.kHttpVerbPOST,
                    options.ApiUrl + path, path, body, aliasId));
                if (headers == null) throw new LinkHeadersUnavailableException();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var validated = new List<KeyValuePair<string, string>>();
                foreach (var header in headers)
                {
                    // Only the additional headers in the current backend authentication contract are accepted.
                    // Bearer, alias, environment, body and installation proof are always controlled by this module.
                    if (header.Key == null || !(header.Key.Equals("x-talo-signature", StringComparison.OrdinalIgnoreCase) ||
                        header.Key.Equals("x-talo-session", StringComparison.OrdinalIgnoreCase) ||
                        header.Key.Equals("x-talo-player", StringComparison.OrdinalIgnoreCase)) || !seen.Add(header.Key) ||
                        string.IsNullOrEmpty(header.Value) || header.Value.Length > 16384 || header.Value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                        throw new LinkHeadersUnavailableException();
                    validated.Add(header);
                }
                if (seen.Contains("x-talo-session") != seen.Contains("x-talo-player")) throw new LinkHeadersUnavailableException();
                foreach (var header in validated) request.SetRequestHeader(header.Key, header.Value);
            }
            catch (Exception) { throw new LinkHeadersUnavailableException(); }
        }

        internal void SetGameplayActive(bool active)
        {
            Capture();
            if (gameplay == active) return;
            if (gameplayDuration != null) gameplayDuration.Flush();
            gameplay = active;
            gameplayDuration = foreground && gameplay ? NewDuration("gameplay_duration", DateTime.UtcNow) : null;
        }

        private AcquisitionDuration NewDuration(string name, DateTime startedAt)
        {
            return new AcquisitionDuration(startedAt, delegate(DateTime start, DateTime end, double seconds)
            {
                Track(name, new Dictionary<string, string>
                {
                    { "seconds", seconds.ToString("0.#########", CultureInfo.InvariantCulture) },
                    { "startedAt", AcquisitionJson.Utc(start) }, { "endedAt", AcquisitionJson.Utc(end) }
                }, end);
            });
        }

        private void Capture()
        {
            long current = Stopwatch.GetTimestamp();
            double elapsed = Math.Max(0, (double)(current - lastTick) / Stopwatch.Frequency);
            lastTick = current;
            if (!foreground) return;
            foregroundDuration.Add(elapsed);
            if (gameplayDuration != null) gameplayDuration.Add(elapsed);
            checkpointElapsed += elapsed;
            if (checkpointElapsed >= options.DurationCheckpointSeconds) Checkpoint();
        }

        private void Checkpoint()
        {
            if (foregroundDuration != null) foregroundDuration.Flush();
            if (gameplayDuration != null) gameplayDuration.Flush();
            checkpointElapsed = 0;
        }

        private void SetForeground(bool active)
        {
            if (foreground == active) return;
            if (foreground) Checkpoint();
            foreground = active;
            lastTick = Stopwatch.GetTimestamp();
            DateTime now = DateTime.UtcNow;
            foregroundDuration = active ? NewDuration("foreground_duration", now) : null;
            gameplayDuration = active && gameplay ? NewDuration("gameplay_duration", now) : null;
            if (active) Track("app_open", null, now);
        }

        private void Update()
        {
            if (!Ready) return;
            try { Capture(); PollGoogleReferrer(); }
            catch (Exception ex) { Fault(ex); }
        }

        private void OnApplicationPause(bool value) { Lifecycle(value, null); }
        private void OnApplicationFocus(bool value) { Lifecycle(null, value); }
        private void Lifecycle(bool? newPaused, bool? newFocused)
        {
            if (!Ready) return;
            try
            {
                Capture();
                if (newPaused.HasValue) paused = newPaused.Value;
                if (newFocused.HasValue) focused = newFocused.Value;
                SetForeground(!paused && focused);
            }
            catch (Exception ex) { Fault(ex); }
        }

        private void OnApplicationQuit()
        {
            if (Ready) try { Capture(); Checkpoint(); } catch (Exception ex) { Fault(ex); }
            if (activeRequest != null) activeRequest.Abort();
        }

        private void OnDestroy()
        {
            if (Ready) try { Capture(); Checkpoint(); } catch (Exception ex) { Fault(ex); }
            if (activeRequest != null) activeRequest.Abort();
            if (PludibaseAcquisition.Host == this) PludibaseAcquisition.Host = null;
        }

        internal void Flush()
        {
            Capture(); Checkpoint();
            state.firstOpenRetryAt = "";
            foreach (var record in state.queue) if (!record.rejected) record.retryAt = "";
            Persist();
        }

        internal void RetryRejected()
        {
            foreach (var record in state.queue) { record.rejected = false; record.retryAt = ""; record.attempts = 0; }
            state.firstOpenRetryAt = "";
            Persist();
        }

        private void Persist()
        {
            if (storageFault) throw new IOException("Acquisition storage is unavailable.");
            try { storage.Save(state); }
            catch { storageFault = true; lastError = "storage_unavailable"; throw; }
        }

        private void Fault(Exception ex)
        {
            storageFault = true;
            lastError = "collection_stopped_" + ex.GetType().Name;
            // Never include request bodies, secrets or raw referrer in diagnostics/logs.
            UnityEngine.Debug.LogError("[pludibase] Acquisition collection stopped (" + ex.GetType().Name + "). Inspect local storage before restarting.");
        }

        internal AcquisitionDiagnostics GetDiagnostics()
        {
            var result = new AcquisitionDiagnostics
            {
                FirstOpenAcknowledged = state.firstOpenAcknowledged, LastHttpStatus = lastHttpStatus, LastError = lastError,
                AttributionStatus = state.attributionStatus, AttributionReason = state.attributionReason
            };
            foreach (var record in state.queue) { if (record.rejected) result.RejectedCount++; else result.PendingCount++; }
            if (state.googleReferrer != null) { result.GooglePlayReferrerStatus = state.googleReferrer.status; result.GooglePlayReferrerReason = state.googleReferrer.reason; }
            return result;
        }

        [Serializable] private sealed class FirstOpenResponse { public string installId = ""; public string firstOpenAt = ""; public bool created = false; }
        [Serializable] private sealed class EventsResponse { public int accepted = -1; public int duplicates = -1; }
        [Serializable] private sealed class LinkResponse { public bool linked = false; }
        [Serializable] private sealed class AttributionResponse { public string status = ""; public string reason = null; }

        private IEnumerator Drain()
        {
            while (Ready)
            {
                bool firstOpen = !state.firstOpenAcknowledged;
                var batch = new List<AcquisitionRecord>();
                if (firstOpen && !Due(state.firstOpenRetryAt)) { yield return new WaitForSecondsRealtime(0.5f); continue; }
                if (!firstOpen)
                {
                    int batchBytes = Encoding.UTF8.GetByteCount(AcquisitionJson.Events(state, batch));
                    foreach (var record in state.queue)
                    {
                        if (record.rejected) continue;
                        if (!Due(record.retryAt))
                        {
                            // Deferred linking/attribution must not gate independent installation activity.
                            // Keep their original proof and retry time while later eligible records upload.
                            if (record.kind == "link" || record.kind == "attribution") continue;
                            break;
                        }
                        if (batch.Count > 0 && (record.kind != "event" || batch[0].kind != "event")) break;
                        if (record.kind == "event")
                        {
                            int candidateBytes = batchBytes + Encoding.UTF8.GetByteCount(AcquisitionJson.Event(record)) + (batch.Count > 0 ? 1 : 0);
                            if (candidateBytes > MaxEventBatchBytes)
                            {
                                if (batch.Count > 0) break;
                                // Legacy/corrupt oversized records are preserved separately and cannot poison valid batches.
                                record.rejected = true; record.responseCode = 413;
                                lastError = "event_exceeds_client_batch_limit";
                                Persist();
                                continue;
                            }
                            batchBytes = candidateBytes;
                        }
                        batch.Add(record);
                        if (record.kind != "event" || batch.Count == 100) break;
                    }
                    if (batch.Count == 0) { yield return new WaitForSecondsRealtime(0.5f); continue; }
                }
                string kind = firstOpen ? "first-open" : batch[0].kind;
                string path = "/v1/acquisition/" + (kind == "event" ? "events" : kind);
                string body = firstOpen ? AcquisitionJson.FirstOpen(state) : kind == "event" ? AcquisitionJson.Events(state, batch) :
                    kind == "attribution" ? AcquisitionJson.Attribution(state, batch[0]) : AcquisitionJson.Link(state);
                using (var request = new UnityWebRequest(options.ApiUrl + path, UnityWebRequest.kHttpVerbPOST))
                {
                    activeRequest = request;
                    UnityWebRequestAsyncOperation operation = null;
                    string startError = "request_start_failed";
                    try
                    {
                        request.timeout = options.RequestTimeoutSeconds;
                        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Content-Type", "application/json");
                        request.SetRequestHeader("Authorization", "Bearer " + options.AccessKey);
                        request.SetRequestHeader("x-talo-dev-build", options.DevelopmentBuild ? "1" : "0");
                        if (kind == "link")
                        {
                            request.SetRequestHeader("x-talo-alias", batch[0].aliasId);
                            ApplyLinkHeaders(request, path, body, batch[0].aliasId);
                        }
                        operation = request.SendWebRequest();
                    }
                    catch (LinkHeadersUnavailableException) { startError = "link_headers_unavailable"; }
                    catch (Exception) { }
                    if (operation != null) yield return operation;
                    activeRequest = null;
                    if (!Ready) yield break;
                    lastHttpStatus = request.responseCode;
                    bool accepted = operation != null && request.result == UnityWebRequest.Result.Success && ValidResponse(kind, request.downloadHandler.text, batch.Count);
                    try
                    {
                        if (accepted)
                        {
                            lastError = "";
                            if (firstOpen) { state.firstOpenAcknowledged = true; state.firstOpenRetryAt = ""; }
                            else
                            {
                                if (kind == "attribution")
                                {
                                    var result = JsonUtility.FromJson<AttributionResponse>(request.downloadHandler.text);
                                    state.attributionStatus = result.status;
                                    state.attributionReason = result.reason;
                                }
                                foreach (var record in batch) state.queue.Remove(record);
                            }
                        }
                        else
                        {
                            lastError = operation == null ? startError : request.result == UnityWebRequest.Result.Success ? "invalid_acknowledgement" : "http_" + request.responseCode.ToString(CultureInfo.InvariantCulture);
                            bool rejected = request.responseCode >= 400 && request.responseCode < 500 && request.responseCode != 408 && request.responseCode != 429;
                            if (firstOpen)
                            {
                                state.firstOpenAttempts++;
                                state.firstOpenRetryAt = RetryAt(state.firstOpenAttempts);
                            }
                            else foreach (var record in batch)
                            {
                                record.attempts++; record.retryAt = RetryAt(record.attempts);
                                record.rejected = rejected; record.responseCode = request.responseCode;
                            }
                        }
                        Persist();
                    }
                    catch (Exception ex) { Fault(ex); yield break; }
                }
            }
        }

        private bool ValidResponse(string kind, string body, int count)
        {
            try
            {
                if (kind == "first-open")
                {
                    var value = JsonUtility.FromJson<FirstOpenResponse>(body);
                    return value != null && value.installId == state.installId && !string.IsNullOrEmpty(value.firstOpenAt);
                }
                if (kind == "event")
                {
                    var value = JsonUtility.FromJson<EventsResponse>(body);
                    return value != null && value.accepted >= 0 && value.duplicates >= 0 && (long)value.accepted + value.duplicates == count;
                }
                if (kind == "link") { var value = JsonUtility.FromJson<LinkResponse>(body); return value != null && value.linked; }
                var attribution = JsonUtility.FromJson<AttributionResponse>(body);
                return attribution != null && (attribution.status == "pending" || attribution.status == "attributed" || attribution.status == "unknown" || attribution.status == "unmapped");
            }
            catch (ArgumentException) { return false; }
        }

        private static bool Due(string value)
        {
            DateTime date;
            return string.IsNullOrEmpty(value) || !DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date) || date.ToUniversalTime() <= DateTime.UtcNow;
        }

        private static string RetryAt(int attempt)
        {
            // Bounded exponential backoff with jitter. Event payloads are never regenerated.
            double seconds = Math.Min(300, Math.Pow(2, Math.Min(attempt, 9))) * (0.8 + UnityEngine.Random.value * 0.4);
            return AcquisitionJson.Utc(DateTime.UtcNow.AddSeconds(seconds));
        }

        private void PollGoogleReferrer()
        {
            if (!options.CollectGooglePlayReferrer || state.googleReferrerCollected) return;
            if (!string.IsNullOrEmpty(referrerNonce))
            {
                if (Monotonic >= referrerDeadline)
                    ReferrerFailure("timeout", "Install Referrer callback timed out.", -1);
                return;
            }
            if (Monotonic < nextReferrerAttempt) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            referrerNonce = Guid.NewGuid().ToString("N");
            referrerDeadline = Monotonic + 15;
            try
            {
                using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var bridge = new AndroidJavaClass("com.pludibase.acquisition.InstallReferrerBridge"))
                    bridge.CallStatic("fetch", activity, gameObject.name, "OnGooglePlayInstallReferrer", referrerNonce);
            }
            catch (Exception) { ReferrerFailure("unavailable", "Install Referrer Java bridge or Google library is not installed.", -1); }
#else
            ReferrerFailure("unavailable", "Google Play Install Referrer requires an Android player installed through Google Play.", -1);
#endif
        }

        // Called by the opt-in Java bridge on Unity's main thread; a stale/duplicate callback cannot enqueue evidence.
        public void OnGooglePlayInstallReferrer(string payload)
        {
            if (!Ready || string.IsNullOrEmpty(referrerNonce)) return;
            GooglePlayReferrerResult result;
            try { result = JsonUtility.FromJson<GooglePlayReferrerResult>(payload); }
            catch (ArgumentException) { ReferrerFailure("error", "Malformed Install Referrer callback.", -1); return; }
            if (result == null || result.nonce != referrerNonce) return;
            if (result.status != "ok") { ReferrerFailure(result.status, result.reason, result.responseCode); return; }
            if (result.rawReferrer == null || Encoding.UTF8.GetByteCount(result.rawReferrer) > 8192) { ReferrerFailure("unsupported_evidence", "Install Referrer evidence exceeds the supported UTF-8 byte size.", result.responseCode); return; }
            try
            {
                state.queue.Add(AttributionRecord("google_play_referrer", result.rawReferrer, Unix(result.referrerClickTimestampSeconds), Unix(result.installBeginTimestampSeconds)));
                state.googleReferrer = result;
                state.googleReferrerCollected = true;
                referrerNonce = null;
                Persist();
            }
            catch (Exception ex) { Fault(ex); }
        }

        private static DateTime? Unix(long seconds)
        {
            if (seconds <= 0) return null;
            try { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds); }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        private void ReferrerFailure(string status, string reason, int responseCode)
        {
            state.googleReferrer = new GooglePlayReferrerResult { nonce = referrerNonce, status = status, reason = reason, responseCode = responseCode };
            referrerNonce = null;
            referrerAttempts++;
            nextReferrerAttempt = status == "unavailable" || status == "unsupported" || status == "permission_error" || status == "unsupported_evidence" ? double.PositiveInfinity :
                Monotonic + Math.Min(300, Math.Pow(2, Math.Min(referrerAttempts, 9)));
            Persist();
        }
    }
}
