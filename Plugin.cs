using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Utils;
using CS2GamingAPIShared;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using static CounterStrikeSharp.API.Core.Listeners;

namespace ClutchExpert
{
    public class Plugin : BasePlugin, IPluginConfig<Configs>
    {
        public override string ModuleName => "The Clutch Expert Acheivement";
        public override string ModuleVersion => "1.0";

        private ICS2GamingAPIShared? _cs2gamingAPI { get; set; }
        public static PluginCapability<ICS2GamingAPIShared> _capability { get; } = new("cs2gamingAPI");
        public Configs Config { get; set; } = new Configs();
        public Dictionary<CCSPlayerController, PlayerClutchCount> _playerWinCount { get; set; } = new();
        public ClutchData ClutchDatas { get; set; } = new();
        public string? filePath { get; set; }
        public readonly ILogger<Plugin> _logger;

        public override void Load(bool hotReload)
        {
            RegisterListener<OnClientDisconnect>(OnClientDisconnect);
            InitializeData();
            ClutchDatas = new();
        }

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            _cs2gamingAPI = _capability.Get();
        }

        public Plugin(ILogger<Plugin> logger)
        {
            _logger = logger;
        }

        public void OnConfigParsed(Configs config)
        {
            Config = config;
        }

        public void InitializeData()
        {
            filePath = Path.Combine(ModuleDirectory, "playerdata.json");

            if (!File.Exists(filePath))
            {
                var empty = "{}";

                File.WriteAllText(filePath, empty);
                _logger.LogInformation("Data file is not found creating a new one.");
            }

            _logger.LogInformation("Found Data file at {0}.", filePath);
        }

        [GameEventHandler]
        public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            var client = @event.Userid;

            if (!IsValidPlayer(client))
                return HookResult.Continue;

            var steamID = client!.AuthorizedSteamID!.SteamId64;

            var data = GetPlayerData(steamID);


            if (data == null)
                _playerWinCount.Add(client!, new());

            else
            {
                var count = data.WinCount;
                var complete = data.Complete;

                if (data.TimeReset == DateTime.Today.ToShortDateString())
                {
                    count = 0;
                    complete = false;
                    Task.Run(async () => await SaveClientData(steamID, count, complete, true));
                }

                _playerWinCount.Add(client!, new(count, complete));
            }

