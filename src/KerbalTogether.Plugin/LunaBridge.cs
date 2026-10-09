using System;
using System.Linq;
using System.Reflection;
using KerbalTogether.Core;

namespace KerbalTogether
{
    internal sealed class LunaBridge
    {
        private readonly PropertyInfo networkState;
        private readonly object query;
        private readonly MethodInfo updateOwner;
        private readonly MethodInfo unloadedOwner;
        private readonly PropertyInfo settings;
        private readonly PropertyInfo playerName;
        private readonly object api;
        private readonly MethodInfo send;
        private readonly object modEvent;
        private readonly Delegate callback;

        internal static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
        internal static object Singleton(Type type) => type.GetProperty("Singleton", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy).GetValue(null, null);

        public LunaBridge(Action<string, byte[]> receive)
        {
            networkState = Find("LmpClient.MainSystem").GetProperty("NetworkState");
            var lockType = Find("LmpClient.Systems.Lock.LockSystem");
            query = lockType.GetProperty("LockQuery").GetValue(null, null);
            updateOwner = query.GetType().GetMethod("GetUpdateLockOwner");
            unloadedOwner = query.GetType().GetMethod("GetUnloadedUpdateLockOwner");
            settings = Find("LmpClient.Systems.SettingsSys.SettingsSystem").GetProperty("CurrentSettings");
            playerName = settings.PropertyType.GetProperty("PlayerName");
            var apiType = Find("LmpClient.Systems.ModApi.ModApiSystem");
            api = Singleton(apiType);
            send = apiType.GetMethod("SendModMessage", new[] { typeof(string), typeof(byte[]), typeof(bool) });
            modEvent = Find("LmpClient.Events.ModApiEvent").GetField("onModMessageReceived").GetValue(null);
            if (modEvent == null) throw new InvalidOperationException("Luna events are still initializing.");
            var add = modEvent.GetType().GetMethod("Add");
            callback = Delegate.CreateDelegate(add.GetParameters()[0].ParameterType, receive.Target, receive.Method);
            add.Invoke(modEvent, new object[] { callback });
        }

        public bool Running => networkState.GetValue(null, null).ToString() == "Running";
        public string Player => (string)playerName.GetValue(settings.GetValue(null, null), null);
        public string Owner(Guid vessel) => (string)updateOwner.Invoke(query, new object[] { vessel }) ?? (string)unloadedOwner.Invoke(query, new object[] { vessel });
        public void Send(CompatibilityPacket packet) => send.Invoke(api, new object[] { CompatibilityPacket.Channel, packet.Encode(), false });
        public void Dispose() => modEvent.GetType().GetMethod("Remove").Invoke(modEvent, new object[] { callback });
    }
}
