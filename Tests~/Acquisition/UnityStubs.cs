// Test-only Unity surface. No network libraries are referenced; URLs outside the fixed test address throw.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Web.Script.Serialization;

namespace UnityEngine
{
    public class Object
    {
        internal static readonly List<Object> Objects = new List<Object>();
        public static T FindObjectOfType<T>() where T : Object { return Objects.Find(o => o is T) as T; }
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value)
        {
            var owner = value as GameObject;
            if (owner != null) foreach (var component in owner.Components.ToArray()) Destroy(component);
            var method = value.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method != null) method.Invoke(value, null);
            Objects.Remove(value);
        }
    }
    public class Component : Object { public GameObject gameObject; }
    public class MonoBehaviour : Component
    {
        public readonly List<IEnumerator> Coroutines = new List<IEnumerator>();
        public void StartCoroutine(IEnumerator routine) { Coroutines.Add(routine); }
    }
    public class GameObject : Object
    {
        public string name;
        internal readonly List<Component> Components = new List<Component>();
        public GameObject(string name) { this.name = name; Objects.Add(this); }
        public T AddComponent<T>() where T : Component
        {
            var component = (T)Activator.CreateInstance(typeof(T), true);
            component.gameObject = this; Components.Add(component); Objects.Add(component); return component;
        }
    }
    public static class Application { public static string persistentDataPath; public static string version = "test"; public static bool isFocused = true; }
    public static class Debug { public static bool isDebugBuild = true; public static void LogError(string message) { } public static void Log(string message) { } public static void LogWarning(string message) { } }
    public static class Random { public static float value { get { return 0.5f; } } }
    public sealed class WaitForSecondsRealtime { public WaitForSecondsRealtime(float seconds) { } }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType value) { } }
    public sealed class DefaultExecutionOrderAttribute : Attribute { public DefaultExecutionOrderAttribute(int value) { } }
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string value) { } }
    public static class PlayerPrefs { public static string GetString(string key, string fallback) { return fallback; } public static void SetString(string key, string value) { } public static void Save() { } }
    public class AndroidJavaObject : IDisposable { public T GetStatic<T>(string key) { return default(T); } public void CallStatic(string name, params object[] args) { } public void Dispose() { } }
    public sealed class AndroidJavaClass : AndroidJavaObject { public AndroidJavaClass(string name) { } }
    public static class JsonUtility
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        public static string ToJson(object value) { return Serializer.Serialize(value); }
        public static T FromJson<T>(string json) { return Serializer.Deserialize<T>(json); }
    }
}
namespace UnityEngine.Networking
{
    public sealed class UnityWebRequestAsyncOperation { public event Action<UnityWebRequestAsyncOperation> completed; public void CompleteForTest() { if (completed != null) completed(this); } }
    public sealed class UploadHandlerRaw { public byte[] data; public UploadHandlerRaw(byte[] data) { this.data = data; } }
    public sealed class DownloadHandlerBuffer { public string text; }
    public sealed class UnityWebRequest : IDisposable
    {
        public const string kHttpVerbPOST = "POST";
        public enum Result { Success, ConnectionError, ProtocolError }
        public Result result;
        public long responseCode;
        public int timeout;
        public UploadHandlerRaw uploadHandler;
        public DownloadHandlerBuffer downloadHandler;
        public string url;
        public string error = "test-only response";
        public readonly Dictionary<string, string> headers = new Dictionary<string, string>();
        public static readonly Queue<Action<UnityWebRequest>> Replies = new Queue<Action<UnityWebRequest>>();
        public static readonly List<UnityWebRequest> Sent = new List<UnityWebRequest>();
        public UnityWebRequest(string url, string method)
        {
            if (!url.StartsWith("http://127.0.0.1:1/v1/acquisition/", StringComparison.Ordinal))
                throw new InvalidOperationException("Production URL is prohibited by the test stub.");
            this.url = url;
        }
        public void SetRequestHeader(string key, string value) { headers[key] = value; }
        public UnityWebRequestAsyncOperation SendWebRequest()
        {
            Sent.Add(this);
            if (Replies.Count == 0) throw new InvalidOperationException("Unexpected request; real network is unavailable.");
            Replies.Dequeue()(this); return new UnityWebRequestAsyncOperation();
        }
        public void Abort() { }
        public void Dispose() { }
    }
}
