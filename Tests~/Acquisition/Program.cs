using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Pludibase;
using UnityEngine;
using UnityEngine.Networking;

internal static class Program
{
    private static int assertions;
    private static string scratch;
    private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    private static void Main(string[] args)
    {
        if (args.Length != 1 || args[0].IndexOf("acquisition-tests", StringComparison.OrdinalIgnoreCase) < 0)
            throw new ArgumentException("Provide an explicitly named acquisition-tests scratch directory.");
        scratch = Path.Combine(Path.GetFullPath(args[0]), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        DurationTests(); StorageTests(); LifecycleAndQueueTests(); LinkHeadersAndNameTests(); BatchByteTests();
        Console.WriteLine("PASS: " + assertions + " acquisition assertions; test-only stubs, zero network.");
    }

    private static void DurationTests()
    {
        var intervals = new List<Tuple<DateTime, DateTime, double>>();
        DateTime anchor = new DateTime(2026, 10, 8, 23, 59, 45, DateTimeKind.Utc);
        var duration = new AcquisitionDuration(anchor, delegate(DateTime start, DateTime end, double seconds) { intervals.Add(Tuple.Create(start, end, seconds)); });
        duration.Add(125); duration.Flush();
        Check(intervals.Count == 3, "Duration must split at UTC midnight and 60 seconds.");
        Check(intervals[0].Item3 == 15 && intervals[1].Item3 == 60 && intervals[2].Item3 == 50, "Split seconds differ.");
        double total = 0;
        foreach (var interval in intervals)
        {
            Check(interval.Item3 > 0 && interval.Item3 <= 60, "Duration interval is out of range.");
            Check(interval.Item1.Date == interval.Item2.AddTicks(-1).Date, "Duration crosses UTC midnight.");
            total += interval.Item3;
        }
        Check(total == 125, "Duration seconds were lost.");
        intervals.Clear();
        duration = new AcquisitionDuration(anchor.Date.AddDays(1).AddTicks(-1), delegate(DateTime start, DateTime end, double seconds) { intervals.Add(Tuple.Create(start, end, seconds)); });
        duration.Add(1.25); duration.Flush();
        Check(intervals.Count == 2 && intervals[0].Item2 == anchor.Date.AddDays(1), "Sub-millisecond midnight split failed.");
        bool invalid = false; try { duration.Add(double.NaN); } catch (ArgumentOutOfRangeException) { invalid = true; }
        Check(invalid, "NaN must be rejected.");
    }

    private static AcquisitionState State()
    {
        return new AcquisitionState
        {
            apiUrl = "http://127.0.0.1:1", developmentBuild = true,
            installId = Guid.NewGuid().ToString(), installationSecret = AcquisitionStorage.NewSecret(),
            firstOpenEventId = Guid.NewGuid().ToString(), firstOpenAt = AcquisitionJson.Utc(DateTime.UtcNow), platform = "other", country = "US"
        };
    }

    private static void StorageTests()
    {
        string path = Path.Combine(scratch, "storage");
        var storage = new AcquisitionStorage(path);
        Check(storage.Load() == null, "Fresh storage should have no installation.");
        var state = State(); storage.Save(state);
        state.queue.Add(new AcquisitionRecord { kind = "event", eventId = Guid.NewGuid().ToString(), name = "download_start", occurredAt = state.firstOpenAt }); storage.Save(state);
        var recovered = new AcquisitionStorage(path).Load();
        Check(recovered.installId == state.installId && recovered.installationSecret == state.installationSecret, "Installation identity changed on restart.");
        Check(recovered.firstOpenEventId == state.firstOpenEventId && recovered.firstOpenAt == state.firstOpenAt, "First-open retry changed identity/time.");
        Check(recovered.queue.Count == 1 && recovered.queue[0].eventId == state.queue[0].eventId, "Offline event changed on restart.");
        string file = Path.Combine(path, "installation.json");
        File.Copy(file, file + ".tmp", true); File.WriteAllText(file, "interrupted-write");
        recovered = new AcquisitionStorage(path).Load();
        Check(recovered.queue.Count == 1, "Newest checksummed tmp was not recovered.");
        File.WriteAllText(file + ".tmp", "bad"); File.WriteAllText(file + ".bak", "bad");
        bool failed = false; try { new AcquisitionStorage(path).Load(); } catch (IOException) { failed = true; }
        Check(failed, "Corrupt installation must fail closed.");
        Check(AcquisitionStorage.NewSecret().Length == 64, "Installation secret length differs.");
        string json = AcquisitionJson.FirstOpen(state);
        Check(json.Contains("\"country\":\"US\"") && json.Contains("\"platform\":\"other\""), "First-open contract fields missing.");
        Check(AcquisitionJson.Quote("\"\\\n\u0001") == "\"\\\"\\\\\\n\\u0001\"", "JSON escaping failed.");
    }

    private static void Invoke(object instance, string method, params object[] args)
    {
        instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(instance, args);
    }

    private static AcquisitionState CurrentState()
    {
        return (AcquisitionState)typeof(AcquisitionBehaviour).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(PludibaseAcquisition.Host);
    }

    private static void PumpToWait()
    {
        IEnumerator routine = PludibaseAcquisition.Host.Coroutines[0];
        for (int i = 0; i < 1000; i++)
        {
            if (!routine.MoveNext() || routine.Current is WaitForSecondsRealtime) return;
        }
        throw new Exception("Coroutine did not yield idle/backoff.");
    }

    private static string Body(UnityWebRequest request) { return Encoding.UTF8.GetString(request.uploadHandler.data); }
    private static void Reply(long code, Func<UnityWebRequest, string> body)
    {
        UnityWebRequest.Replies.Enqueue(delegate(UnityWebRequest request)
        {
            request.responseCode = code; request.result = code >= 200 && code < 300 ? UnityWebRequest.Result.Success : UnityWebRequest.Result.ProtocolError;
            request.downloadHandler.text = body(request);
        });
    }

    private static void LifecycleAndQueueTests()
    {
        Application.persistentDataPath = Path.Combine(scratch, "lifecycle");
        var options = new AcquisitionOptions { ApiUrl = "http://127.0.0.1:1", AccessKey = "TEST_ONLY_GAME_KEY", StorageNamespace = "tests", Country = "kr" };
        PludibaseAcquisition.Initialize(options);
        var host = PludibaseAcquisition.Host;
        var state = CurrentState();
        string id = state.installId, secret = state.installationSecret, firstAt = state.firstOpenAt, firstEvent = state.firstOpenEventId;
        Check(PludibaseAcquisition.InitReady && state.queue.Count == 1 && state.queue[0].name == "app_open", "Early initialization did not persist app_open.");
        PludibaseAcquisition.Initialize(options);
        Check(PludibaseAcquisition.Host == host && state.queue.Count == 1, "Repeated init created a duplicate collector/open.");
        typeof(PludibaseAcquisition).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        PludibaseAcquisition.Initialize(options);
        Check(PludibaseAcquisition.Host == host && state.queue.Count == 1, "Subsystem static reset duplicated an existing collector.");
        PludibaseAcquisition.SetGameplayActive(true); Thread.Sleep(15);
        Invoke(host, "OnApplicationPause", true); Invoke(host, "OnApplicationFocus", false);
        int pausedCount = state.queue.Count;
        Thread.Sleep(15); Invoke(host, "Update");
        Check(state.queue.Count == pausedCount, "Background tick recorded activity.");
        Invoke(host, "OnApplicationPause", false);
        Check(state.queue.Count == pausedCount, "Resumed while unfocused recorded a duplicate open.");
        Invoke(host, "OnApplicationFocus", true); Invoke(host, "OnApplicationFocus", true);
        int opens = state.queue.FindAll(r => r.name == "app_open").Count;
        Check(opens == 2, "Pause/focus combination generated duplicate app_open.");
        Thread.Sleep(10); PludibaseAcquisition.SetGameplayActive(false);
        Check(state.queue.Exists(r => r.name == "gameplay_duration") && state.queue.Exists(r => r.name == "foreground_duration"), "Gameplay/foreground duration missing.");
        string stableEvent = PludibaseAcquisition.Track("download_start", new Dictionary<string, string> { { "stage", "12" } });
        string stableTime = state.queue.Find(r => r.eventId == stableEvent).occurredAt;
        PludibaseAcquisition.LinkIdentifiedPlayer("42"); PludibaseAcquisition.LinkIdentifiedPlayer("42");
        Check(state.queue.FindAll(r => r.kind == "link").Count == 1, "Repeated pending link was not deduplicated.");
        // A lost first-open response leaves the original registration payload intact.
        Reply(503, r => "{}"); PumpToWait();
        string firstBody = Body(UnityWebRequest.Sent[0]);
        Check(!state.firstOpenAcknowledged && UnityWebRequest.Sent.Count == 1, "Events were sent before first-open ack.");
        PludibaseAcquisition.Flush();
        Reply(200, r => "{\"installId\":\"" + id + "\",\"firstOpenAt\":\"" + firstAt + "\",\"created\":false}");
        Reply(503, r => "{}"); PumpToWait();
        Check(firstBody == Body(UnityWebRequest.Sent[1]), "First-open retry payload changed.");
        Check(state.firstOpenAcknowledged && state.queue.Exists(r => r.eventId == stableEvent), "Failed batch was lost.");
        Check(UnityWebRequest.Sent[0].headers["x-talo-dev-build"] == "1" && !UnityWebRequest.Sent[0].headers.ContainsKey("x-talo-alias"), "Early headers require an alias or wrong environment.");
        // Restart with unacknowledged event batch on disk; IDs and original timestamps must survive.
        Invoke(host, "OnApplicationPause", true);
        UnityEngine.Object.Destroy(host.gameObject);
        Application.isFocused = false;
        PludibaseAcquisition.Initialize(options); state = CurrentState(); host = PludibaseAcquisition.Host;
        Check(state.installId == id && state.installationSecret == secret && state.firstOpenAt == firstAt && state.firstOpenEventId == firstEvent, "Restart changed installation credentials.");
        Check(state.queue.Find(r => r.eventId == stableEvent).occurredAt == stableTime, "Restart changed pending event timestamp.");
        PludibaseAcquisition.Flush();
        Reply(200, r => EventsAcknowledgement(r));
        Reply(403, r => "{}");
        Reply(200, r => EventsAcknowledgement(r));
        PumpToWait();
        Check(PludibaseAcquisition.Diagnostics.RejectedCount == 1 && PludibaseAcquisition.Diagnostics.PendingCount == 0, "Non-transient link rejection must be preserved and allow later events.");
        var sentLink = UnityWebRequest.Sent.Find(r => r.url.EndsWith("/link", StringComparison.Ordinal));
        Check(sentLink != null && sentLink.headers["x-talo-alias"] == "42", "Link alias header missing.");
        PludibaseAcquisition.RetryRejected();
        Reply(200, r => "{\"linked\":true}"); PumpToWait();
        Check(PludibaseAcquisition.Diagnostics.RejectedCount == 0 && state.queue.Count == 0, "Successful link retry did not remove acknowledged record.");
        Check(PludibaseAcquisition.Diagnostics.MetaReferrerStatus == "unavailable", "Unverified Meta provider must remain unavailable.");
        bool nameRejected = false;
        try { PludibaseAcquisition.Track(new string('a', 65)); } catch (ArgumentException) { nameRejected = true; }
        Check(nameRejected, "Over-limit event name must fail before enqueue.");
        bool rawRejected = false;
        try { PludibaseAcquisition.SubmitAttribution("google_play_referrer", new string('한', 3000)); } catch (ArgumentException) { rawRejected = true; }
        Check(rawRejected, "Referrer limit must count UTF-8 bytes, not characters.");
        bool propsRejected = false;
        var oversizedProps = new Dictionary<string, string>();
        for (int p = 0; p < 33; p++) oversizedProps[p.ToString(CultureInfo.InvariantCulture)] = "value";
        try { PludibaseAcquisition.Track("stage_start", oversizedProps); } catch (ArgumentException) { propsRejected = true; }
        Check(propsRejected, "Props above the backend limit must fail before enqueue.");
        string attributionId = PludibaseAcquisition.SubmitAttribution("meta_install_referrer", "TEST_ONLY_UNVERIFIED_EVIDENCE");
        Reply(200, r => "{\"status\":\"unknown\",\"reason\":\"meta_adapter_unavailable\"}"); PumpToWait();
        Check(!state.queue.Exists(r => r.eventId == attributionId) && PludibaseAcquisition.Diagnostics.AttributionStatus == "unknown", "Attribution ack/status was not preserved.");
        Check(PludibaseAcquisition.Diagnostics.MetaReferrerStatus == "unavailable", "Raw evidence incorrectly enabled the Meta provider.");
        string failedStartId = PludibaseAcquisition.Track("tutorial_start");
        UnityWebRequest.Replies.Enqueue(delegate(UnityWebRequest request) { throw new InvalidOperationException("Simulated request-start failure."); });
        PumpToWait();
        Check(PludibaseAcquisition.Diagnostics.LastError == "request_start_failed" && state.queue.Exists(r => r.eventId == failedStartId), "Request-start exception lost the durable queue.");
        PludibaseAcquisition.Flush();
        Reply(200, r => "{\"accepted\":0,\"duplicates\":0}"); PumpToWait();
        Check(PludibaseAcquisition.Diagnostics.LastError == "invalid_acknowledgement" && state.queue.Exists(r => r.eventId == failedStartId), "Invalid batch ack removed an event.");
        PludibaseAcquisition.Flush(); Reply(200, r => EventsAcknowledgement(r)); PumpToWait();
        Check(state.queue.Count == 0, "Valid retry did not drain the preserved event.");
        bool differentConfig = false;
        try { PludibaseAcquisition.Initialize(new AcquisitionOptions { ApiUrl = options.ApiUrl, AccessKey = options.AccessKey, StorageNamespace = "different" }); }
        catch (InvalidOperationException) { differentConfig = true; }
        Check(differentConfig && PludibaseAcquisition.Host == host, "Different settings must not replace a live collector.");
        bool forbidden = false;
        try { new UnityWebRequest("https://production.invalid/v1/acquisition/events", "POST"); } catch (InvalidOperationException) { forbidden = true; }
        Check(forbidden, "Test stub failed to prohibit production URLs.");
    }

    private static string EventsAcknowledgement(UnityWebRequest request)
    {
        var parsed = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Body(request));
        var events = (ArrayList)parsed["events"];
        // A simulated duplicate-only server acknowledgement exercises lost-response retry safety.
        return "{\"accepted\":0,\"duplicates\":" + events.Count.ToString(CultureInfo.InvariantCulture) + "}";
    }

