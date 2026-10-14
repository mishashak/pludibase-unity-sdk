// Compile-only type surface. These methods never contact player services.
using System.Threading.Tasks;
namespace TaloGameServices
{
    public sealed class Alias { public int id = 42; }
    public sealed class PlayersSurface { public Task Identify(string service, string value) { return Task.FromResult(true); } }
    public sealed class EventsSurface
    {
        public Task Track(string name, params (string key, string value)[] props) { return Task.FromResult(true); }
        public Task Flush() { return Task.FromResult(true); }
    }
    public static class Talo
    {
        public static readonly Alias CurrentAlias = new Alias();
        public static readonly PlayersSurface Players = new PlayersSurface();
        public static readonly EventsSurface Events = new EventsSurface();
    }
}