            return HookResult.Continue;
        }

        public void OnClientDisconnect(int playerslot)
        {
            var client = Utilities.GetPlayerFromSlot(playerslot);

            if (!IsValidPlayer(client))
                return;

            var steamID = client!.AuthorizedSteamID!.SteamId64;
            var value = _playerWinCount[client].WinCount;
            var complete = _playerWinCount[client].Complete;

            Task.Run(async () => await SaveClientData(steamID, value, complete, !complete));

            _playerWinCount.Remove(client!);
        }

        [GameEventHandler]
        public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
        {
            var client = @event.Userid;

            AddTimer(0.1f, () =>
            {
                var ct = Utilities.GetPlayers().Where(player => player.PawnIsAlive && player.Team == CsTeam.CounterTerrorist && player != client);
                var t = Utilities.GetPlayers().Where(player => player.PawnIsAlive && player.Team == CsTeam.Terrorist && player != client);

                // check if it's null
                if (ClutchDatas._survivor == null)
                {
                    // if one of the ct is left then choose him
                    if (ct.Count() == 1)
                        ClutchDatas._survivor = ct.FirstOrDefault();

                    // if one of the t is left then choose him
                    else if (t.Count() == 1)
                        ClutchDatas._survivor = t.FirstOrDefault();

                    // after choose him make this true.
                    if (ClutchDatas._survivor != null)
                    {
                        //Server.PrintToChatAll($"Clutch Expert is activated, Survivor is {ClutchDatas._survivor.PlayerName}");
                        ClutchDatas._active = true;
                    }
                }

                // if clutcher is dead then deactivate things.
                if (ClutchDatas._survivor == client)
                {
                    //Server.PrintToChatAll($"Clutch Expert is deactivated, {ClutchDatas._survivor!.PlayerName} is dead");
                    ClutchDatas._active = false;
                }
            });

            return HookResult.Continue;
        }

        [GameEventHandler]
        public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
        {
            // delay a little bit for checking last death is not clutcher.
            AddTimer(0.5f, () =>
            {
                // if they still active then let's send count!
                if (ClutchDatas._active)
                {
                    //Server.PrintToChatAll($"{ClutchDatas._survivor!.PlayerName} is survived, Add score.");
                    CountWin(ClutchDatas._survivor!);
                }
            });

            return HookResult.Continue;
        }

        [GameEventHandler]
        public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            ClutchDatas._survivor = null;
            ClutchDatas._active = false;

            return HookResult.Continue;
        }

        public void CountWin(CCSPlayerController client)
        {
            if (!IsValidPlayer(client))
                return;

            if (!_playerWinCount.ContainsKey(client!))
                return;

            if (_playerWinCount[client!].Complete)
                return;

            _playerWinCount[client!].WinCount += 1;

            if (_playerWinCount[client!].WinCount >= Config.MaxClutchCount)
            {
                var steamid = client.AuthorizedSteamID?.SteamId64;
                Task.Run(async () => await TaskComplete(client!, (ulong)steamid!));
            }
        }

        public async Task TaskComplete(CCSPlayerController client, ulong steamid)
        {
            if (_playerWinCount[client].Complete)
                return;

            _playerWinCount[client].Complete = true;
            var response = await _cs2gamingAPI?.RequestSteamID(steamid!)!;
            if (response != null)
            {
                if (response.Status != 200)
                    return;

                Server.NextFrame(() =>
                {
                    client.PrintToChat($" {ChatColors.Green}[Acheivement]{ChatColors.Default} You acheive 'The Clutch Expert' (Winning by being only 1 survive in team for {Config.MaxClutchCount} round.)");
                    client.PrintToChat($" {ChatColors.Green}[Acheivement]{ChatColors.Default} {response.Message}");
                });

                await SaveClientData(steamid!, _playerWinCount[client].WinCount, true, true);
            }
        }

        private async Task SaveClientData(ulong steamid, int count, bool complete, bool settime)
        {
            var finishTime = DateTime.Today.ToShortDateString();
            var resetTime = DateTime.Today.AddDays(7.0).ToShortDateString();
            var steamKey = steamid.ToString();

            var data = new PlayerData(finishTime, resetTime, count, complete);

            var jsonObject = ParseFileToJsonObject();

            if (jsonObject == null)
                return;

            if (jsonObject.ContainsKey(steamKey))
            {
                jsonObject[steamKey].WinCount = count;
                jsonObject[steamKey].Complete = complete;

                if (settime)
                {
                    jsonObject[steamKey].TimeAcheived = finishTime;
                    jsonObject[steamKey].TimeReset = resetTime;
                }

                var updated = JsonConvert.SerializeObject(jsonObject, Formatting.Indented);
                await File.WriteAllTextAsync(filePath!, updated);
            }

            else
            {
                jsonObject.Add(steamKey, data);
                var updated = JsonConvert.SerializeObject(jsonObject, Formatting.Indented);
                await File.WriteAllTextAsync(filePath!, updated);
            }
        }

        private PlayerData? GetPlayerData(ulong steamid)
        {
            var jsonObject = ParseFileToJsonObject();

            if (jsonObject == null)
                return null;

            var steamKey = steamid.ToString();

            if (jsonObject.ContainsKey(steamKey))
                return jsonObject[steamKey];

            return null;
        }

        private async void RemovePlayerFromData(ulong steamid)
        {
            var jsonObject = ParseFileToJsonObject();

            if (jsonObject == null)
                return;

            var steamKey = steamid.ToString();

            if (jsonObject.ContainsKey(steamKey))
            {
                _logger.LogInformation("Successfully removed {0} from player data file.", steamKey);
                jsonObject.Remove(steamKey);
                var updated = JsonConvert.SerializeObject(jsonObject, Formatting.Indented);
                await File.WriteAllTextAsync(filePath!, updated);
            }

            else
                _logger.LogError("SteamID {0} is not existed!", steamKey);
        }

        private Dictionary<string, PlayerData>? ParseFileToJsonObject()
        {
            if (!File.Exists(filePath))
                return null;

            return JsonConvert.DeserializeObject<Dictionary<string, PlayerData>>(File.ReadAllText(filePath));
        }

        public bool IsValidPlayer(CCSPlayerController? client)
        {
            return client != null && client.IsValid && !client.IsBot;
        }
    }

    public class ClutchData
    {
        public ClutchData()
        {
            _survivor = null;
            _active = false;
        }

        public CCSPlayerController? _survivor { get; set; } = null;
        public bool _active { get; set; } = false;
    }
}