    private static void LinkHeadersAndNameTests()
    {
        UnityEngine.Object.Destroy(PludibaseAcquisition.Host.gameObject);
        Application.persistentDataPath = Path.Combine(scratch, "authenticated-link");
        Application.isFocused = false;
        int mainThread = Thread.CurrentThread.ManagedThreadId;
        int calls = 0;
        string mode = "valid";
        string forbiddenHeader = "";
        var contexts = new List<AcquisitionRequestContext>();
        Func<AcquisitionRequestContext, IDictionary<string, string>> provider = delegate(AcquisitionRequestContext context)
        {
            Check(Thread.CurrentThread.ManagedThreadId == mainThread, "Link provider ran outside the main thread.");
            calls++; contexts.Add(context);
            if (mode == "unavailable") return null;
            if (mode == "switch_alias" && context.AliasId == "44") return null;
            if (mode == "throw") throw new InvalidOperationException("HEADER_CREDENTIAL_DO_NOT_LOG");
            if (mode == "forbidden") return new Dictionary<string, string> { { forbiddenHeader, "HEADER_CREDENTIAL_FORBIDDEN" } };
            if (mode == "duplicate") return new Dictionary<string, string> { { "x-talo-session", "HEADER_CREDENTIAL_SESSION" }, { "X-TALO-SESSION", "HEADER_CREDENTIAL_OTHER" } };
            if (mode == "partial") return new Dictionary<string, string> { { "x-talo-session", "HEADER_CREDENTIAL_SESSION" } };
            if (mode == "newline") return new Dictionary<string, string> { { "x-talo-signature", "HEADER_CREDENTIAL\r\nINJECTION" } };
            return new Dictionary<string, string>
            {
                { "x-talo-signature", "HEADER_CREDENTIAL_SIGNATURE_" + calls.ToString(CultureInfo.InvariantCulture) },
                { "x-talo-session", "HEADER_CREDENTIAL_SESSION_" + context.AliasId },
                { "x-talo-player", "HEADER_CREDENTIAL_PLAYER_" + context.AliasId }
            };
        };
        var options = new AcquisitionOptions
        {
            ApiUrl = "http://127.0.0.1:1", AccessKey = "TEST_ONLY_GAME_KEY", StorageNamespace = "auth-tests",
            LinkHeadersProvider = provider
        };
        PludibaseAcquisition.Initialize(options);
        var state = CurrentState();
        Reply(200, r => "{\"installId\":\"" + state.installId + "\",\"firstOpenAt\":\"" + state.firstOpenAt + "\",\"created\":true}");
        PumpToWait();
        Check(calls == 0, "Pre-identification first-open invoked the player credential provider.");

        PludibaseAcquisition.Track("Aa09_.:-");
        int validCount = state.queue.Count;
        foreach (string invalidName in new[] { "tutorial start", "튜토리얼", "stage/12", "line\nbreak", "event\u0000", "event😀" })
        {
            bool rejected = false;
            try { PludibaseAcquisition.Track(invalidName); } catch (ArgumentException) { rejected = true; }
            Check(rejected && state.queue.Count == validCount, "Invalid characters were persisted into the normal event batch.");
        }
        PludibaseAcquisition.Track(new string('a', 64));
        Reply(200, r => EventsAcknowledgement(r)); PumpToWait();
        Check(state.queue.Count == 0 && calls == 0, "Rejected event names poisoned valid events or invoked link auth.");

        PludibaseAcquisition.LinkIdentifiedPlayer("42");
        int sentStart = UnityWebRequest.Sent.Count;
        Reply(503, r => "{}"); PumpToWait();
        Check(calls == 1 && state.queue.Count == 1, "Transient link failure did not preserve its record.");
        PludibaseAcquisition.Flush(); Reply(200, r => "{\"linked\":true}"); PumpToWait();
        var first = UnityWebRequest.Sent[sentStart];
        var second = UnityWebRequest.Sent[sentStart + 1];
        Check(calls == 2 && contexts[0].Body == Body(first) && contexts[1].Body == Body(second), "Provider did not receive the exact outgoing body for every attempt.");
        Check(Body(first) == Body(second) && first.headers["x-talo-signature"] != second.headers["x-talo-signature"], "Retry regenerated install proof or reused a request signature.");
        Check(contexts[0].Method == "POST" && contexts[0].Path == "/v1/acquisition/link" && contexts[0].Url == first.url && contexts[0].AliasId == "42", "Provider request/alias context differs from HTTP.");
        Check(first.headers["x-talo-session"] == "HEADER_CREDENTIAL_SESSION_42" && first.headers["x-talo-player"] == "HEADER_CREDENTIAL_PLAYER_42", "Native player/session headers were omitted.");
        Check(first.headers["Authorization"] == "Bearer TEST_ONLY_GAME_KEY" && first.headers["x-talo-alias"] == "42" && first.headers["x-talo-dev-build"] == "1", "Core authentication values changed.");

        PludibaseAcquisition.LinkIdentifiedPlayer("43");
        foreach (string header in new[] { "Authorization", "X-TALO-ALIAS", "x-talo-dev-build", "installationSecret", "x-talo-installation-secret", "Content-Type", "Host" })
        {
            mode = "forbidden"; forbiddenHeader = header;
            int previousSends = UnityWebRequest.Sent.Count;
            PludibaseAcquisition.Flush(); PumpToWait();
            Check(UnityWebRequest.Sent.Count == previousSends && state.queue.Count == 1 && !state.queue[0].rejected,
                "Provider was allowed to override a protected request value or lost the pending link.");
            Check(PludibaseAcquisition.Diagnostics.LastError == "link_headers_unavailable", "Provider failure should expose only a fixed non-secret status.");
        }
        foreach (string unavailable in new[] { "unavailable", "throw", "duplicate", "partial", "newline" })
        {
            mode = unavailable;
            int previousSends = UnityWebRequest.Sent.Count;
            PludibaseAcquisition.Flush(); PumpToWait();
            Check(UnityWebRequest.Sent.Count == previousSends && state.queue.Count == 1 && PludibaseAcquisition.Diagnostics.LastError == "link_headers_unavailable",
                "Unavailable/throwing/duplicate header provider must defer without leaking credential values.");
        }
        mode = "valid";
        PludibaseAcquisition.SetLinkHeadersProvider(provider);
        PludibaseAcquisition.Flush(); Reply(200, r => "{\"linked\":true}"); PumpToWait();
        Check(state.queue.Count == 0, "Deferred link did not recover after valid headers became available.");
        mode = "switch_alias";
        PludibaseAcquisition.LinkIdentifiedPlayer("44");
        PludibaseAcquisition.LinkIdentifiedPlayer("45");
        string activityId = PludibaseAcquisition.Track("download_complete");
        int sendsBeforeSwitch = UnityWebRequest.Sent.Count;
        Reply(200, r => "{\"linked\":true}"); Reply(200, r => EventsAcknowledgement(r)); PumpToWait();
        Check(state.queue.Count == 1 && state.queue[0].kind == "link" && state.queue[0].aliasId == "44" && !state.queue[0].rejected,
            "A previous alias's unavailable credentials should preserve only that deferred link.");
        Check(UnityWebRequest.Sent.Count == sendsBeforeSwitch + 2 && UnityWebRequest.Sent[sendsBeforeSwitch].headers["x-talo-alias"] == "45" &&
            !state.queue.Exists(r => r.eventId == activityId), "Old-alias deferral blocked a current player's link or independent installation activity.");
        mode = "valid"; PludibaseAcquisition.Flush(); Reply(200, r => "{\"linked\":true}"); PumpToWait();
        Check(state.queue.Count == 0, "Preserved old-alias link did not resume when its authentication context returned.");

        mode = "throw";
        PludibaseAcquisition.LinkIdentifiedPlayer("46");
        var host = PludibaseAcquisition.Host;
        Invoke(host, "OnApplicationFocus", true);
        PludibaseAcquisition.SetGameplayActive(true); Thread.Sleep(15);
        Invoke(host, "OnApplicationPause", true); Invoke(host, "OnApplicationFocus", false);
        PludibaseAcquisition.SetGameplayActive(false);
        int sentBeforeGameplay = UnityWebRequest.Sent.Count;
        Reply(200, r => EventsAcknowledgement(r)); PumpToWait();
        Check(state.queue.Count == 1 && state.queue[0].kind == "link" && state.queue[0].aliasId == "46", "Throwing provider discarded its preserved link.");
        Check(UnityWebRequest.Sent.Count == sentBeforeGameplay + 1 && UnityWebRequest.Sent[sentBeforeGameplay].url.EndsWith("/events", StringComparison.Ordinal) &&
            Body(UnityWebRequest.Sent[sentBeforeGameplay]).Contains("\"name\":\"gameplay_duration\""), "Provider exception blocked real gameplay duration upload.");
        mode = "valid"; PludibaseAcquisition.Flush(); Reply(200, r => "{\"linked\":true}"); PumpToWait();
        Check(state.queue.Count == 0, "Deferred throwing-provider link could not recover after gameplay upload.");

        string deferredAttribution = PludibaseAcquisition.SubmitAttribution("google_play_referrer", "TEST_ONLY_DEFERRED_REFERRER");
        Invoke(host, "OnApplicationPause", false); Invoke(host, "OnApplicationFocus", true);
        Invoke(host, "OnApplicationPause", true); Invoke(host, "OnApplicationFocus", false);
        int sentBeforeAttribution = UnityWebRequest.Sent.Count;
        Reply(503, r => "{}"); Reply(200, r => EventsAcknowledgement(r)); PumpToWait();
        var deferredBody = Body(UnityWebRequest.Sent[sentBeforeAttribution]);
        Check(state.queue.Count == 1 && state.queue[0].eventId == deferredAttribution && state.queue[0].kind == "attribution" && !state.queue[0].rejected,
            "Delayed attribution proof should remain pending during its backoff.");
        Check(UnityWebRequest.Sent.Count == sentBeforeAttribution + 2 && Body(UnityWebRequest.Sent[sentBeforeAttribution + 1]).Contains("\"name\":\"app_open\""),
            "Attribution backoff blocked a later app_open upload.");
        string laterDownload = PludibaseAcquisition.Track("download_complete");
        Reply(200, r => EventsAcknowledgement(r)); PumpToWait();
        Check(state.queue.Count == 1 && !state.queue.Exists(r => r.eventId == laterDownload), "Pending attribution blocked newly added ordinary activity.");
        PludibaseAcquisition.Flush(); Reply(200, r => "{\"status\":\"unknown\",\"reason\":\"test_evidence_unverified\"}"); PumpToWait();
        Check(state.queue.Count == 0 && Body(UnityWebRequest.Sent[UnityWebRequest.Sent.Count - 1]) == deferredBody,
            "Control retry regenerated its original event/proof/time or did not acknowledge.");
        foreach (string file in Directory.GetFiles(Application.persistentDataPath, "installation.json*", SearchOption.AllDirectories))
            Check(!File.ReadAllText(file).Contains("HEADER_CREDENTIAL"), "Authentication header credentials were serialized to local storage.");
        Check(!PludibaseAcquisition.Diagnostics.LastError.Contains("HEADER_CREDENTIAL"), "Credential exception message escaped into diagnostics.");
    }

