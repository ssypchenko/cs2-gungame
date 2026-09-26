using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;

namespace GunGame
{
    public partial class GunGame
    {
        private const string MotdInterfaceName = "Source2EngineToServerStringTable001";
        private const string MotdTableName = "InfoPanel";
        private const string MotdStringKey = "motd";

        private INetworkStringTableContainer? _motdNetworkStringTableContainer;

        private void PublishMotdUrl()
        {
            string motdFileName = ConVar.Find("motdfile")?.StringValue ?? "motd.txt";
            string motdPath = Path.Combine(Server.GameDirectory, "csgo", motdFileName);

            if (!File.Exists(motdPath))
            {
                Logger.LogWarning("[GunGame] MOTD file not found: {MotdPath}. Server Website link will not be published.", motdPath);
                return;
            }

            string url;
            try
            {
                url = File.ReadAllText(motdPath).Trim();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[GunGame] Failed to read MOTD file: {MotdPath}", motdPath);
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Logger.LogWarning("[GunGame] MOTD file must contain a single absolute http/https URL. Value: {MotdValue}", url);
                return;
            }

            Server.NextFrame(() =>
            {
                try
                {
                    _motdNetworkStringTableContainer ??=
                        new INetworkStringTableContainer(NativeAPI.GetValveInterface(0, MotdInterfaceName));

                    INetworkStringTable? table = _motdNetworkStringTableContainer.FindTable(MotdTableName);
                    if (table == null)
                    {
                        Logger.LogWarning("[GunGame] Failed to find network string table {MotdTableName}. Server Website link was not published.", MotdTableName);
                        return;
                    }

                    SetMotdValue(table, url);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "[GunGame] Failed to publish Server Website URL from {MotdPath}", motdPath);
                }
            });
        }

        private unsafe void SetMotdValue(INetworkStringTable table, string value)
        {
            byte[] message = Encoding.UTF8.GetBytes(value + "\0");

            fixed (byte* messagePtr = message)
            {
                SetStringUserDataRequest_t data;
                data.m_pRawData = messagePtr;
                data.m_cbDataSize = message.Length;

                if (table.AddString(true, MotdStringKey, ref data) == INetworkStringTable.INVALID_STRING_INDEX)
                {
                    Logger.LogWarning("[GunGame] Failed to add MOTD URL to the {MotdTableName} network string table.", MotdTableName);
                    return;
                }
            }

            Logger.LogInformation("[GunGame] Server Website URL published from {MotdPath}.",
                Path.Combine(Server.GameDirectory, "csgo", ConVar.Find("motdfile")?.StringValue ?? "motd.txt"));
        }
    }
}