    private static Dictionary<string, string> LargeProps(char value, bool worstCaseKeys)
    {
        var props = new Dictionary<string, string>();
        for (int i = 0; i < 32; i++)
            props[worstCaseKeys ? new string('\0', 62) + i.ToString("00", CultureInfo.InvariantCulture) : "prop_" + i.ToString(CultureInfo.InvariantCulture)] = new string(value, 1024);
        return props;
    }

    private static void BatchByteTests()
    {
        var state = CurrentState();
        var ids = new HashSet<string>();
        var timestamps = new Dictionary<string, string>();
        for (int i = 0; i < 33; i++)
        {
            string id = PludibaseAcquisition.Track("batch.ascii", LargeProps('x', false));
            ids.Add(id); timestamps[id] = state.queue.Find(r => r.eventId == id).occurredAt;
        }
        Check(Encoding.UTF8.GetByteCount(AcquisitionJson.Events(state, state.queue)) > 1024 * 1024, "Regression fixture must exceed the server's one-MiB body limit.");
        int start = UnityWebRequest.Sent.Count;
        Reply(503, r => "{}"); PumpToWait();
        var failed = UnityWebRequest.Sent[start];
        Check(failed.uploadHandler.data.Length <= AcquisitionBehaviour.MaxEventBatchBytes && state.queue.Count == 33 && PludibaseAcquisition.Diagnostics.RejectedCount == 0,
            "Oversized queue was not split before sending, or transient failure rejected events.");
        string newId = PludibaseAcquisition.Track("new.event");
        ids.Add(newId); timestamps[newId] = state.queue.Find(r => r.eventId == newId).occurredAt;
        PludibaseAcquisition.Flush();
        for (int r = 0; r < 10; r++) Reply(200, request => EventsAcknowledgement(request));
        PumpToWait(); UnityWebRequest.Replies.Clear();
        Check(Body(failed) == Body(UnityWebRequest.Sent[start + 1]), "Retry changed the original bounded batch/event payload.");
        Check(state.queue.Count == 0 && PludibaseAcquisition.Diagnostics.RejectedCount == 0, "Byte-split batches did not drain successfully.");
        var delivered = new HashSet<string>();
        for (int r = start + 1; r < UnityWebRequest.Sent.Count; r++)
        {
            var request = UnityWebRequest.Sent[r];
            Check(request.uploadHandler.data.Length <= AcquisitionBehaviour.MaxEventBatchBytes, "Actual serialized UTF-8 batch exceeds the client ceiling.");
            foreach (Dictionary<string, object> item in EventObjects(request))
            {
                string id = (string)item["eventId"];
                Check(ids.Contains(id) && delivered.Add(id) && (string)item["occurredAt"] == timestamps[id], "Retry/splitting regenerated, duplicated or lost event identity/time.");
            }
        }
        Check(delivered.SetEquals(ids), "Byte splitting failed to acknowledge every queued event.");

        start = UnityWebRequest.Sent.Count;
        for (int i = 0; i < 7; i++) PludibaseAcquisition.Track("batch.korean", LargeProps('한', false));
        for (int r = 0; r < 10; r++) Reply(200, request => EventsAcknowledgement(request));
        PumpToWait(); UnityWebRequest.Replies.Clear();
        Check(UnityWebRequest.Sent.Count - start >= 2 && state.queue.Count == 0, "UTF-8 Korean bodies must be split even when string character count fits.");
        for (int r = start; r < UnityWebRequest.Sent.Count; r++)
            Check(UnityWebRequest.Sent[r].uploadHandler.data.Length <= AcquisitionBehaviour.MaxEventBatchBytes, "Unicode batch was measured in characters instead of UTF-8 bytes.");

        start = UnityWebRequest.Sent.Count;
        for (int i = 0; i < 3; i++) PludibaseAcquisition.Track("batch.escaped", LargeProps('\0', true));
        for (int r = 0; r < 10; r++) Reply(200, request => EventsAcknowledgement(request));
        PumpToWait(); UnityWebRequest.Replies.Clear();
        Check(UnityWebRequest.Sent.Count - start >= 2 && state.queue.Count == 0, "Every individually legal worst-case escaped event must fit and drain.");
        for (int r = start; r < UnityWebRequest.Sent.Count; r++)
            Check(UnityWebRequest.Sent[r].uploadHandler.data.Length <= AcquisitionBehaviour.MaxEventBatchBytes, "JSON escaping was omitted from the byte limit calculation.");
    }

    private static ArrayList EventObjects(UnityWebRequest request)
    {
        var parser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var parsed = parser.Deserialize<Dictionary<string, object>>(Body(request));
        return (ArrayList)parsed["events"];
    }
}
